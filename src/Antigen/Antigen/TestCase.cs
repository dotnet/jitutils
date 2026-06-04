using Antigen.Config;
using Antigen.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Utils;
using Antigen.Compilation;
using Antigen.Execution;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;

namespace Antigen
{
    public class TestCase : IDisposable
    {
        #region Compiler options

        public enum CompilationType
        {
            Debug, Release
        };

        #endregion

        private readonly List<string> _knownDiffs = new List<string>()
        {
            "System.OverflowException: Value was either too large or too small for a Decimal.",
            "Value was either too large or too small for a Decimal.",
            "System.DivideByZeroException: Attempted to divide by zero.",
            "Attempted to divide by zero.",
            "Arithmetic operation resulted in an overflow.",
            "isCandidateVar(fieldVarDsc) == isMultiReg", // https://github.com/dotnet/runtime/issues/85628
            "curSize < maxSplitSize", // https://github.com/dotnet/runtime/issues/91251
        };

        private SyntaxNode testCaseRoot;

        private class UniqueIssueFile
        {
            public readonly int UniqueIssueId;
            public int HitCount { get; private set; }

            public UniqueIssueFile(int _uniqueIssueId, int hitCount)
            {
                UniqueIssueId = _uniqueIssueId;
                HitCount = hitCount;
            }

            public void IncreaseHitCount()
            {
                HitCount++;
            }
        }
        private static readonly ConcurrentDictionary<int, UniqueIssueFile> s_uniqueIssues = new();

