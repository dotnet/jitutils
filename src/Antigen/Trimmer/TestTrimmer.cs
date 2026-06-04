// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Compilation;
using Antigen.Execution;
using Antigen.Trimmer.Rewriters;
using Antigen.Trimmer.Rewriters.Expressions;
using Antigen.Trimmer.Rewriters.Statements;
using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Utils;

namespace Trimmer
{
    public struct ReproDetails
    {
        public Dictionary<string, string> envVars;
        public TestResult failureKind;
        public string assertionText;
    }

    public class TestTrimmer
    {
        const int TRIMMER_RESET_COUNT = 10;
        const int TRIMMER_TIMEOUT_IN_MINS = 30;
        const int TRIMMER_NEW_FOLDER_CHECK_IN_MINS = 5;
        const int SAVE_LKG_EVERY = 100;

        private SyntaxNode _treeToTrim;
        private ExecuteResult _lkgExecuteResult;
        private int _sizeOfTestFileToTrim;
        private static TestRunner _testRunner;
        private static int s_parentProcessId;
        private int _iterId = 0;
        private readonly CommandLineOptions _opts = null;
        private readonly Compiler _compiler;
        private readonly ReproDetails _reproDetails;
        private string _issueFolder;

        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<CommandLineOptions>(args).MapResult(Run, err => 1);
        }

        private static int Run(CommandLineOptions opts)
        {
            int.TryParse(opts.ParentPid, out s_parentProcessId);
            Task monitorTask = Task.Run(() => MonitorParentProcess());

            if (!string.IsNullOrEmpty(opts.ReproFile) && File.Exists(opts.ReproFile))
            {
                return RunOne(opts);
            }
            else if (!string.IsNullOrEmpty(opts.IssuesFolder) && Directory.Exists(opts.IssuesFolder))
            {
                return RunMany(opts);
            }
            throw new ArgumentException("Valid ReproFile or IssuesFolder needed.");
        }

        private static int RunMany(CommandLineOptions opts)
        {
            int uniqueId = 0;
            while (true)
            {
                string uniqueFolder = Path.Combine(opts.IssuesFolder, $"UniqueIssue{uniqueId}");
                if (!Directory.Exists(uniqueFolder))
                {
                    Thread.Sleep(TRIMMER_NEW_FOLDER_CHECK_IN_MINS * 60 * 1000); // wait for 10 minutes
                    continue;
                }

                string[] csFiles = Directory.GetFiles(uniqueFolder, "*.cs", SearchOption.AllDirectories);
                if (csFiles.Length == 0)
                {
                    Thread.Sleep(TRIMMER_NEW_FOLDER_CHECK_IN_MINS * 60 * 1000); // wait for 10 minutes
                    continue;
                }
                uniqueId++;

                // Create FileInfo objects and sort by file size
                var testCaseToTrim = csFiles
                    .Select(file => new FileInfo(file))
                    .OrderBy(fileInfo => fileInfo.Length) // Sort by file size
                    .First().FullName;

                TestTrimmer testTrimmer = new TestTrimmer(testCaseToTrim, uniqueFolder, opts);
                testTrimmer.Trim();
                testTrimmer.SaveRepro();
            }
        }

        private static int RunOne(CommandLineOptions opts)
        {
            string issueFolder = Path.GetDirectoryName(opts.ReproFile);
            TestTrimmer testTrimmer = new TestTrimmer(opts.ReproFile, issueFolder, opts);
            testTrimmer.Trim();
            testTrimmer.SaveRepro();
            return 0;
        }

        public TestTrimmer(string testFileToTrim, string issueFolder, CommandLineOptions opts)
        {
            if (!System.IO.File.Exists(testFileToTrim))
            {
                throw new Exception($"{testFileToTrim} doesn't exist.");
            }
            _opts = opts;
            _issueFolder = issueFolder;
            _compiler = new Compiler(_issueFolder);

            _reproDetails = ParseReproFile(testFileToTrim);
            EEDriver driver = EEDriver.GetInstance(opts.CoreRunPath,
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ExecutionEngine.dll"),
                () => _reproDetails.envVars);

            _testRunner = TestRunner.GetInstance(driver, opts.CoreRunPath);
        }

