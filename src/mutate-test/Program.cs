// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.MSBuild;

// TODO:
// * Fix dependent project limitation
// * Find better way of piping in references needed for compilation, and resolving what's needed to run
// * Try random stuff from https://github.com/dotnet/roslyn-sdk/tree/master/samples/CSharp/TreeTransforms
// * Consider making the mutated assemblies unloadable?
//
// See http://roslynquoter.azurewebsites.net/ for tool that shows how use roslyn APIs for C# syntax.
// Useful if you can express what you want in C# and need to see how to get a transform to create it for you.

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace MutateTest
{
    public class MutateTestException : Exception { }

    enum ExecutionResultKind
    {
        RanNormally,
        CompilationException,
        CompilationFailed,
        MutantCompilationException,
        MutantCompilationFailed,
        SizeTooLarge,
        RanTooLong,
        LoadFailed,
        ThrewException,
        BadExitCode,
        MutantLoadFailed,
        MutantThrewException,
        MutantBadExitCode,
        HasDependentProjects,
        NoFileAccess,
        SkipSpecialCase
    }

    struct ExecutionResult
    {
        public ExecutionResultKind kind;
        public int value;

        public bool Success => kind == ExecutionResultKind.RanNormally;
        public bool OriginalCompileFailed => kind == ExecutionResultKind.CompilationFailed || kind == ExecutionResultKind.CompilationException
            || kind == ExecutionResultKind.HasDependentProjects || kind == ExecutionResultKind.SkipSpecialCase;

        public bool CompileFailed => kind == ExecutionResultKind.CompilationFailed || kind == ExecutionResultKind.CompilationException
            || kind == ExecutionResultKind.MutantCompilationFailed || kind == ExecutionResultKind.MutantCompilationException;

        public bool AssemblyLoadFailed => kind == ExecutionResultKind.LoadFailed || kind == ExecutionResultKind.MutantLoadFailed;

        public bool OriginalRunFailed => kind == ExecutionResultKind.ThrewException || kind == ExecutionResultKind.BadExitCode;

        public bool NoMutationsAttempted => kind == ExecutionResultKind.SizeTooLarge || kind == ExecutionResultKind.RanTooLong;

        public override string ToString()
        {
            switch (kind)
            {
                case ExecutionResultKind.RanNormally: return "ran normally";
                case ExecutionResultKind.CompilationException: return "base compile caused exception";
                case ExecutionResultKind.CompilationFailed: return "base compilation failed";
                case ExecutionResultKind.MutantCompilationException: return "mutant compilation caused exception";
                case ExecutionResultKind.MutantCompilationFailed: return "mutant compilation failed";
                case ExecutionResultKind.SizeTooLarge: return $"test case size {value} bytes exceeds current size limit {Program.SizeLimit} bytes";
                case ExecutionResultKind.RanTooLong: return $"base compile or excution time {value} ms exceeds current time limit {Program.TimeLimit} ms";
                case ExecutionResultKind.LoadFailed: return "base assembly load failed";
                case ExecutionResultKind.ThrewException: return "base execution threw an exception";
                case ExecutionResultKind.BadExitCode: return $"base execution returned bad exit code {value}";
                case ExecutionResultKind.MutantThrewException: return "mutant execution threw exception";
                case ExecutionResultKind.MutantLoadFailed: return $"mutant assembly load failed";
                case ExecutionResultKind.MutantBadExitCode: return $"mutant execution returned bad exit code {value}";
                case ExecutionResultKind.HasDependentProjects: return "base project has dependent projects";
                case ExecutionResultKind.NoFileAccess: return "file access error";
                case ExecutionResultKind.SkipSpecialCase: return "test is on internal skip list";
            }

            return "unknown?";
        }
    }

    internal sealed class Program
    {
        public static int SizeLimit;
        public static int TimeLimit;
        public static bool Verbose;

        private static readonly CSharpCompilationOptions DebugOptions =
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, concurrentBuild: false, optimizationLevel: OptimizationLevel.Debug).WithAllowUnsafe(true);

        private static readonly CSharpCompilationOptions ReleaseOptions =
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, concurrentBuild: false, optimizationLevel: OptimizationLevel.Release).WithAllowUnsafe(true);

        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        private static readonly MetadataReference[] References =
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            // These two are needed to properly pick up System.Object when using methods on System.Console.
            // See here: https://github.com/dotnet/corefx/issues/11601
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime.Extensions")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime.InteropServices")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime.InteropServices.RuntimeInformation")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("mscorlib")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Numerics.Vectors")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Linq")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Collections")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Threading")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Threading.Tasks")).Location),
        };

        public readonly Random _random;
        private readonly List<Mutator> _mutators;
        private readonly MutateTestRootCommand _command;
        private readonly bool _quiet;

        public Program(MutateTestRootCommand command)
        {
            _command = command;
            _random = new Random(Get(command.Seed));
            _quiet = Get(_command.Quiet);

            SizeLimit = Get(_command.SizeLimit);
            TimeLimit = Get(_command.TimeLimit);
            Verbose = Get(_command.Verbose);

            _mutators = new List<Mutator>();

            SplitBlocksInTwo splitBlocks = new SplitBlocksInTwo(_random);
            _mutators.Add(splitBlocks);

            AddBlocks addBlocks = new AddBlocks();
            _mutators.Add(addBlocks);

            if (Get(_command.EHStress))
            {
                // Singletons
                Mutator tryCatch = new WrapBlocksInTryCatch();
                Mutator tryEmptyFinally = new WrapBlocksInTryEmptyFinally();
                Mutator emptyTryFinally = new WrapBlocksInEmptyTryFinally();
                Mutator moveToCatch = new MoveBlocksIntoCatchClauses();

                // Random @ runtime
                Mutator randomTryCatch = new RandomRuntimeMutator(tryCatch, 0.5);
                Mutator randomTryEmptyFinally = new RandomRuntimeMutator(tryEmptyFinally, 0.5);
                Mutator randomEmptyTryFinally = new RandomRuntimeMutator(emptyTryFinally, 0.5);
                Mutator randomMoveToCatch = new RandomRuntimeMutator(moveToCatch, 0.1);

                // In the below, we always use randomMoveToCatch instead of moveToCatch
                // to avoid blowing stack at runtime
                // (each try-catch uses ~8700K of stack space on x64 checked)
                //
                // Also, keep the probability of throws low since throws are very slow at runtime

                // Repeated
                Mutator tryCatchx2 = new RepeatMutator(tryCatch, 2);
                Mutator tryEmtpyFinallyx2 = new RepeatMutator(tryEmptyFinally, 2);
                Mutator emptyTryFinallyx2 = new RepeatMutator(emptyTryFinally, 2);
                Mutator randomMoveToCatchx2 = new RepeatMutator(randomMoveToCatch, 2);

                // Random @ mutation time
                Mutator tryCatchRandom = new RandomMutator(tryCatch, _random, 0.25);
                Mutator tryEmtpyFinallyRandom = new RandomMutator(tryEmptyFinally, _random, 0.25);
                Mutator emptyTryFinallyRandom = new RandomMutator(emptyTryFinally, _random, 0.25);
                Mutator moveToCatchRandom = new RandomMutator(randomMoveToCatch, _random, 0.25);

                // Alternative
                Mutator either12 = new RandomChoiceMutator(tryCatch, tryEmptyFinally, _random, 0.5);
                Mutator either34 = new RandomChoiceMutator(randomMoveToCatch, tryEmptyFinally, _random, 0.5);
                Mutator either1s = new RandomChoiceMutator(tryCatch, splitBlocks, _random, 0.5);
                Mutator either2s = new RandomChoiceMutator(randomMoveToCatch, splitBlocks, _random, 0.5);

                // Combination
                Mutator addSplit = new ComboMutator(addBlocks, splitBlocks);
                Mutator combo1 = new ComboMutator(tryEmptyFinally, tryCatch);
                Mutator combo2 = new ComboMutator(emptyTryFinally, tryCatch);
                Mutator combo3 = new ComboMutator(emptyTryFinally, tryEmptyFinally);
                Mutator combo4 = new ComboMutator(randomMoveToCatch, tryEmptyFinally);

                Mutator combo1s = new ComboMutator(addSplit, tryCatch);
                Mutator combo2s = new ComboMutator(addSplit, emptyTryFinally);
                Mutator combo3s = new ComboMutator(addSplit, tryEmptyFinally);
                Mutator combo4s = new ComboMutator(addSplit, randomMoveToCatch);

                // Combos of combos
                Mutator combo2s1 = new ComboMutator(combo2s, combo1);
                Mutator combo3s4 = new ComboMutator(combo3s, combo4);
                Mutator combo1s2 = new ComboMutator(combo1s, combo2);
                Mutator combo4s3 = new ComboMutator(combo4s, combo3);

                _mutators.AddRange(new Mutator[]
                {
                    addSplit,
                    tryCatch, tryCatchx2,
                    tryEmptyFinally, tryEmtpyFinallyx2,
                    emptyTryFinally, emptyTryFinallyx2,
                    randomTryCatch, randomTryEmptyFinally, randomEmptyTryFinally,
                    randomMoveToCatch, randomMoveToCatchx2,
                    tryCatchRandom, tryEmtpyFinallyRandom,
                    emptyTryFinallyRandom, moveToCatchRandom,
                    either12, either34, either1s, either2s,
                    combo1, combo2, combo3, combo4,
                    combo1s, combo2s, combo3s, combo4s,
                    combo2s1, combo3s4, combo1s2, combo4s3,
                });
            }
        }

        public static bool EnsureStack() => RuntimeHelpers.TryEnsureSufficientExecutionStack();

        private static bool isFirstRun = true;

        private T Get<T>(Option<T> option) => _command.Result.GetValue(option);

        private static int Main(string[] args) =>
            new CommandLineBuilder(new MutateTestRootCommand(args))
                .UseVersionOption("--version", "-v")
                .UseHelp()
                .UseParseErrorReporting()
                .Build()
                .Invoke(args);

        public int Run()
        {
            int total = 0;
            int skipped = 0;
            int failed = 0;
            int succeeded = 0;
            int variantTotal = 0;
            int variantFailedToCompile = 0;
            int variantFailedToRun = 0;

            if (Get(_command.Projects))
            {
                MSBuildLocator.RegisterDefaults();
            }

            string inputFilePath = _command.Result.GetValue(_command.InputFilePath);
            if (Get(_command.Recursive))
            {
                if (!Directory.Exists(inputFilePath))
                {
                    Console.WriteLine($"Unable to access directory '{inputFilePath}'");
                    return -1;
                }

                string suffix = Get(_command.Projects) ? ".csproj" : ".cs";
                string kind = Get(_command.Projects) ? "projects" : "test files";
                var inputFiles = Directory.EnumerateFiles(inputFilePath, "*", SearchOption.AllDirectories)
                                    .Where(s => (s.EndsWith(suffix)));

                Console.WriteLine($"Processing {inputFiles.Count()} {kind}\n");

                foreach (var subInputFile in inputFiles)
                {
                    total++;

                    int subVariantTotal = 0;
                    int subVariantFailedToCompile = 0;
                    int subVariantFailedToRun = 0;

                    ExecutionResult result;

                    if (Get(_command.Projects))
                    {
                        result = MutateOneProject(subInputFile, ref subVariantTotal, ref subVariantFailedToCompile, ref subVariantFailedToRun);
                    }
                    else
                    {
                        result = MutateOneTestFile(subInputFile, ref subVariantTotal, ref subVariantFailedToCompile, ref subVariantFailedToRun);
                    }

                    if (result.Success)
                    {
                        if (!Get(_command.OnlyFailures))
                        {
                            Console.WriteLine($"// {subInputFile}: {subVariantTotal} variants, all passed");
                        }
                        succeeded++;
                    }
                    else
                    {
                        if (result.OriginalCompileFailed || result.OriginalRunFailed || result.NoMutationsAttempted)
                        {
                            if (!Get(_command.OnlyFailures))
                            {
                                Console.WriteLine($"// {subInputFile}: {result}");
                            }
                            skipped++;
                        }
                        else
                        {
                            if (subVariantFailedToRun > 0)
                            {
                                failed++;
                            }

                            if ((subVariantFailedToRun > 0) || !Get(_command.OnlyFailures))
                            {
                                int successes = subVariantTotal - subVariantFailedToCompile - subVariantFailedToRun;
                                Console.WriteLine($"// {subInputFile}: {subVariantTotal} variants, {successes} passed" +
                                    $" [{subVariantFailedToCompile} did not compile, {subVariantFailedToRun} did not run correctly]");
                            }
                        }
                    }

                    variantTotal += subVariantTotal;
                    variantFailedToCompile += subVariantFailedToCompile;
                    variantFailedToRun += subVariantFailedToRun;
                }

                Console.WriteLine($"Final Results: {total} files, {succeeded} succeeded, {skipped} skipped, {failed} failed");
                Console.WriteLine($"{variantTotal} total variants attempted,  {variantFailedToCompile} did not compile, {variantFailedToRun} did not run.");

                if (failed == 0)
                {
                    return 100;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                ExecutionResult result;

                if (Get(_command.Projects))
                {
                    result = MutateOneProject(inputFilePath, ref variantTotal, ref variantFailedToCompile, ref variantFailedToRun);
                }
                else
                {
                    result = MutateOneTestFile(inputFilePath, ref variantTotal, ref variantFailedToCompile, ref variantFailedToRun);
                }

                if (result.Success)
                {
                    Console.WriteLine($"// {inputFilePath}: {variantTotal} variants, all passed");
                    succeeded++;
                    return 100;
                }

                if (result.OriginalCompileFailed || result.OriginalRunFailed || result.NoMutationsAttempted)
                {
                    // base case did not compile
                    Console.WriteLine($"// {inputFilePath}: {result}");
                }
                else
                {
                    int successes = variantTotal - variantFailedToCompile - variantFailedToRun;
                    Console.WriteLine($"// {inputFilePath}: {variantTotal} variants, {successes} passed" +
                        $" [{variantFailedToCompile} did not compile, {variantFailedToRun} did not run correctly]");
                }

                return -1;
            }
        }

        private ExecutionResult MutateOneTestFile(string testFile, ref int attempted, ref int failedToCompile, ref int failedToRun)
        {
            if (!_quiet)
            {
                Console.WriteLine("---------------------------------------");
                Console.WriteLine("// Original Program");
            }

            // Access input and build parse tree
            if (!File.Exists(testFile))
            {
                return new ExecutionResult() { kind = ExecutionResultKind.NoFileAccess };
            }

            string inputText = File.ReadAllText(testFile);
            SyntaxTree inputTree = CSharpSyntaxTree.ParseText(inputText,
                    path: testFile,
                    options: ParseOptions);

            SyntaxTree[] inputTrees = { inputTree };
            CSharpCompilation compilation = CSharpCompilation.Create("InputProgram", inputTrees, References, ReleaseOptions);

            return MutateOneCompilation(compilation, Path.GetFileName(testFile), ref attempted, ref failedToCompile, ref failedToRun);
        }

        private ExecutionResult MutateOneProject(string projectFile, ref int attempted, ref int failedToCompile, ref int failedToRun)
        {
            if (!_quiet)
            {
                Console.WriteLine("---------------------------------------");
                Console.WriteLine("// Original Program");
            }

            // Access input and build parse tree
            if (!File.Exists(projectFile))
            {
                return new ExecutionResult() { kind = ExecutionResultKind.NoFileAccess }; ;
            }

            using (var workspace = MSBuildWorkspace.Create())
            {
                var project = workspace.OpenProjectAsync(projectFile).Result;

                // We don't handle dependent projects properly yet.
                if (project.AllProjectReferences.Count() > 0)
                {
                    return new ExecutionResult() { kind = ExecutionResultKind.HasDependentProjects };
                }

                // Seems like we need to spoon feed in the assembly references here?
                // Probably missing some important step.
                var compilation = project.GetCompilationAsync().Result.AddReferences(References);

                if (!_quiet)
                {
                    if (Get(_command.ShowResults))
                    {
                        // Would be nice to show breakdown by file....
                        foreach (SyntaxTree s in compilation.SyntaxTrees)
                        {
                            Console.WriteLine(s.GetRoot().ToFullString());
                        }
                    }
                }

                return MutateOneCompilation((CSharpCompilation)compilation, Path.GetFileName(projectFile), ref attempted, ref failedToCompile, ref failedToRun);
            }
        }

        private ExecutionResult MutateOneCompilation(CSharpCompilation compilation, string name, ref int attempted, ref int failedToCompile, ref int failedToRun)
        {
            // Bail on some specific tests
            // We use substring match as there are often variants
            string[] exclusions = new string[]
            {
                "GitHub_25039",   // fatal error on 3.0p6 and before
                "b16102",         // does not exit with 100
                "structinregs",   // not sure why this fails, native dll dependency...?
                "virtcall",       // stack overflow with EH mutants...
                "stress1",
                "stress3",
                "skippage3",
                "skippage7",
                "b178119",
                "b178128",
                "b38269",
                "GitHub_19438",   // doesn't fail ? just super slow
            };

            if (exclusions.Any(x => name.Contains(x)))
            {
                return new ExecutionResult { kind = ExecutionResultKind.SkipSpecialCase };
            }

            Stopwatch s = new Stopwatch();
            s.Start();

            ExecutionResult inputResult = CompileAndExecute(compilation, name);

            s.Stop();

            if (!inputResult.Success)
            {
                return inputResult;
            }

            int inputSize = compilation.SyntaxTrees.Sum(x => x.Length);

            if (inputSize > Get(_command.SizeLimit))
            {
                return new ExecutionResult { kind = ExecutionResultKind.SizeTooLarge, value = inputSize };
            }

            // First run will be slower because of jitting (sigh)
            int timeLimit = Program.TimeLimit;
            if (isFirstRun)
            {
                timeLimit *= 3;
                isFirstRun = false;
            }

            if (s.ElapsedMilliseconds > timeLimit)
            {
                return new ExecutionResult { kind = ExecutionResultKind.RanTooLong, value = (int) s.ElapsedMilliseconds };
            }

            // Ok, we have a compile and runnable test case. Now, mess with it....
            int variantNumber = 0;

            ExecutionResult result = new ExecutionResult { kind = ExecutionResultKind.RanNormally };

            foreach (var mutator in _mutators)
            {
                attempted++;
                ExecutionResult mutationResult = ApplyMutations(variantNumber++, mutator, compilation);

                if (!mutationResult.Success)
                {
                    // count assembly load failures as compile failures for now.
                    if (mutationResult.CompileFailed || mutationResult.AssemblyLoadFailed)
                    {
                        failedToCompile++;
                    }
                    else
                    {
                        failedToRun++;
                    }

                    // Just report first failure seen, if any....
                    if (result.Success)
                    {
                        result = mutationResult;
                    }
                }
            }

            return result;
        }

        private ExecutionResult ApplyMutations(int variantNumber, Mutator m, CSharpCompilation compilation)
        {
            string shortTitle = $"Mutation [{variantNumber}]";
            string title = $"// {shortTitle}: {m.Name}";

            if (!_quiet)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------------");
                Console.WriteLine(title);
            }

            int totalTransformCount = 0;
            List<SyntaxTree> transformedTrees = new List<SyntaxTree>(compilation.SyntaxTrees.Count());

            foreach (SyntaxTree s in compilation.SyntaxTrees)
            {
                int transformCount = 0;
                SyntaxNode transformedRoot = m.Mutate(s.GetRoot(), out transformCount);
                totalTransformCount += transformCount;
                transformedTrees.Add(SyntaxTree(transformedRoot));
            }

            if (!_quiet)
            {
                Console.WriteLine($"// {shortTitle}: made {totalTransformCount} mutations");

                if (Get(_command.ShowResults))
                {
                    foreach (SyntaxTree s in transformedTrees)
                    {
                        Console.WriteLine(s.GetRoot().ToFullString());
                    }
                }
            }

            CSharpCompilation newCompilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(transformedTrees.ToArray());

            return CompileAndExecute(newCompilation, shortTitle, isMutant: true);
        }

        private ExecutionResult CompileAndExecute(CSharpCompilation compilation, string name, bool isMutant = false)
        {
            using (var ms = new MemoryStream())
            {
                EmitResult emitResult;
                try
                {
                    emitResult = compilation.Emit(ms);
                }
                catch (Exception ex)
                {
                    if (!_quiet)
                    {
                        Console.WriteLine($"// Compilation of '{name}' failed: {ex.Message}");
                    }
                    return new ExecutionResult() { kind = isMutant? ExecutionResultKind.MutantCompilationException : ExecutionResultKind.CompilationException };
                }

                if (!emitResult.Success)
                {
                    if (!_quiet)
                    {
                        Console.WriteLine($"// Compilation of '{name}' failed: {emitResult.Diagnostics.Length} errors");
                        foreach (var d in emitResult.Diagnostics)
                        {
                            Console.WriteLine(d);
                        }
                    }
                    return new ExecutionResult() { kind = isMutant ? ExecutionResultKind.MutantCompilationFailed : ExecutionResultKind.CompilationFailed };
                }

                if (!_quiet)
                {
                    Console.WriteLine($"// Compiled '{name}' successfully");
                }

                object inputResult = null;
                StreamWriter writer = null;

                try
                {
                    // Load up the test assembly.
                    // This sometimes fails because of anti-virus checks.. sigh
                    Assembly inputAssembly = Assembly.Load(ms.GetBuffer());
                    MethodInfo inputAssemblyEntry = inputAssembly.EntryPoint;

                    // Rebind console output to /dev/null
                    writer = new StreamWriter(Stream.Null);
                    Console.SetOut(writer);

                    // Invoke Main of test program
                    try
                    {
                        if (inputAssemblyEntry.GetParameters().Length == 0)
                        {
                            inputResult = inputAssemblyEntry.Invoke(null, new object[] { });
                        }
                        else
                        {
                            string[] arglist = new string[] { };
                            inputResult = inputAssemblyEntry.Invoke(null, new object[] { arglist });
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"// Execution of '{name}' failed with exception {e.Message}");
                        return new ExecutionResult() { kind = isMutant ? ExecutionResultKind.MutantThrewException : ExecutionResultKind.ThrewException };
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"// Assembly load of '{name}' failed with exception {e.Message}");
                    return new ExecutionResult() { kind = isMutant ? ExecutionResultKind.MutantLoadFailed : ExecutionResultKind.LoadFailed };
                }
                finally
                {
                    // Even though main has returned, the test may have spawned background work that is still running.
                    // So don't close the writer. Instead let GC clean it up.
                    //
                    // Restore standard output, if we redirected it
                    if (writer != null)
                    {
                        writer.Flush();
                        StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput());
                        standardOutput.AutoFlush = true;
                        Console.SetOut(standardOutput);
                    }
                }

                if (inputResult == null)
                {
                    inputResult = -1;
                }

                if ((int)inputResult != 100)
                {
                    Console.WriteLine($"// Execution of '{name}' failed (exitCode {inputResult})");
                    return new ExecutionResult() { kind = isMutant? ExecutionResultKind.MutantBadExitCode : ExecutionResultKind.BadExitCode, value = (int)inputResult };
                }

                if (!_quiet)
                {
                    Console.WriteLine($"// Execution of '{name}' succeeded (exitCode {inputResult})");
                }

                return new ExecutionResult() { kind = ExecutionResultKind.RanNormally };
            }
        }
    }

    // Base class for Mutations
    //
    // Mutations add "semantic preserving" constructs to
    // methods. This can be useful for stress testing the jit
    // or for getting an estimate of the perf impact of having
    // various constructs in code.

    public abstract class Mutator : CSharpSyntaxRewriter
    {
        public abstract string Name { get; }

        private int TransformCount { get; set; }

        public virtual IEnumerable<Mutator> Constituents()
        {
            var result = new List<Mutator>
            {
                this
            };
            return result;
        }

        protected int GetTransformCount()
        {
            return Constituents().Distinct().Sum(x => x.TransformCount);
        }

        protected virtual void Announce(SyntaxNode node, string message = "")
        {
            if (Program.Verbose)
            {
                var lineSpan = node.GetLocation().GetMappedLineSpan();
                Console.WriteLine($"// {Name} [{TransformCount}] @ lines {lineSpan.StartLinePosition.Line}-{lineSpan.EndLinePosition.Line} {message}");
            }
            TransformCount++;
        }

        protected void AnnounceSkip(SyntaxNode node, string message = "")
        {
            if (Program.Verbose)
            {
                var lineSpan = node.GetLocation().GetMappedLineSpan();
                Console.WriteLine($"// SKIP {Name} [{TransformCount}] @ lines {lineSpan.StartLinePosition.Line}-{lineSpan.EndLinePosition.Line} {message}");
            }
        }

        protected static bool IsInTryBlock(SyntaxNode baseNode)
        {
            SyntaxNode node = baseNode.Parent;
            while (node != null)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.TryStatement:
                    case SyntaxKind.UsingStatement:
                    case SyntaxKind.ForEachStatement:
                    case SyntaxKind.ForEachVariableStatement:
                        // Latter 3 may not create trys, but can
                        return true;
                    case SyntaxKind.SimpleLambdaExpression:
                    case SyntaxKind.ParenthesizedLambdaExpression:
                    case SyntaxKind.AnonymousMethodExpression:
                        // Stop looking.
                        return false;
                    case SyntaxKind.CatchClause:
                        // If we're in the catch of a try-catch-finally, then
                        // we're still in the scope of the try-finally handler.
                        if (((TryStatementSyntax)node.Parent).Finally != null)
                        {
                            return true;
                        }
                        goto case SyntaxKind.FinallyClause;
                    case SyntaxKind.FinallyClause:
                        // Skip past the enclosing try to avoid a false positive.
                        node = node.Parent;
                        node = node.Parent;
                        break;
                    default:
                        if (node is MemberDeclarationSyntax)
                        {
                            // Stop looking.
                            return false;
                        }
                        node = node.Parent;
                        break;
                }
            }

            return false;
        }

        // More generally, things that can't be in finallys
        protected static bool InvalidInFinally(SyntaxNode node)
        {
            // We could allow break so long as we see a "consuming" ancestor
            // before we see a block. Bail for now.
            return node.DescendantNodes(descendIntoTrivia: false).Any(
                x =>
                x.IsKind(SyntaxKind.ReturnStatement)
                || x.IsKind(SyntaxKind.ThrowStatement)
                || x.IsKind(SyntaxKind.ImplicitStackAllocArrayCreationExpression)
                || x.IsKind(SyntaxKind.StackAllocArrayCreationExpression)
                || x.IsKind(SyntaxKind.GotoStatement)
                || x.IsKind(SyntaxKind.BreakStatement)
                ); ;
        }

        // More generally, things that can't be in funclets
        protected static bool IsInvalidInCatchOrFinally(SyntaxNode node)
        {
            // Throw is legally ok, but we disallow to try and avoid causing stack overflow
            return node.DescendantNodes(descendIntoTrivia: false).Any(
                x => x.IsKind(SyntaxKind.ImplicitStackAllocArrayCreationExpression)
                || x.IsKind(SyntaxKind.StackAllocArrayCreationExpression)
                || x.IsKind(SyntaxKind.ThrowStatement)
                );
        }

        protected static bool IsEnclosedInLoop(SyntaxNode node)
        {
            return node.Ancestors().Any(
                x => x.IsKind(SyntaxKind.ForStatement)
                || x.IsKind(SyntaxKind.DoStatement)
                || x.IsKind(SyntaxKind.WhileStatement)
                || x.IsKind(SyntaxKind.ForEachStatement)
                );
        }

        protected static bool IsEnclosedInCatch(SyntaxNode node)
        {
            return node.Ancestors().Any(
                x => x.IsKind(SyntaxKind.CatchClause)
                );
        }

        protected static bool DefinesLabel(SyntaxNode node)
        {
            return node.DescendantNodes().Any(x => x.IsKind(SyntaxKind.LabeledStatement));
        }

    public SyntaxNode Mutate(SyntaxNode node, out int transformCount)
        {
            int initialCount = GetTransformCount();
            var result = Visit(node);
            transformCount = GetTransformCount() - initialCount;
            return result.NormalizeWhitespace();
        }
    }

    // Rewrite <block> as
    // try { <block> } catch (MutateTest.MutateTestException) { throw; }
    public class WrapBlocksInTryCatch : Mutator
    {
        public override string Name => "TryCatch";

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            node = (BlockSyntax)base.VisitBlock(node);

            Announce(node);

            var newNode = Block(
                        SingletonList<StatementSyntax>(
                            TryStatement(
                                SingletonList<CatchClauseSyntax>(
                                    CatchClause()
                                    .WithDeclaration(
                                        CatchDeclaration(
                                            QualifiedName(
                                                IdentifierName("MutateTest"),
                                                IdentifierName("MutateTestException"))))
                                    .WithBlock(
                                        Block(
                                            SingletonList<StatementSyntax>(
                                                ThrowStatement())))))
                            .WithBlock(node)));
            return newNode;
        }
    }

    // Rewrite <block> as
    // try { } finally { <block> }
    public class WrapBlocksInEmptyTryFinally : Mutator
    {
        public override string Name => "EmptyTryFinally";

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            node = (BlockSyntax)base.VisitBlock(node);

            if (InvalidInFinally(node))
            {
                AnnounceSkip(node);
                return node;
            }

            Announce(node);

            var newNode = Block(
                            SingletonList<StatementSyntax>(
                                TryStatement()
                                    .WithFinally(
                                        FinallyClause(node))))
                            .NormalizeWhitespace();
            return newNode;
        }
    }

    // Rewrite <block> as
    // try { <block> } finally { }
    public class WrapBlocksInTryEmptyFinally : Mutator
    {
        public override string Name => "TryEmptyFinally";

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            node = (BlockSyntax) base.VisitBlock(node);

            Announce(node);

            var newNode = Block(
                            SingletonList<StatementSyntax>(
                                TryStatement()
                                .WithBlock(node)
                                .WithFinally(FinallyClause(Block()))));
            return newNode;
        }
    }

    // Rewrite <block> as
    // try { throw MutateTestException; } catch (MutateTestException) { <block> }
    //
    // Note if <block> is frequently executed (say in a loop) this can introduce
    // considerable runtime overhead, as a throw/catch is slow.
    //
    // And it can also introduce considerable stack bloat, eg on x64 windows
    // a catch funclet is running with ~8700 bytes of runtime frames on the stack
    // between it and the throwing method. So recursive calls from catch
    // clauses are more prone to non-catchable stack overflows.
    //
    // We use EnsureStack to try avoid blowing the stack, and also won't
    // transform blocks that are already enclosed in a catch.
    // Even so stack overflows still happen, so it is best to only apply this
    // mutator sparingly (for instance: wrap either with one of the random mutations)
    public class MoveBlocksIntoCatchClauses : Mutator
    {
        public override string Name => "IntoCatch";

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            node = (BlockSyntax)base.VisitBlock(node);

            if (IsInvalidInCatchOrFinally(node))
            {
                AnnounceSkip(node);
                return node;
            }

            if (IsEnclosedInLoop(node))
            {
                AnnounceSkip(node);
                return node;
            }

            if (IsEnclosedInCatch(node))
            {
                AnnounceSkip(node);
                return node;
            }

            Announce(node);

            // only throw/catch if we have enough stack space
            ExpressionSyntax predicate =
                        InvocationExpression(
                            QualifiedName(
                                QualifiedName(
                                    IdentifierName("MutateTest"),
                                     IdentifierName("Program")),
                                IdentifierName("EnsureStack")));

            StatementSyntax thenClause =
                Block(
                  SingletonList<StatementSyntax>(
                            TryStatement(
                                SingletonList<CatchClauseSyntax>(
                                    CatchClause()
                                    .WithDeclaration(
                                        CatchDeclaration(
                                            QualifiedName(
                                                IdentifierName("MutateTest"),
                                                IdentifierName("MutateTestException"))))
                                    .WithBlock(node)))
                               .WithBlock(
                                   Block(
                                            SingletonList<StatementSyntax>(
                                                ThrowStatement(
                                                    ObjectCreationExpression(
                                                         QualifiedName(
                                                            IdentifierName("MutateTest"),
                                                            IdentifierName("MutateTestException")))
                                                    .WithArgumentList(
                                                     ArgumentList())))))));

            ElseClauseSyntax elseClause = ElseClause(node);

            var result = Block(IfStatement(predicate, thenClause, elseClause));

            return result;
        }
    }

    // Apply two mutators in sequence
    public class ComboMutator : Mutator
    {
        protected readonly Mutator _m1;
        protected readonly Mutator _m2;

        public ComboMutator(Mutator m1, Mutator m2)
        {
            _m1 = m1;
            _m2 = m2;
        }

        public override string Name => $"({_m1.Name})+({_m2.Name})";

        public override IEnumerable<Mutator> Constituents()
        {
            return base.Constituents().Concat(_m1.Constituents()).Concat(_m2.Constituents());
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            SyntaxNode result = _m1.VisitBlock(node);

            if (result is BlockSyntax)
            {
                result = _m2.VisitBlock((BlockSyntax)result);
            }

            return result;
        }
    }

    // Repeatedly apply a mutator
    public class RepeatMutator : Mutator
    {
        readonly Mutator _m;
        readonly int _n;
        public RepeatMutator(Mutator m, int n)
        {
            _m = m;
            _n = n;
        }

        public override string Name => $"({_m.Name})x{_n}";

        public override IEnumerable<Mutator> Constituents()
        {
            return base.Constituents().Concat(_m.Constituents());
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            SyntaxNode result = node;

            for (int i = 0; i < _n; i++)
            {
                if (result is BlockSyntax)
                {
                    var newResult = _m.VisitBlock((BlockSyntax)result);
                    if (newResult != result)
                    {
                        result = newResult;
                    }
                }
                else
                {
                    break;
                }
            }

            return result;
        }
    }

    // Randomly apply a mutator
    public class RandomMutator : Mutator
    {
        readonly Mutator _m;
        readonly Random _random;
        readonly double _p;

        public RandomMutator(Mutator m, Random r, double p)
        {
            _m = m;
            _random = r;
            _p = p;
        }

        public override String Name => $"({_m.Name})|()@{_p:F2}";

        public override IEnumerable<Mutator> Constituents()
        {
            return base.Constituents().Concat(_m.Constituents());
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            double x = _random.NextDouble();

            if (x < _p)
            {
                Announce(node, $"// {Name}: random choose x={x:F2} < p={_p:F2}");
                var result = _m.VisitBlock(node);
                return result;
            }
            else
            {
                AnnounceSkip(node, $"// {Name}: random skip x={x:F2} >= p={_p:F2}");
                return base.VisitBlock(node); ;
            }
        }
    }

    // Randomly choose between two mutators
    public class RandomChoiceMutator : ComboMutator
    {
        readonly Random _random;
        readonly double _p;

        public RandomChoiceMutator(Mutator m1, Mutator m2, Random r, double p) : base(m1, m2)
        {
            _random = r;
            _p = p;
        }

        public override string Name => $"({_m1.Name})|({_m2.Name})@{_p:F2}";

        public override IEnumerable<Mutator> Constituents()
        {
            return base.Constituents().Concat(_m1.Constituents()).Concat(_m2.Constituents());
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            double x = _random.NextDouble();
            SyntaxNode result = null;

            if (x < _p)
            {
                Announce(node, $"// {Name}: random choice x={x:F2} < p={_p:F2}: {_m1.Name}");
                result = _m1.VisitBlock(node);
            }
            else
            {
                Announce(node, $"// {Name}: random choice x={x:F2} >= p={_p:F2}: {_m2.Name}");
                result = _m2.VisitBlock(node);
            }

            return result;
        }
    }

    // Randomly execute a mutation at runtime
    //
    // Similar to the random mutator, but instead of randomly choosing to mutate or not,
    // creates an if-then-else that randomly chooses at runtime whether to run the original
    // code or the mutated code.
    public class RandomRuntimeMutator : Mutator
    {
        readonly Mutator _m;
        readonly double _p;

        public RandomRuntimeMutator(Mutator m, double p)
        {
            _m = m;
            _p = p;
        }

        public override String Name => $"({_m.Name})|()@[R:{_p:F2}]";

        public override IEnumerable<Mutator> Constituents()
        {
            return _m.Constituents();
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            Announce(node, $"// {Name}: random @ runtime with < p={_p:F2}");

            var newNode = (BlockSyntax) _m.VisitBlock(node);

            ExpressionSyntax predicate =
                BinaryExpression(
                    SyntaxKind.LessThanExpression,
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("MutateTest"),
                                        IdentifierName("Program")),
                                    IdentifierName("Random")),
                                IdentifierName("NextDouble"))),
                    LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        Literal(_p)));

            StatementSyntax thenClause = newNode;
            ElseClauseSyntax elseClause = ElseClause(node);

            var result = Block(IfStatement(predicate, thenClause, elseClause));

            return result;
        }
    }

    // Split a block with multiple statements into two blocks
    //
    // Useful as a preliminary step to enable more mutations.
    //
    // Randomly chooses the split point.
    public class SplitBlocksInTwo : Mutator
    {
        readonly Random _random;

        public override string Name => "SplitBlocks";

        public SplitBlocksInTwo(Random r)
        {
            _random = r;
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            node = (BlockSyntax) base.VisitBlock(node);

            int statementCount = node.Statements.Count;
            if (statementCount < 2)
            {
                AnnounceSkip(node);
                return node;
            }

            if (DefinesLabel(node))
            {
                AnnounceSkip(node);
                return node;
            }

            // randomly pick split point
            int beforeCount = 1 + _random.Next(statementCount - 2);
            int afterCount = statementCount - beforeCount;

            Announce(node, $"split [{ beforeCount}, {afterCount}]");

            var beforeNodes = node.Statements.Take(beforeCount);
            var afterNodes = node.Statements.TakeLast(afterCount);
            BlockSyntax afterBlock = Block(afterNodes);
            BlockSyntax resultBlock = Block(beforeNodes.Append(afterBlock));

            return resultBlock;
        }
    }

    // Put all isolated statements into blocks
    //
    // Useful as a preliminary step to enable more mutations.
    //
    // There are likely many more statement kinds we could add.
    public class AddBlocks : Mutator
    {
        public override string Name => "MakeBlocks";

        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            node = (ExpressionStatementSyntax)base.VisitExpressionStatement(node);

            if (node.Parent.IsKind(SyntaxKind.Block))
            {
                AnnounceSkip(node);
                return node;
            }

            Announce(node);
            var result = Block(node);
            return result;
        }

        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        {
            node = (ReturnStatementSyntax)base.VisitReturnStatement(node);

            if (node.Parent.IsKind(SyntaxKind.Block))
            {
                AnnounceSkip(node);
                return node;
            }

            Announce(node);
            var result = Block(node);
            return result;
        }
    }
}