        internal IList<Weights<int>> _numerals = new List<Weights<int>>()
        {
            new Weights<int>(int.MinValue, (double) PRNG.Next(1, 10) / 10000 ),
            new Weights<int>(int.MinValue + 1, (double)PRNG.Next(1, 10) / 10000 ),
            new Weights<int>(PRNG.Next(-100, -6), (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(-5, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(-2, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(-1, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(0, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(1, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(2, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(5, (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(PRNG.Next(6, 100), (double) PRNG.Next(1, 10) / 1000 ),
            new Weights<int>(int.MaxValue - 1, (double) PRNG.Next(1, 10) / 10000 ),
            new Weights<int>(int.MaxValue, (double) PRNG.Next(1, 10) / 10000 ),
        };

        internal static EEDriver s_Driver = null;
        internal static TestRunner s_TestRunner = null;
        internal static RunOptions s_RunOptions = null;

        private Compiler m_compiler { get; set; }

        internal ConfigOptions Config { get; private set; }
        public string Name { get; private set; }
        public AstUtils AstUtils { get; private set; }
        public bool ContainsVectorData { get; private set; }

        public TestCase(int testId, RunOptions runOptions)
        {
            Config = s_RunOptions.Configs[PRNG.Next(s_RunOptions.Configs.Count)];
            Config.UseSve = Sve.IsSupported && PRNG.Decide(Config.SveMethodsProbability);
            if (Config.UseSve || PRNG.Decide(Config.VectorDataProbability))
            {
                ContainsVectorData = true;
            }

            AstUtils = new AstUtils(this, new ConfigOptions(), null);
            Name = "TestClass" + testId;

            m_compiler = new Compiler(s_RunOptions.OutputDirectory);
        }

        public void Generate()
        {
            var klass = new TestClass(this, PreGenerated.MainClassName).Generate();
            var finalCode = PreGenerated.UsingDirective + klass.ToString();
            testCaseRoot = CSharpSyntaxTree.ParseText(finalCode).GetRoot();
        }

        public TestResult Verify()
        {
            SyntaxTree syntaxTree = testCaseRoot.SyntaxTree; // RslnUtilities.GetValidSyntaxTree(testCaseRoot);
            CompileResult compileResult = m_compiler.Compile(syntaxTree, Name);
            ExecuteResult executeResult = s_TestRunner.Execute(compileResult);

            switch(executeResult.Result)
            {
                case RunOutcome.CompilationError:
                    return TestResult.CompileError;
                case RunOutcome.AssertionFailure:
                {
                    var assertionMessage = executeResult.AssertionMessage;
                    var parsedAssertion = executeResult.ShortAssertionText;

                    if (!string.IsNullOrEmpty(parsedAssertion))
                    {
                        foreach (var knownError in _knownDiffs)
                        {
                            if (parsedAssertion.Contains(knownError))
                            {
                                return TestResult.Pass;
                            }
                        }
                        SaveTestCase(compileResult.AssemblyFullPath, testCaseRoot, executeResult.AssertionMessage, executeResult.EnvVars, parsedAssertion, $"{Name}-test-assertion");
                        return TestResult.Assertion;
                    }
                    else
                    {
                        foreach (var knownError in _knownDiffs)
                        {
                            if (assertionMessage.Contains(knownError))
                            {
                                //SaveTestCase(compileResult.AssemblyFullPath, testCaseRoot, null, null, test, testVariables, "Out of memory", $"{Name}-test-divbyzero");

                                return assertionMessage.Contains("System.OverflowException:") ? TestResult.Overflow : TestResult.DivideByZero;
                            }
                        }
                    }
                    return TestResult.Assertion;
                }
                case RunOutcome.OtherError:
                {
                    string errorMessage = executeResult.OtherErrorMessage;
                    if (errorMessage.Contains("System.OutOfMemoryException"))
                    {
                        return TestResult.OOM;
                    }
                    if (errorMessage == "Operation is not supported on this platform.")
                    {
                        // probably we are running with Altjit.

                        return TestResult.Pass;
                    }
                    foreach (var knownError in _knownDiffs)
                    {
                        if (errorMessage.Contains(knownError))
                        {
                            if (errorMessage.Contains("Attempted to divide by zero"))
                            {
                                return TestResult.DivideByZero;
                            }
                            else if (errorMessage.Contains("overflow") || errorMessage.Contains("too large or too small"))
                            {
                                return TestResult.Overflow;
                            }
                        }
                        return TestResult.OtherError;
                    }
                    var parsedError = RslnUtilities.ParseAssertionError(errorMessage);
                    parsedError = parsedError ?? errorMessage;

                    SaveTestCase(compileResult.AssemblyFullPath, testCaseRoot, executeResult.OtherErrorMessage, executeResult.EnvVars, parsedError, $"{Name}-test-error");
                    return TestResult.OtherError;
                }
                case RunOutcome.OutputMismatch:
                {
                    var outputDiff = executeResult.OtherErrorMessage;
                    SaveTestCase(compileResult.AssemblyFullPath, testCaseRoot, outputDiff, executeResult.EnvVars, "OutputMismatch", $"{ Name}-test-output-mismatch");
                    return TestResult.OutputMismatch;
                }
                case RunOutcome.Timeout:
                {
                    return TestResult.Timeout;
                }
                default:
                    return TestResult.Pass;
            }
        }

        private void SaveTestCase(
            string assemblyPath,
            SyntaxNode testCaseRoot,
            string testOutput,
            IReadOnlyList<Tuple<string, string>> envVars,
            string failureText,
            string testFileName)
        {
#if UNREACHABLE
            File.Move(assemblyPath, Path.Combine(s_runOptions.OutputDirectory, $"{Name}-fail.exe"), overwrite: true);
#endif

            StringBuilder fileContents = new StringBuilder();
            if (envVars != null)
            {
                fileContents.AppendLine($"// EnvVars: {string.Join("|", envVars.ToList().Select(x => $"{x.Item1}={x.Item2}"))}");
            }
            fileContents.AppendLine("//");
            fileContents.AppendLine(testCaseRoot.NormalizeWhitespace().SyntaxTree.GetText().ToString());
            fileContents.AppendLine("/*");

            fileContents.AppendFormat("Config: {0}", Config.Name).AppendLine();
            fileContents.AppendLine();
            fileContents.AppendLine("Environment:");
            fileContents.AppendLine();
            if (envVars != null)
            {
                foreach (var envVar in envVars)
                {
                    fileContents.AppendFormat("{0}={1}", envVar.Item1, envVar.Item2).AppendLine();
                }
            }
            fileContents.AppendLine();
            fileContents.AppendLine(testOutput);
            fileContents.AppendLine();
            fileContents.AppendLine();
            fileContents.AppendLine("GH title text:");
            fileContents.AppendLine(failureText);
            fileContents.AppendLine("*/");

            StringBuilder summaryContents = new StringBuilder(testOutput);
            string uniqueIssueDirName = null;
            int assertionHashCode = failureText.GetHashCode();
            string currentReproFile = $"{testFileName}.g.cs";
            lock (Program.s_spinLock)
            {
                UniqueIssueFile uniqueIssueFile;
                if (!s_uniqueIssues.ContainsKey(assertionHashCode))
                {
                    uniqueIssueFile = new UniqueIssueFile(s_uniqueIssues.Count, 0);
                    s_uniqueIssues[assertionHashCode] = uniqueIssueFile;
                }
                else
                {
                    uniqueIssueFile = s_uniqueIssues[assertionHashCode];
                }
                uniqueIssueFile.IncreaseHitCount();
                summaryContents.AppendLine();
                summaryContents.AppendLine();
                summaryContents.AppendLine($"HitCount: {uniqueIssueFile.HitCount}");

                // Create hash of testAssertion and copy files in respective bucket.
                uniqueIssueDirName = Path.Combine(s_RunOptions.OutputDirectory, $"UniqueIssue{uniqueIssueFile.UniqueIssueId}");

                if (!Directory.Exists(uniqueIssueDirName))
                {
                    Directory.CreateDirectory(uniqueIssueDirName);
                }

                File.WriteAllText(Path.Combine(uniqueIssueDirName, "summary.txt"), summaryContents.ToString());

                if (uniqueIssueFile.HitCount > 1)
                {
                    return;
                }

                string failFile = Path.Combine(uniqueIssueDirName, currentReproFile);
                File.WriteAllText(failFile, fileContents.ToString());
                Program.StartTrimmerForFile(failFile);
            }
        }

        public void Dispose()
        {
            this.testCaseRoot = null;
            this.m_compiler = null;
            GC.Collect();
        }
    }
}