        /// <summary>
        ///     Returns a tuple of Baseline, Test environment variables
        /// </summary>
        /// <returns></returns>
        private ReproDetails ParseReproFile(string testFileToTrim)
        {
            var reproDetails = new ReproDetails();

            var fileContents = File.ReadAllText(testFileToTrim);
            _treeToTrim = CSharpSyntaxTree.ParseText(fileContents).GetRoot();
            _sizeOfTestFileToTrim = _treeToTrim.ToFullString().Length;

            fileContents = fileContents.Replace(Environment.NewLine, "\n");
            var fileContentLines = fileContents.Split("\n");

            foreach (var line in fileContentLines)
            {
                var lineContent = line.Trim();

                if (lineContent.StartsWith("// EnvVars:"))
                {
                    var testContents = lineContent.Replace("// EnvVars: ", string.Empty).Trim();
                    var testVariables = testContents.Split("|").ToList().ToDictionary(x => x.Split("=")[0], x => x.Split("=")[1]);

                    if (!string.IsNullOrEmpty(_opts.AltJitName))
                    {
                        testVariables["DOTNET_AltJitName"] = _opts.AltJitName;
                    }

                    if (!string.IsNullOrEmpty(_opts.AltJitMethodName))
                    {
                        testVariables["DOTNET_AltJit"] = _opts.AltJitMethodName;
                    }
                    reproDetails.envVars = testVariables;
                    break;
                }
            }

            if (reproDetails.envVars == null)
            {
                throw new Exception("EnvVars not present.");
            }

            string assertionMessage = RslnUtilities.ParseAssertionError(fileContents);
            if (!string.IsNullOrEmpty(assertionMessage))
            {
                reproDetails.assertionText = assertionMessage;
                reproDetails.failureKind = TestResult.Assertion;
                return reproDetails;
            }

            int debugCode = 0, releaseCode = 0;
            for (var i = fileContentLines.Length - 1; i >= 0; i--)
            {
                var line = fileContentLines[i].Trim();
                if (line.Contains("OutputMismatch") || line.Contains("Output mismatch"))
                {
                    reproDetails.failureKind = TestResult.OutputMismatch;
                    return reproDetails;
                }
                if (line.StartsWith("Debug: "))
                {
                    debugCode = int.Parse(line.Replace("Debug: ", string.Empty));
                    break;
                }
                else if (line.StartsWith("Release: "))
                {
                    releaseCode = int.Parse(line.Replace("Release: ", string.Empty));
                }
            }

            reproDetails.failureKind = debugCode != releaseCode ? TestResult.OutputMismatch : TestResult.Pass;
            return reproDetails;
        }

        public void Trim()
        {
            try
            {
                var trimTask = Task.Run(TrimTree);
                trimTask.Wait(TimeSpan.FromMinutes(TRIMMER_TIMEOUT_IN_MINS));
            }
            catch (Exception ex) {
                Console.WriteLine("Timed out." + ex.Message);
            }
        }

        /// <summary>
        /// 1. Trim as many statements as possible
        /// 2. Trim as many expressions as possible
        /// 3. If anything was trimmed, goto 1.
        /// </summary>
        public void TrimTree()
        {
            bool trimmedAtleastOne;
            do
            {
                trimmedAtleastOne = false;
                //trimmedAtleastOne |= TrimEnvVars();
                trimmedAtleastOne |= TrimStatements();
                trimmedAtleastOne |= TrimExpressions();

            } while (trimmedAtleastOne);
        }

        /// <summary>
        ///     Iterate through all the trimmers and trim the tree using them.
        ///     If at least one trimmer could trim the tree successfully, rerun all
        ///     the trimmers until there was no change made to the tree.
        /// </summary>
        /// <returns></returns>
        public bool TrimStatements()
        {
            List<SyntaxRewriter> trimmerList = new List<SyntaxRewriter>()
            {
                // From high to low

                // statements/blocks
                new MethodDeclStmtRemoval(),
                new ConsoleLogStmtRemoval(),
                new StructDeclStmtRemoval(),
                new BlockRemoval(),
                new DoWhileStmtRemoval(),
                new ForStmtRemoval(),
                new WhileStmtRemoval(),
                new TryCatchFinallyStmtRemoval(),
                new SwitchStmtRemoval(),
                new IfElseStmtRemoval(),
                new ExprStmtRemoval(),
                new LocalDeclStmtRemoval(),
            };

            bool trimmedAtleastOne = false;
            bool trimmedInCurrIter;

            do
            {
                trimmedInCurrIter = false;
                trimmedInCurrIter |= Trim(trimmerList);
                trimmedAtleastOne |= trimmedInCurrIter;
            } while (trimmedInCurrIter);

            return trimmedAtleastOne;
        }

        /// <summary>
        ///     Iterate through all the trimmers and trim the tree using them.
        ///     If at least one trimmer could trim the tree successfully, rerun all
        ///     the trimmers until there was no change made to the tree.
        /// </summary>
        /// <returns></returns>
        public bool TrimExpressions()
        {
            List<SyntaxRewriter> trimmerList = new List<SyntaxRewriter>()
            {
                // From high to low

                // expressions
                new InvocationExprRemoval(),
                new FieldExprRemoval(),
                new BinaryExpRemoval(),
                new AssignExprRemoval(),
                new MemberAccessExprRemoval(),
                new LiteralExprRemoval(),
                new IdentityNameExprRemoval(),
                new CastExprRemoval(),
                new ParenExprRemoval(),

            };

            bool trimmedAtleastOne = false;
            bool trimmedInCurrIter;

            do
            {
                trimmedInCurrIter = false;
                trimmedInCurrIter |= Trim(trimmerList);
                trimmedAtleastOne |= trimmedInCurrIter;
            } while (trimmedInCurrIter);

            return trimmedAtleastOne;
        }

        /// <summary>
        /// Trim the test case.
        /// </summary>
        private bool Trim(List<SyntaxRewriter> trimmerList)
        {
            SyntaxNode recentTree = _treeToTrim;
            CompileResult compileResult = _compiler.Compile(recentTree.SyntaxTree, "trimmer");
            ExecuteResult executeResult = _testRunner.Execute(compileResult);

            if (executeResult.Result == RunOutcome.CompilationError)
            {
                return false;
            }

            bool trimmedAtleastOne = false;
            bool trimmedInCurrIter;
            if (Verify($"trim{_iterId++}", recentTree) != _reproDetails.failureKind)
            {
                return false;
            }

            do
            {
                trimmedInCurrIter = false;
                int trimCount = 0;
                int trimmerId = 0;

                // pick category
                while (trimmerId < trimmerList.Count)
                {
TRIMMER_LOOP:
                    var trimmer = trimmerList[trimmerId];
                    SyntaxNode treeBeforeTrim = recentTree, treeAfterTrim = null;

                    // remove all
                    Console.Write($"{_iterId}. {trimmer.GetType()}");

                    int noOfNodes = 0;
                    bool gotException = false;
                    try
                    {
                        // For expression, it can be nested, so first count
                        // total expressions present.
                        if (trimmer is BinaryExpRemoval)
                        {
                            treeAfterTrim = trimmer.Visit(recentTree);
                            noOfNodes = trimmer.TotalVisited;
                            trimmer.RemoveAll();
                            treeAfterTrim = trimmer.Visit(recentTree);
                        }
                        // for statements, count while removing them all.
                        else
                        {
                            trimmer.RemoveAll();
                            treeAfterTrim = trimmer.Visit(recentTree);
                            noOfNodes = trimmer.TotalVisited;
                        }
                    }
                    catch (ArgumentNullException)
                    {
                        gotException = true;
                    }

                    bool isSameTree = false;
                    if (!gotException)
                    {
                        treeAfterTrim = RslnUtilities.GetValidSyntaxTree(treeAfterTrim, doValidation: false).GetRoot();
                        isSameTree = treeBeforeTrim.ToFullString() == treeAfterTrim.ToFullString();
                    }

                    // compile, execute and repro
                    if (!gotException &&
                        !isSameTree &&      // If they are same tree
                        trimmer.IsAnyNodeVisited && // We have visited and removed at least one node
                        Verify($"trim{_iterId++}", treeAfterTrim) == _reproDetails.failureKind)
                    {
                        // move to next trimmer
                        recentTree = treeAfterTrim;
                        Console.WriteLine(" - Success");
                        trimmedInCurrIter = true;
                        continue;
                    }
                    else
                    {
                        recentTree = treeBeforeTrim;
                        Console.WriteLine(" - Revert");
                    }

                    trimmer.Reset();
                    trimmer.RemoveOneByOne();

                    int nodeId = 0;
                    int localIterId = 0;
                    while (nodeId < noOfNodes)
                    {
                        Console.Write($"{_iterId}. {trimmer.GetType()}, localIterId = {localIterId++}");
                        trimmer.Reset();
                        trimmer.UpdateId(nodeId);

                        treeBeforeTrim = recentTree;
                        treeAfterTrim = null;

                        try
                        {
                            treeAfterTrim = trimmer.Visit(recentTree);
                        }
                        catch (ArgumentNullException)
                        {
                            gotException = true;
                        }

                        isSameTree = false;
                        if (!gotException)
                        {
                            treeAfterTrim = RslnUtilities.GetValidSyntaxTree(treeAfterTrim, doValidation: false).GetRoot();
                            isSameTree = treeBeforeTrim.ToFullString() == treeAfterTrim.ToFullString();
                        }

                        // compile, execute and repro
                        if (!gotException &&
                            !isSameTree &&
                            trimmer.IsAnyNodeVisited &&
                            Verify($"trim{_iterId++}", treeAfterTrim) == _reproDetails.failureKind)
                        {
                            // move to next trimmer
                            recentTree = treeAfterTrim;

                            if (trimmer is BinaryExpRemoval)
                            {
                                nodeId++;
                            }
                            else
                            {
                                // We have just removed a node, so decrease nodes count
                                noOfNodes--;
                            }

                            Console.WriteLine(" - Success");
                            trimmedInCurrIter = true;
                            trimmedAtleastOne = true;
                            trimCount++;

                            if ((trimmerId != 0) && (trimCount > TRIMMER_RESET_COUNT))
                            {
                                // If we have seen enough trimming for lower/smaller trimmer, reset to
                                // try out higher trimmer.
                                trimCount = 0;
                                trimmerId = 0;
                                goto TRIMMER_LOOP;
                            }
                        }
                        else
                        {
                            recentTree = treeBeforeTrim;

                            // Go to next nodeId
                            nodeId++;

                            Console.WriteLine(" - Revert");
                        }
                    }
                    trimmerId++;
                }
            } while (trimmedInCurrIter);

            return trimmedAtleastOne;
        }

        private TestResult Verify(string iterId, SyntaxNode programRootNode/*, bool skipBaseline*/)
        {
            CompileResult compileResult = _compiler.Compile(programRootNode.SyntaxTree, iterId);
            ExecuteResult executeResult = _testRunner.Execute(compileResult);

            TestResult validationResult;
            switch (executeResult.Result)
            {
                case RunOutcome.CompilationError:
                    validationResult = TestResult.CompileError;
                    break;
                case RunOutcome.AssertionFailure:
                    validationResult = _reproDetails.assertionText == executeResult.ShortAssertionText ?
                        TestResult.Assertion : TestResult.Pass;
                    break;
                case RunOutcome.OutputMismatch:
                    validationResult = TestResult.OutputMismatch;
                    break;
                case RunOutcome.OtherError:
                case RunOutcome.Timeout:
                case RunOutcome.Success:
                    validationResult = TestResult.Pass;
                    break;
                default:
                    throw new Exception("Unknown outcome.");
            }

            if (_iterId % SAVE_LKG_EVERY == 0)
            {
                SaveRepro();
            }

            if ((validationResult == TestResult.Assertion) || (validationResult == TestResult.OutputMismatch))
            {
                _treeToTrim = programRootNode;
                _lkgExecuteResult = executeResult;
            }

            return validationResult;
        }

        private void SaveRepro()
        {
            if (_treeToTrim == null)
            {
                return;
            }
            string programContents = _treeToTrim.ToFullString();
            programContents = Regex.Replace(programContents, @"[\r\n]*$", string.Empty, RegexOptions.Multiline);

            StringBuilder fileContents = new StringBuilder();
            fileContents.AppendLine("// Found by Antigen");
            fileContents.AppendLine($"// Reduced from {GetReadableFileSize(_sizeOfTestFileToTrim)} to {GetReadableFileSize(programContents.Length)}.");
            fileContents.AppendLine();
            fileContents.AppendLine(programContents);
            fileContents.AppendLine("/*");
            fileContents.AppendLine("Environment:");
            fileContents.AppendLine();
            if (_reproDetails.envVars != null)
            {
                foreach (var envVars in _reproDetails.envVars)
                {
                    fileContents.AppendFormat("set {0}={1}", envVars.Key, envVars.Value).AppendLine();
                }
            }
            fileContents.AppendLine();
            if (_lkgExecuteResult.Result == RunOutcome.AssertionFailure)
            {
                fileContents.AppendLine(_lkgExecuteResult.AssertionMessage);
            }
            else if (_lkgExecuteResult.Result == RunOutcome.OutputMismatch)
            {
                fileContents.AppendLine("Output mismatch.");
            }
            else if (_lkgExecuteResult.OtherErrorMessage != null)
            {
                fileContents.AppendLine(_lkgExecuteResult.OtherErrorMessage);
            }
            fileContents.AppendLine("*/");

            //TODO: Only if something was visited

            string failedFileName = "repro-lkg";
            string failFile = Path.Combine(_issueFolder, $"{failedFileName}.g.cs");
            File.WriteAllText(failFile, fileContents.ToString());
        }

        // Credits: https://stackoverflow.com/a/281679
        private static string GetReadableFileSize(double len)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
            // show a single decimal place, and no space.
            return String.Format("{0:0.##} {1}", len, sizes[order]);
        }


        /// <summary>
        /// Monitor parent process every 10 seconds and exit if it terminates
        /// </summary>
        private static void MonitorParentProcess()
        {
            if (s_parentProcessId == 0)
            {
                // No need to monitor parent process
                return;
            }
            try
            {
                while (true)
                {
                    // Check if the parent process is still running
                    Process.GetProcessById(s_parentProcessId);
                    Thread.Sleep(10000); // Check every 10 seconds
                }
            }
            catch (ArgumentException)
            {
                // Parent process is no longer running
                Console.WriteLine("Parent process terminated. Exiting...");
                Environment.Exit(0);
            }
        }
    }

    public class CommandLineOptions
    {
        [Option(shortName: 'c', longName: "CoreRun", Required = true, HelpText = "Path to CoreRun/CoreRun.exe.")]
        public string CoreRunPath { get; set; }

        [Option(shortName: 'p', longName: "ParentPid", Required = false, HelpText = "Antigen process id")]
        public string ParentPid { get; set; }

        [Option(shortName: 'o', longName: "IssuesFolder", Required = false, HelpText = "Path to folder where trimmed issue will be copied.")]
        public string IssuesFolder { get; set; }

        [Option(shortName: 'f', longName: "ReproFile", Required = false, HelpText = "Full path of the repro file.")]
        public string ReproFile { get; set; }

        [Option(shortName: 'j', longName: "AltJitName", Required = false, HelpText = "Name of altjit. By default, current OS/arch.")]
        public string AltJitName { get; set; }

        [Option(shortName: 'm', longName: "AltJitMethodName", Required = false, HelpText = "Name of method for altjit. By default, current OS/arch.")]
        public string AltJitMethodName { get; set; }
    }
}
