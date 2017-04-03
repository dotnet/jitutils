// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ManagedCodeGen
{
    public class Utility
    {
        public static string CombinePath(string basePath, string[] pathComponents)
        {
            string resultPath = basePath;
            foreach (var dir in pathComponents)
            {
                resultPath = Path.Combine(resultPath, dir);
            }
            return resultPath;
        }
    }

    public class AssemblyInfo
    {
        // Contains full path to assembly.
        public string Path { get; set; }

        // Contains relative path within output directory for given assembly.
        // This allows for different output directories per tool.
        public string OutputPath { get; set; }
    }

    public class jitdiff
    {
        // Supported commands.  List to view information from the CI system, and Copy to download artifacts.
        public enum Commands
        {
            Install,
            Diff,
            List
        }
        
        private static string s_asmTool = "jit-dasm";
        private static string s_analysisTool = "jit-analyze";
        private static string s_configFileName = "config.json";
        private static string s_configFileRootKey = "asmdiff";
        private static string[] s_defaultDiffDirectoryPath = { "bin", "diffs" };

        private static string GetJitLibraryName(string osMoniker)
        {
            switch (osMoniker)
            {
                case "Windows":
                    return "clrjit.dll";
                default:
                    return "libclrjit.so";
            }
        }

        private static string GetCrossgenExecutableName(string osMoniker)
        {
            switch (osMoniker)
            {
                case "Windows":
                    return "crossgen.exe";
                default:
                    return "crossgen";
            }
        }

        public class Config
        {
            private ArgumentSyntax _syntaxResult;
            private Commands _command = Commands.Diff;
            private bool _baseSpecified = false;    // True if user specified "--base" or "--base <path>"
            private bool _diffSpecified = false;    // True if user specified "--diff" or "--diff <path>"
            private string _basePath = null;        // Non-null if user specified "--base <path>"
            private string _diffPath = null;        // Non-null if user specified "--base <path>"
            private string _crossgenExe = null;
            private string _outputPath = null;
            private bool _noanalyze = false;
            private string _tag = null;
            private bool _sequential = false;
            private string _platformPath = null;
            private string _testPath = null;
            private string _baseRoot = null;
            private string _diffRoot = null;
            private string _arch = null;
            private string _build = null;
            private bool _corelib = false;
            private bool _frameworks = false;
            private bool _benchmarks = false;
            private bool _tests = false;
            private bool _gcinfo = false;
            private bool _verbose = false;
            private string _jobName;
            private string _number;
            private string _jitUtilsRoot;
            private string _rid;
            private string _platformName;
            private string _moniker;
            private string _branchName;

            private JObject _jObj;
            private bool _asmdiffLoaded = false;
            private bool _noJitUtilsRoot = false;
            private bool _validationError = false;

            public Config(string[] args)
            {
                // Get configuration values from JIT_UTILS_ROOT/config.json
                LoadFileConfig();

                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    // Diff command section.
                    syntax.DefineCommand("diff", ref _command, Commands.Diff, "Run asm diff.");
                    var baseOption = syntax.DefineOption("b|base", ref _basePath, false,
                        "The base compiler directory or tag. Will use crossgen or clrjit from this directory, " +
                        "depending on whether --crossgen is specified.");
                    var diffOption = syntax.DefineOption("d|diff", ref _diffPath, false,
                        "The diff compiler directory or tag. Will use crossgen or clrjit from this directory, " +
                        "depending on whether --crossgen is specified.");
                    syntax.DefineOption("crossgen", ref _crossgenExe,
                        "The crossgen compiler exe. When this is specified, will use clrjit from the --base and " +
                        "--diff directories with this crossgen.");
                    syntax.DefineOption("o|output", ref _outputPath, "The output path.");
                    syntax.DefineOption("noanalyze", ref _noanalyze, "Do not analyze resulting base, diff dasm directories. (By default, the directories are analyzed for diffs.)");
                    syntax.DefineOption("s|sequential", ref _sequential, "Run sequentially; don't do parallel compiles.");
                    syntax.DefineOption("t|tag", ref _tag, "Name of root in output directory. Allows for many sets of output.");
                    syntax.DefineOption("c|corelib", ref _corelib, "Diff System.Private.CoreLib.dll.");
                    syntax.DefineOption("f|frameworks", ref _frameworks, "Diff frameworks.");
                    syntax.DefineOption("benchmarks", ref _benchmarks, "Diff core benchmarks. Must pass --test_root.");
                    syntax.DefineOption("tests", ref _tests, "Diff all tests. Must pass --test_root.");
                    syntax.DefineOption("gcinfo", ref _gcinfo, "Add GC info to the disasm output.");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");
                    syntax.DefineOption("core_root", ref _platformPath, "Path to test CORE_ROOT.");
                    syntax.DefineOption("test_root", ref _testPath, "Path to test tree. Use with --benchmarks or --tests.");
                    syntax.DefineOption("base_root", ref _baseRoot, "Path to root of base dotnet/coreclr repo.");
                    syntax.DefineOption("diff_root", ref _diffRoot, "Path to root of diff dotnet/coreclr repo.");
                    syntax.DefineOption("arch", ref _arch, "Architecture to diff (x86, x64).");
                    syntax.DefineOption("build", ref _build, "Build flavor to diff (checked, debug).");

                    // List command section.
                    syntax.DefineCommand("list", ref _command, Commands.List,
                        "List defaults and available tools in " + s_configFileName + ".");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");

                    // Install command section.
                    syntax.DefineCommand("install", ref _command, Commands.Install, "Install tool in " + s_configFileName + ".");
                    syntax.DefineOption("j|job", ref _jobName, "Name of the job.");
                    syntax.DefineOption("n|number", ref _number, "Job number.");
                    syntax.DefineOption("b|branch", ref _branchName, "Name of branch.");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");

                    _baseSpecified = baseOption.IsSpecified;
                    _diffSpecified = diffOption.IsSpecified;
                });

                SetRID();

                SetDefaults();

                Validate();

                ExpandToolTags();

                if (_command == Commands.Diff)
                {
                    // Do additional initialization relevant for just the "diff" command.

                    DeriveOutputTag();

                    // Now that output path and tag are guaranteed to be set, update
                    // the output path to included the tag.
                    _outputPath = Path.Combine(_outputPath, _tag);
                }
            }

            private void SetRID()
            {
                // Extract system RID from dotnet cli
                List<string> commandArgs = new List<string> { "--info" };
                CommandResult result = TryCommand("dotnet", commandArgs, true);

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("dotnet --info returned non-zero");
                    Environment.Exit(-1);
                }

                var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    Regex ridPattern = new Regex(@"RID:\s*([A-Za-z0-9\.-]*)$");
                    Match ridMatch = ridPattern.Match(line);
                    if (ridMatch.Success)
                    {
                        _rid = ridMatch.Groups[1].Value;
                        continue;
                    }

                    Regex platPattern = new Regex(@"Platform:\s*([A-Za-z0-9]*)$");
                    Match platMatch = platPattern.Match(line);
                    if (platMatch.Success)
                    {
                        _platformName = platMatch.Groups[1].Value;
                        continue;
                    }
                }

                switch (_platformName)
                {
                    case "Windows":
                    case "Linux":
                        {
                            _moniker = _platformName;
                        }
                        break;
                    case "Darwin":
                        {
                            _moniker = "OSX";
                        }
                        break;
                    default:
                        {
                            Console.WriteLine("No platform mapping!");
                            _moniker = "bogus";
                        }
                        break;
                }
            }

            private string GetBuildOS()
            {
                switch (_platformName)
                {
                    case "Windows":
                        return "Windows_NT";
                    case "Linux":
                        return "Linux";
                    case "Darwin":
                        return "OSX";
                    default:
                        Console.Error.WriteLine("No platform mapping!");
                        return null;
                }
            }

            private void SetDefaults()
            {
                // Figure out what we need to set from defaults.

                bool needOutputPath = (_outputPath == null);                            // We need to find --output
                bool needCoreRoot = (_platformPath == null);                            // We need to find --core_root
                bool needCrossgen = (_crossgenExe == null);                             // We need to find --crossgen
                bool needBasePath = _baseSpecified && (_basePath == null);              // We need to find --base
                bool needDiffPath = _diffSpecified && (_diffPath == null);              // We need to find --diff
                bool needTestTree = (Benchmarks || DoTestTree) && (_testPath == null);  // We need to find --test_root

                bool needDiffRoot = (_diffRoot == null) &&
                    (needOutputPath || needCoreRoot || needCrossgen || needDiffPath || needTestTree);

                // If --diff_root wasn't specified, see if we can figure it out from the current directory
                // using git.
                //
                // NOTE: we shouldn't do this unless it will be used, that is, if there are some
                // arguments that are currently unspecified and need this to compute their default.
                //
                if (needDiffRoot)
                {
                    _diffRoot = GetRepoRoot();
                }

                if ((_outputPath == null) && (_diffRoot != null))
                {
                    _outputPath = Utility.CombinePath(_diffRoot, s_defaultDiffDirectoryPath);
                    PathUtility.EnsureDirectoryExists(_outputPath);

                    Console.WriteLine("Using --output={0}", _outputPath);
                }

                if (needCoreRoot || needCrossgen || needBasePath || needDiffPath || needTestTree)
                {
                    // Try all architectures to find one that satisfies all the required defaults.
                    // All defaults must use the same architecture (e.g., x86 or x64). Note that
                    // we don't know what architecture a non-default argument (such as a full path
                    // for --base) is, so it's up to the user to ensure the default architecture
                    // is the same. Typically, this isn't a problem because either (1) the user
                    // specifies full paths for both --base and --diff (and the other paths), or
                    // (2) they specify no full paths, and the defaults logic picks a default, or
                    // (3) they specify no full paths, but do specify --arch to require a specific
                    // architecture.
                    //
                    // Try all build flavors to find one --base and -diff. Both --base and --diff
                    // must be the same build flavor (e.g., checked).
                    //
                    // Then, if necessary, try all build flavors to find both a Core_Root and, if
                    // necessary, --test_root. --test_root and --core_root must be the same build
                    // flavor. If these aren't "release", a warning is given, as it is considered
                    // best practice to use release builds for these.
                    //
                    // If the user already specified one of --core_root, --base, or --diff, we leave
                    // that alone, and only try to fill in the remaining ones with an appropriate
                    // default.
                    //
                    // --core_root, --test_root, and --diff are found within the --diff_root tree.
                    // --base is found within the --base_root tree.
                    //
                    // --core_root and --test_root build flavor defaults to (in order): release, checked, debug.
                    // --base and --diff flavor defaults to (in order): checked, debug.
                    //
                    // --crossgen and --core_root need to be from the same build.
                    //
                    // E.g.:
                    //    test_root: c:\gh\coreclr\bin\tests\Windows_NT.x64.release
                    //    Core_Root: c:\gh\coreclr\bin\tests\Windows_NT.x64.release\Tests\Core_Root
                    //    base/diff: c:\gh\coreclr\bin\Product\Windows_NT.x64.checked

                    List<string> archList;
                    List<string> buildList;

                    if (_arch == null)
                    {
                        archList = new List<string> { "x64", "x86" };
                    }
                    else
                    {
                        archList = new List<string> { _arch };
                    }

                    foreach (var arch in archList)
                    {
                        if (_build == null)
                        {
                            buildList = new List<string> { "checked", "debug" };
                        }
                        else
                        {
                            buildList = new List<string> { _build };
                        }

                        foreach (var build in buildList)
                        {
                            var buildDirName = GetBuildOS() + "." + arch + "." + build;
                            string tryBasePath = null, tryDiffPath = null;

                            if (needBasePath && (_baseRoot != null))
                            {
                                tryBasePath = Path.Combine(_baseRoot, "bin", "Product", buildDirName);
                                if (!Directory.Exists(tryBasePath))
                                {
                                    continue;
                                }
                            }

                            if (needDiffPath && (_diffRoot != null))
                            {
                                tryDiffPath = Path.Combine(_diffRoot, "bin", "Product", buildDirName);
                                if (!Directory.Exists(tryDiffPath))
                                {
                                    continue;
                                }
                            }

                            // If we made it here, we've filled in all the defaults we needed to fill in.
                            // Thus, we found an architecture/build combination that has an appropriate
                            // --diff and --base.

                            if (tryBasePath != null)
                            {
                                _basePath = tryBasePath;
                                needBasePath = false;
                                Console.WriteLine("Using --base={0}", _basePath);
                            }
                            if (tryDiffPath != null)
                            {
                                _diffPath = tryDiffPath;
                                needDiffPath = false;
                                Console.WriteLine("Using --diff={0}", _diffPath);
                            }
                            break;
                        }

                        if (_build == null)
                        {
                            buildList = new List<string> { "release", "checked", "debug" };
                        }
                        else
                        {
                            buildList = new List<string> { _build };
                        }

                        foreach (var build in buildList)
                        {
                            var buildDirName = GetBuildOS() + "." + arch + "." + build;
                            string tryPlatformPath = null, tryCrossgen = null, tryTestPath = null;

                            if (needCoreRoot && (_diffRoot != null))
                            {
                                tryPlatformPath = Path.Combine(_diffRoot, "bin", "tests", buildDirName, "Tests", "Core_Root");
                                if (!Directory.Exists(tryPlatformPath))
                                {
                                    continue;
                                }
                            }

                            if (needCrossgen && (_diffRoot != null))
                            {
                                tryCrossgen = Path.Combine(_diffRoot, "bin", "Product", buildDirName, GetCrossgenExecutableName(_moniker));
                                if (!File.Exists(tryCrossgen))
                                {
                                    continue;
                                }
                            }

                            if (needTestTree && (_diffRoot != null))
                            {
                                tryTestPath = Path.Combine(_diffRoot, "bin", "tests", buildDirName);
                                if (!Directory.Exists(tryTestPath))
                                {
                                    continue;
                                }
                            }

                            // If we made it here, we've filled in all the defaults we needed to fill in.
                            // Thus, we found an architecture/build combination that has an appropriate
                            // Core_Root path (in the "diff" tree) and test_root.

                            if (tryPlatformPath != null)
                            {
                                _platformPath = tryPlatformPath;
                                needCoreRoot = false;
                                Console.WriteLine("Using --core_root={0}", _platformPath);
                            }
                            if (tryCrossgen != null)
                            {
                                _crossgenExe = tryCrossgen;
                                needCrossgen = false;
                                Console.WriteLine("Using --crossgen={0}", _crossgenExe);
                            }
                            if (tryTestPath != null)
                            {
                                _testPath = tryTestPath;
                                needTestTree = false;
                                Console.WriteLine("Using --test_root={0}", _testPath);
                            }

                            if (build != "release")
                            {
                                Console.WriteLine();
                                Console.WriteLine("Warning: it is best practice to use a release build for --core_root, --crossgen, and --test_root.");
                                Console.WriteLine();
                            }

                            break;
                        }

                        if (!needCoreRoot && !needCrossgen && !needBasePath && !needDiffPath && !needTestTree)
                        {
                            break;
                        }
                    }

                    if (needCoreRoot)
                    {
                        Console.Error.WriteLine("Error: didn't find --core_root default");
                    }
                    if (needCrossgen)
                    {
                        Console.Error.WriteLine("Error: didn't find --crossgen default");
                    }
                    if (needTestTree)
                    {
                        Console.Error.WriteLine("Error: didn't find --test_path default");
                    }
                    if (needBasePath)
                    {
                        Console.Error.WriteLine("Error: didn't find --base default");
                    }
                    if (needDiffPath)
                    {
                        Console.Error.WriteLine("Error: didn't find --diff default");
                    }
                }

                if (Verbose)
                {
                    Console.WriteLine("After setting defaults:");
                    Console.WriteLine("--base_root: {0}", _baseRoot);
                    Console.WriteLine("--diff_root: {0}", _diffRoot);
                    Console.WriteLine("--core_root: {0}", _platformPath);
                    Console.WriteLine("--crossgen: {0}", _crossgenExe);
                    Console.WriteLine("--arch: {0}", _arch);
                    Console.WriteLine("--build: {0}", _build);
                    Console.WriteLine("--output: {0}", _outputPath);
                    Console.WriteLine("--base: {0}", _basePath);
                    Console.WriteLine("--diff: {0}", _diffPath);
                    Console.WriteLine("--test_root: {0}", _testPath);
                }
            }

            private string GetRepoRoot()
            {
                // git rev-parse --show-toplevel
                List<string> commandArgs = new List<string> { "rev-parse", "--show-toplevel" };
                CommandResult result = TryCommand("git", commandArgs, true);
                if (result.ExitCode != 0)
                {
                    if (Verbose)
                    {
                        Console.Error.WriteLine("'git rev-parse --show-toplevel' returned non-zero ({0})", result.ExitCode);
                    }
                    return null;
                }

                var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                var git_root = lines[0];
                var repo_root = git_root.Replace('/', Path.DirectorySeparatorChar);

                // Is it actually the dotnet/coreclr repo?
                commandArgs = new List<string> { "remote", "-v" };
                result = TryCommand("git", commandArgs, true);
                if (result.ExitCode != 0)
                {
                    if (Verbose)
                    {
                        Console.Error.WriteLine("'git remote -v' returned non-zero ({0})", result.ExitCode);
                    }
                    return null;
                }

                bool isCoreClr = result.StdOut.Contains("/coreclr");
                if (!isCoreClr)
                {
                    if (Verbose)
                    {
                        Console.Error.WriteLine("Doesn't appear to be the dotnet/coreclr repo:");
                        Console.Error.WriteLine(result.StdOut);
                    }
                    return null;
                }

                if (Verbose)
                {
                    Console.WriteLine("Repo root: " + repo_root);
                }
                return repo_root;
            }

            private void DeriveOutputTag()
            {
                if (_tag == null)
                {
                    int currentCount = 0;
                    foreach (var dir in Directory.EnumerateDirectories(_outputPath))
                    {
                        var name = Path.GetFileName(dir);
                        Regex pattern = new Regex(@"dasmset_([0-9]{1,})");
                        Match match = pattern.Match(name);
                        if (match.Success)
                        {
                            int count = Convert.ToInt32(match.Groups[1].Value);
                            if (count > currentCount)
                            {
                                currentCount = count;
                            }
                        }
                    }

                    currentCount++;
                    _tag = String.Format("dasmset_{0}", currentCount);
                }
            }

            private void ExpandToolTags()
            {
                if (_noJitUtilsRoot)
                {
                    // Early out if there is no JIT_UTILS_ROOT.
                    return;
                }

                var tools = _jObj[s_configFileRootKey]["tools"];

                foreach (var tool in tools)
                {
                    var tag = (string)tool["tag"];
                    var path = (string)tool["path"];

                    if (_basePath == tag)
                    {
                        // passed base tag matches installed tool, reset path.
                        _basePath = path;
                    } 

                    if (_diffPath == tag)
                    {
                        // passed diff tag matches installed tool, reset path.
                        _diffPath = path;
                    }
                }
            }

            private void Validate()
            {
                _validationError = false;

                switch (_command)
                {
                    case Commands.Diff:
                        ValidateDiff();
                        break;
                    case Commands.Install:
                        ValidateInstall();
                        break;
                    case Commands.List:
                        ValidateList();
                        break;
                }

                if (_validationError)
                {
                    DisplayUsageMessage();
                    Environment.Exit(-1);
                }
            }

            private void DisplayUsageMessage()
            {
                Console.Error.WriteLine("");
                Console.Error.Write(_syntaxResult.GetHelpText(100));

                string[] exampleText = {
                    @"Examples:",
                    @"",
                    @"  jit-diff diff --output c:\diffs --corelib --core_root c:\coreclr\bin\tests\Windows_NT.x64.release\Tests\Core_Root --base c:\coreclr_base\bin\Product\Windows_NT.x64.checked --diff c:\coreclr\bin\Product\Windows_NT.x86.checked",
                    @"      Generate diffs of System.Private.CoreLib.dll by specifying baseline and",
                    @"      diff compiler directories explicitly.",
                    @"",
                    @"  jit-diff diff --output c:\diffs --base c:\coreclr_base\bin\Product\Windows_NT.x64.checked --diff",
                    @"      If run within the c:\coreclr git clone of dotnet/coreclr, does the same",
                    @"      as the prevous example, using defaults.",
                    @"",
                    @"  jit-diff diff --output c:\diffs --base --base_root c:\coreclr_base --diff",
                    @"      Does the same as the prevous example, using -base_root to find the base",
                    @"      directory (if run from c:\coreclr tree).",
                    @"",
                    @"  jit-diff diff --base --diff",
                    @"      Does the same as the prevous example (if run from c:\coreclr tree), but uses",
                    @"      default c:\coreclr\bin\diffs output directory, and `base_root` must be specified",
                    @"      in the config.json file in the directory pointed to by the JIT_UTILS_ROOT",
                    @"      environment variable.",
                    @"",
                    @"  jit-diff diff --diff",
                    @"      Only generates asm using the diff JIT -- does not generate asm from a baseline compiler",
                    @"      using all computed defaults.",
                    @"",
                    @"  jit-diff diff --diff --arch x86",
                    @"      Generate diffs, but for x86, even if there is an x64 compiler available.",
                    @"",
                    @"  jit-diff diff --diff --build debug",
                    @"      Generate diffs, but using a debug build, even if there is a checked build available."
                };
                foreach (var line in exampleText)
                {
                    Console.Error.WriteLine(line);
                }
            }

            private void DisplayErrorMessage(string error)
            {
                Console.Error.WriteLine("error: {0}", error);
                _validationError = true;
            }
            
            private void ValidateDiff()
            {
                if (_platformPath == null)
                {
                    DisplayErrorMessage("Specify --core_root <path>");
                }

                if (_corelib && _frameworks)
                {
                    DisplayErrorMessage("Specify only one of --corelib or --frameworks");
                }

                if (_benchmarks && _tests)
                {
                    DisplayErrorMessage("Specify only one of --benchmarks or --tests");
                }

                if (_benchmarks && (_testPath == null))
                {
                    DisplayErrorMessage("--benchmarks requires specifying --test_root as well");
                }

                if (_tests && (_testPath == null))
                {
                    DisplayErrorMessage("--tests requires specifying --test_root as well");
                }

                if (!_corelib && !_frameworks && !_benchmarks && !_tests)
                {
                    // Setting --corelib as the default
                    _corelib = true;
                }

                if (_outputPath == null)
                {
                    DisplayErrorMessage("Specify --output <path>");
                }
                else if (!Directory.Exists(_outputPath))
                {
                    DisplayErrorMessage("Can't find --output path.");
                }

                if ((_basePath == null) && (_diffPath == null))
                {
                    DisplayErrorMessage("Specify either --base or --diff or both.");
                }

                if (_basePath != null && !Directory.Exists(_basePath))
                {
                    DisplayErrorMessage("Can't find --base directory.");
                }

                if (_diffPath != null && !Directory.Exists(_diffPath))
                {
                    DisplayErrorMessage("Can't find --diff directory.");
                }

                if (_crossgenExe != null && !File.Exists(_crossgenExe))
                {
                    DisplayErrorMessage("Can't find --crossgen executable.");
                }
            }

            private void ValidateInstall()
            {
                if (_jobName == null)
                {
                    DisplayErrorMessage("Specify --job <name>");
                }
                
                if (_number == null)
                {
                    DisplayErrorMessage("Specify --number to identify build to install.");
                }

                if (!_asmdiffLoaded)
                {
                    DisplayErrorMessage("\"install\" command requires a valid " + s_configFileName + " file loaded from the directory specified by the JIT_UTILS_ROOT environment variable.");
                }
            }

            private void ValidateList()
            {
                if (!_asmdiffLoaded)
                {
                    DisplayErrorMessage("\"list\" command requires a valid " + s_configFileName + " file loaded from the directory specified by the JIT_UTILS_ROOT environment variable.");
                }
            }

            public string GetToolPath(string tool, out bool found)
            {
                var token = _jObj[s_configFileRootKey]["default"][tool];

                if (token != null)
                {
                    found = true;

                    string tag = token.Value<string>();

                    // Extract set value for tool and see if we can find it
                    // in the installed tools.
                    var path = _jObj[s_configFileRootKey]["tools"].Children()
                                        .Where(x => (string)x["tag"] == tag)
                                        .Select(x => (string)x["path"]);
                    // If the tag resolves to a tool return it, otherwise just return it 
                    // as a posible path.
                    return path.Any() ? path.First() : tag;
                }

                found = false;
                return null;
            }

            public T ExtractDefault<T>(string name, out bool found)
            {
                var token = _jObj[s_configFileRootKey]["default"][name];

                if (token != null)
                {
                    found = true;

                    try
                    {
                        return token.Value<T>();
                    }
                    catch (System.FormatException e)
                    {
                        Console.Error.WriteLine("Bad format for default {0}.  See " + s_configFileName, name, e);
                    }
                }

                found = false;
                return default(T);
            }

            private void LoadFileConfig()
            {
                _jitUtilsRoot = Environment.GetEnvironmentVariable("JIT_UTILS_ROOT");
                if (_jitUtilsRoot == null)
                {
                    _noJitUtilsRoot = true;
                    return;
                }

                string path = Path.Combine(_jitUtilsRoot, s_configFileName);
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("Can't find {0}", path);
                    return;
                }

                string configJson = File.ReadAllText(path);

                try
                {
                    _jObj = JObject.Parse(configJson);
                }
                catch (Newtonsoft.Json.JsonReaderException ex)
                {
                    Console.Error.WriteLine("Error reading config file: {0}", ex.Message);
                    Console.Error.WriteLine("Continuing; ignoring config file.");
                    return;
                }
                
                // Flag that the config file is loaded.
                _asmdiffLoaded = true;

                // Check if there is any default config specified.
                if ((_jObj[s_configFileRootKey] != null) && (_jObj[s_configFileRootKey]["default"] != null))
                {
                    bool found;

                    // Find baseline tool if any.
                    var basePath = GetToolPath("base", out found);
                    if (found)
                    {
                        if (!Directory.Exists(basePath))
                        {
                            Console.WriteLine("Default base path {0} not found! Investigate config file entry.", basePath);
                        }
                        else
                        {
                            _basePath = basePath;
                        }
                    }

                    // Find diff tool if any
                    var diffPath = GetToolPath("diff", out found);
                    if (found)
                    {
                        if (!Directory.Exists(diffPath))
                        {
                            Console.WriteLine("Default diff path {0} not found! Investigate config file entry.", diffPath);
                        }
                        else
                        {
                            _diffPath = diffPath;
                        }
                    }

                    // Find crossgen tool if any
                    var crossgenExe = GetToolPath("crossgen", out found);
                    if (found)
                    {
                        if (!File.Exists(_crossgenExe))
                        {
                            Console.WriteLine("Default crossgen path {0} not found! Investigate config file entry.", crossgenExe);
                        }
                        else
                        {
                            _crossgenExe = crossgenExe;
                        }
                    }

                    // Set up output
                    var outputPath = ExtractDefault<string>("output", out found);
                    _outputPath = (found) ? outputPath : _outputPath;

                    // Setup platform path (core_root).
                    var platformPath = ExtractDefault<string>("core_root", out found);
                    _platformPath = (found) ? platformPath : _platformPath;

                    // Set up test path (test_root).
                    var testPath = ExtractDefault<string>("test_root", out found);
                    _testPath = (found) ? testPath : _testPath;

                    var baseRoot = ExtractDefault<string>("base_root", out found);
                    _baseRoot = (found) ? baseRoot : _baseRoot;

                    var diffRoot = ExtractDefault<string>("diff_root", out found);
                    _diffRoot = (found) ? diffRoot : _diffRoot;

                    var arch = ExtractDefault<string>("arch", out found);
                    _arch = (found) ? arch : _arch;

                    var build = ExtractDefault<string>("build", out found);
                    _build = (found) ? build : _build;

                    // Set flag from default for analyze.
                    var noanalyze = ExtractDefault<bool>("noanalyze", out found);
                    _noanalyze = (found) ? noanalyze : _noanalyze;
                    
                    // Set flag from default for corelib.
                    var corelib = ExtractDefault<bool>("corelib", out found);
                    _corelib = (found) ? corelib : _corelib;
                    
                    // Set flag from default for frameworks.
                    var frameworks = ExtractDefault<bool>("frameworks", out found);
                    _frameworks = (found) ? frameworks : _frameworks;

                    // Set flag from default for benchmarks.
                    var benchmarks = ExtractDefault<bool>("benchmarks", out found);
                    _benchmarks = (found) ? benchmarks : _benchmarks;

                    // Set flag from default for tests.
                    var tests = ExtractDefault<bool>("tests", out found);
                    _tests = (found) ? tests : _tests;

                    // Set flag from default for gcinfo.
                    var gcinfo = ExtractDefault<bool>("gcinfo", out found);
                    _gcinfo = (found) ? gcinfo : _gcinfo;

                    // Set flag from default for tag.
                    var tag = ExtractDefault<string>("tag", out found);
                    _tag = (found) ? tag : _tag;
                    
                    // Set flag from default for verbose.
                    var verbose = ExtractDefault<bool>("verbose", out found);
                    _verbose = (found) ? verbose : _verbose;
                }

                Console.WriteLine("Environment variable JIT_UTILS_ROOT found - configuration loaded.");
            }

            void PrintDefault<T>(string parameter) 
            {
                bool found;
                var defaultValue = ExtractDefault<T>(parameter, out found);
                if (found)
                {
                    Console.WriteLine("\t{0}: {1}", parameter, defaultValue);
                }
            }

            public int List()
            {
                var asmdiffNode = _jObj[s_configFileRootKey];
                if (asmdiffNode == null)
                    return 0;

                // Check if there is any default config specified.
                if (asmdiffNode["default"] != null)
                {
                    Console.WriteLine("Defaults:");

                    PrintDefault<string>("base");
                    PrintDefault<string>("diff");
                    PrintDefault<string>("crossgen");
                    PrintDefault<string>("output");
                    PrintDefault<string>("core_root");
                    PrintDefault<string>("test_root");
                    PrintDefault<string>("base_root");
                    PrintDefault<string>("diff_root");
                    PrintDefault<string>("arch");
                    PrintDefault<string>("build");
                    PrintDefault<string>("analyze");
                    PrintDefault<string>("corelib");
                    PrintDefault<string>("frameworks");
                    PrintDefault<string>("benchmarks");
                    PrintDefault<string>("tests");
                    PrintDefault<string>("tag");
                    PrintDefault<string>("verbose");
                    
                    Console.WriteLine();
                }

                // Print list of the installed tools.

                var tools = asmdiffNode["tools"];
                if (tools != null)
                {
                    Console.WriteLine("Installed tools:");

                    foreach (var tool in tools.Children())
                    {
                        if (_verbose)
                        {
                            Console.WriteLine("\t{0}: {1}", (string)tool["tag"], (string)tool["path"]);
                        }
                        else
                        {
                            Console.WriteLine("\t{0}", (string)tool["tag"]);
                        }
                    }

                    Console.WriteLine();
                }

                return 0;
            }

            public string CoreRoot { get { return _platformPath; } }
            public string TestRoot { get { return _testPath; } }
            public string BasePath { get { return _basePath; } }
            public bool HasBasePath { get { return (_basePath != null); } }
            public string DiffPath { get { return _diffPath; } }
            public bool HasDiffPath { get { return (_diffPath != null); } }
            public string CrossgenExe { get { return _crossgenExe; } }
            public bool HasCrossgenExe { get { return (_crossgenExe != null); } }
            public string OutputPath { get { return _outputPath; } }
            public bool Sequential { get { return _sequential; } }
            public string Tag { get { return _tag; } }
            public bool HasTag { get { return (_tag != null); } }
            public bool CoreLib { get { return _corelib; } }
            public bool DoFrameworks { get { return _frameworks; } }
            public bool Benchmarks { get { return _benchmarks; } }
            public bool DoTestTree { get { return _tests; } }
            public bool GenerateGCInfo { get { return _gcinfo; } }
            public bool Verbose { get { return _verbose; } }
            public bool DoAnalyze { get { return !_noanalyze; } }
            public Commands DoCommand { get { return _command; } }
            public string JobName { get { return _jobName; } }
            public string JitUtilsRoot { get { return _jitUtilsRoot; } }
            public bool HasJitUtilsRoot { get { return (_jitUtilsRoot != null); } }
            public string RID { get { return _rid; } }
            public string Moniker { get { return _moniker; } }
            public string Number { get { return _number; } }
            public string BranchName { get { return _branchName; } }
        }

        private static string[] s_testDirectories =
        {
            "Interop",
            "JIT"
        };

        // This represents the path components (without platform-specific separator characters)
        // from the root of the test tree to the benchmark directory.
        private static string[] s_benchmarksPath = { "JIT", "Performance", "CodeQuality" };

        private static string[] s_benchmarkDirectories =
        {
            "BenchI",
            "BenchF",
            "BenchmarksGame"
        };

        private static string[] s_CoreLibAssembly =
        {
            "System.Private.CoreLib.dll"
        };

        // List of framework assemblies we will run diffs on.
        // Note: System.Private.CoreLib.dll should be first in this list.
        private static string[] s_frameworkAssemblies =
        {
            "System.Private.CoreLib.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.VisualBasic.dll",
            "Microsoft.CSharp.dll",
            "Microsoft.VisualBasic.dll",
            "Microsoft.Win32.Primitives.dll",
            "Microsoft.Win32.Registry.dll",
            "System.Buffers.dll",
            "System.Collections.Concurrent.dll",
            "System.Collections.Immutable.dll",
            "System.Collections.dll",
            "System.ComponentModel.Annotations.dll",
            "System.ComponentModel.dll",
            "System.Console.dll",
            "System.Diagnostics.Debug.dll",
            "System.Diagnostics.DiagnosticSource.dll",
            "System.Diagnostics.FileVersionInfo.dll",
            "System.Diagnostics.Process.dll",
            "System.Diagnostics.StackTrace.dll",
            "System.Diagnostics.Tools.dll",
            "System.Diagnostics.Tracing.dll",
            "System.Dynamic.Runtime.dll",
            "System.Globalization.Extensions.dll",
            "System.IO.Compression.dll",
            "System.IO.Compression.ZipFile.dll",
            "System.IO.FileSystem.dll",
            "System.IO.FileSystem.Watcher.dll",
            "System.IO.MemoryMappedFiles.dll",
            "System.IO.dll",
            "System.IO.UnmanagedMemoryStream.dll",
            "System.Linq.Expressions.dll",
            "System.Linq.dll",
            "System.Linq.Parallel.dll",
            "System.Linq.Queryable.dll",
            "System.Net.Http.dll",
            "System.Net.NameResolution.dll",
            "System.Net.Primitives.dll",
            "System.Net.Requests.dll",
            "System.Net.Security.dll",
            "System.Net.Sockets.dll",
            "System.Net.WebHeaderCollection.dll",
            "System.Numerics.Vectors.dll",
            "System.ObjectModel.dll",
            "System.Private.Uri.dll",
            "System.Reflection.DispatchProxy.dll",
            "System.Reflection.Extensions.dll",
            "System.Reflection.Metadata.dll",
            "System.Reflection.TypeExtensions.dll",
            "System.Resources.Reader.dll",
            "System.Runtime.Extensions.dll",
            "System.Runtime.Handles.dll",
            "System.Runtime.InteropServices.dll",
            "System.Runtime.InteropServices.RuntimeInformation.dll",
            "System.Runtime.dll",
            "System.Runtime.Numerics.dll",
            "System.Security.Claims.dll",
            "System.Security.Cryptography.Algorithms.dll",
            "System.Security.Cryptography.Cng.dll",
            "System.Security.Cryptography.Csp.dll",
            "System.Security.Cryptography.Encoding.dll",
            "System.Security.Cryptography.OpenSsl.dll",
            "System.Security.Cryptography.Primitives.dll",
            "System.Security.Cryptography.X509Certificates.dll",
            "System.Security.Principal.Windows.dll",
            "System.Text.Encoding.CodePages.dll",
            "System.Text.Encoding.Extensions.dll",
            "System.Text.RegularExpressions.dll",
            "System.Threading.dll",
            "System.Threading.Overlapped.dll",
            "System.Threading.Tasks.Dataflow.dll",
            "System.Threading.Tasks.Extensions.dll",
            "System.Threading.Tasks.dll",
            "System.Threading.Tasks.Parallel.dll",
            "System.Xml.ReaderWriter.dll",
            "System.Xml.XDocument.dll",
            "System.Xml.XmlDocument.dll",
            "System.Xml.XPath.dll",
            "System.Xml.XPath.XDocument.dll"
        };

        public static int Main(string[] args)
        {
            Config config = new Config(args);
            int ret = 0;
            
            switch (config.DoCommand)
            {
                case Commands.Diff:
                    {
                        ret = DiffTool.DiffCommand(config);
                    }
                    break;
                case Commands.List:
                    {
                        // List command: list loaded configuration
                        ret = config.List();
                    }
                    break;
                case Commands.Install:
                    {
                        ret = InstallCommand(config);
                    }
                    break;
            }
            
            return ret;
        }

        class ScriptResolverPolicyWrapper : ICommandResolverPolicy
        {
            public CompositeCommandResolver CreateCommandResolver() => ScriptCommandResolverPolicy.Create();
        }

        public static CommandResult TryCommand(string name, IEnumerable<string> commandArgs, bool capture = false)
        {
            try 
            {
                Command command =  Command.Create(new ScriptResolverPolicyWrapper(), name, commandArgs);

                if (capture)
                {
                    // Capture stdout/stderr for consumption within tool.
                    command.CaptureStdOut();
                    command.CaptureStdErr();
                }
                else
                {
                    // Wireup stdout/stderr so we can see output.
                    command.ForwardStdOut();
                    command.ForwardStdErr();
                }

                return command.Execute();
            }
            catch (CommandUnknownException e)
            {
                Console.Error.WriteLine("\nError: {0} command not found!  Add {0} to the path.", name, e);
                Environment.Exit(-1);
                return CommandResult.Empty;
            }
        }

        public static int InstallCommand(Config config)
        {
            var configFilePath = Path.Combine(config.JitUtilsRoot, s_configFileName);
            string configJson = File.ReadAllText(configFilePath);
            string tag = String.Format("{0}-{1}", config.JobName, config.Number);
            string toolPath = Path.Combine(config.JitUtilsRoot, "tools", tag);
            var jObj = JObject.Parse(configJson);

            if ((jObj[s_configFileRootKey] == null) || (jObj[s_configFileRootKey]["tools"] == null))
            {
                Console.Error.WriteLine("\"install\" doesn't know how to add the \"" + s_configFileRootKey + "\":\"tool\" section to the config file");
                return -1;
            }

            var tools = (JArray)jObj[s_configFileRootKey]["tools"];

            // Early out if the tool is already installed.
            if (tools.Where(x => (string)x["tag"] == tag).Any())
            {
                Console.Error.WriteLine("{0} is already installed in the " + s_configFileName + ". Remove before re-install.", tag);
                return -1;
            }

            // Issue cijobs command to download bits            
            List<string> cijobsArgs = new List<string>();

            cijobsArgs.Add("copy");

            cijobsArgs.Add("--job");
            cijobsArgs.Add(config.JobName);
            
            if (config.BranchName != null)
            {
                cijobsArgs.Add("--branch");
                cijobsArgs.Add(config.BranchName);
            }
            
            cijobsArgs.Add("--number");
            cijobsArgs.Add(config.Number);
            
            cijobsArgs.Add("--unzip");
            
            cijobsArgs.Add("--output");
            cijobsArgs.Add(toolPath);

            if (config.Verbose)
            {
                Console.WriteLine("Command: {0} {1}", "cijobs", String.Join(" ", cijobsArgs));
            }
            
            CommandResult result = TryCommand("cijobs", cijobsArgs);

            if (result.ExitCode != 0)
            {
                Console.Error.WriteLine("cijobs command returned with {0} failures", result.ExitCode);
                return result.ExitCode;
            }

            JObject newTool = new JObject();
            newTool.Add("tag", tag);
            string platformPath = Path.Combine(toolPath, "Product");
            if (!Directory.Exists(platformPath))
            {
                Console.Error.WriteLine("cijobs didn't create or populate directory {0}", platformPath);
                return 1;
            }

            foreach (var dir in Directory.EnumerateDirectories(platformPath))
            {
                if (Path.GetFileName(dir).ToUpper().Contains(config.Moniker.ToUpper()))
                {
                    newTool.Add("path", Path.GetFullPath(dir));
                    tools.Last.AddAfterSelf(newTool);
                    break;
                }
            }

            // Overwrite current config.json with new data.
            using (var file = File.CreateText(configFilePath))
            {
                using (JsonTextWriter writer = new JsonTextWriter(file))
                {
                    writer.Formatting = Formatting.Indented;
                    jObj.WriteTo(writer);
                }
            }

            return 0;
        }

        public class DiffTool
        {
            private class DasmWorkItem
            {
                public List<string> DasmArgs { get; set; }

                public DasmWorkItem(List<string> dasmArgs)
                {
                    DasmArgs = dasmArgs;
                }
            }

            private Config m_config;
            private List<Task<int>> DasmWorkTasks = new List<Task<int>>();

            public DiffTool(Config config)
            {
                m_config = config;
            }

            // Returns a count of the number of failures.
            private int RunDasmTool(DasmWorkItem item)
            {
                int dasmFailures = 0;

                string command = s_asmTool + " " + String.Join(" ", item.DasmArgs);
                Console.WriteLine("Dasm command: {0}", command);
                CommandResult result = TryCommand(s_asmTool, item.DasmArgs);
                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("Dasm command \"{0}\" returned with {1} failures", command, result.ExitCode);
                    dasmFailures += result.ExitCode;
                }

                return dasmFailures;
            }

            private void StartDasmWork(List<string> args)
            {
                Task<int> task = Task<int>.Factory.StartNew(() => RunDasmTool(new DasmWorkItem(args)));
                DasmWorkTasks.Add(task);

                if (m_config.Verbose)
                {
                    string command = String.Join(" ", args);
                    Console.WriteLine("Started dasm command \"{0}\"", command);
                }

                if (m_config.Sequential)
                {
                    Task.WaitAll(task);
                }
            }

            private void StartDasmWorkOne(List<string> commandArgs, string tagBaseDiff, string clrPath, AssemblyInfo assemblyInfo)
            {
                List<string> args = ConstructArgs(commandArgs, clrPath);

                string outputPath = Path.Combine(m_config.OutputPath, tagBaseDiff, assemblyInfo.OutputPath);
                args.Add("--output");
                args.Add(outputPath);

                args.Add(assemblyInfo.Path);

                StartDasmWork(args);
            }

            private void StartDasmWorkBaseDiff(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                foreach (var assemblyInfo in assemblyWorkList)
                {
                    if (m_config.HasBasePath)
                    {
                        StartDasmWorkOne(commandArgs, "base", m_config.BasePath, assemblyInfo);
                    }
                    if (m_config.HasDiffPath)
                    {
                        StartDasmWorkOne(commandArgs, "diff", m_config.DiffPath, assemblyInfo);
                    }
                }
            }

            private List<string> ConstructArgs(List<string> commandArgs, string clrPath)
            {
                List<string> dasmArgs = commandArgs.ToList();
                dasmArgs.Add("--crossgen");
                if (m_config.HasCrossgenExe)
                {
                    dasmArgs.Add(m_config.CrossgenExe);

                    var jitPath = Path.Combine(clrPath, GetJitLibraryName(m_config.Moniker));
                    if (!File.Exists(jitPath))
                    {
                        Console.Error.WriteLine("clrjit not found at " + jitPath);
                        return null;
                    }

                    dasmArgs.Add("--jit");
                    dasmArgs.Add(jitPath);
                }
                else
                {
                    var crossgenPath = Path.Combine(clrPath, GetCrossgenExecutableName(m_config.Moniker));
                    if (!Directory.Exists(crossgenPath))
                    {
                        Console.Error.WriteLine("crossgen not found at " + crossgenPath);
                        return null;
                    }

                    dasmArgs.Add(crossgenPath);
                }

                return dasmArgs;
            }

            // Returns:
            // 0 on success,
            // Otherwise, a count of the number of failures generating asm, as reported by the asm tool.
            private int RunDasmTool(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                StartDasmWorkBaseDiff(commandArgs, assemblyWorkList);

                int dasmFailures = 0;

                try
                {
                    Task<int>[] taskArray = DasmWorkTasks.ToArray();
                    Task.WaitAll(taskArray);

                    foreach (Task<int> t in taskArray)
                    {
                        dasmFailures += t.Result;
                    }
                }
                catch (AggregateException ex)
                {
                    Console.Error.WriteLine("Dasm task failed with {0}", ex.Message);
                    dasmFailures += ex.InnerExceptions.Count;
                }

                if (dasmFailures != 0)
                {
                    Console.Error.WriteLine("Dasm commands returned with total of {0} failures", dasmFailures);
                }

                return dasmFailures;
            }

            // Returns 0 on success, 1 on failure.
            public static int DiffCommand(Config config)
            {
                string diffString = "";

                if (config.CoreLib)
                {
                    diffString += "System.Private.CoreLib.dll";
                }
                else if (config.DoFrameworks)
                {
                    diffString += "System.Private.CoreLib.dll, framework assemblies";
                }

                if (config.Benchmarks)
                {
                    if (!String.IsNullOrEmpty(diffString)) diffString += ", ";
                    diffString += "benchstones and benchmarks game in " + config.TestRoot;
                }
                else if (config.DoTestTree)
                {
                    if (!String.IsNullOrEmpty(diffString)) diffString += ", ";
                    diffString += "assemblies in " + config.TestRoot;
                }

                Console.WriteLine("Beginning diff of {0}", diffString);

                // Create subjob that runs jit-dasm, which should be in path, with the 
                // relevent coreclr assemblies/paths.

                List<string> commandArgs = new List<string>();

                commandArgs.Add("--platform");
                commandArgs.Add(config.CoreRoot);

                if (config.GenerateGCInfo)
                {
                    commandArgs.Add("--gcinfo");
                }

                if (config.Verbose)
                {
                    commandArgs.Add("--verbose");
                }

                List<AssemblyInfo> assemblyWorkList = GenerateAssemblyWorklist(config);

                DiffTool diffTool = new DiffTool(config);
                int dasmFailures = diffTool.RunDasmTool(commandArgs, assemblyWorkList);

                // Analyze completed run.

                if (config.DoAnalyze && config.HasDiffPath && config.HasBasePath)
                {
                    List<string> analysisArgs = new List<string>();

                    analysisArgs.Add("--base");
                    analysisArgs.Add(Path.Combine(config.OutputPath, "base"));
                    analysisArgs.Add("--diff");
                    analysisArgs.Add(Path.Combine(config.OutputPath, "diff"));
                    analysisArgs.Add("--recursive");

                    Console.WriteLine("Analyze command: {0} {1}",
                        s_analysisTool, String.Join(" ", analysisArgs));

                    CommandResult analyzeResult = TryCommand(s_analysisTool, analysisArgs);
                }

                // Report any failures to generate asm at the very end (again). This is so
                // this information doesn't get buried in previous output.

                if (dasmFailures != 0)
                {
                    Console.Error.WriteLine("");
                    Console.Error.WriteLine("Warning: {0} failures detected generating asm", dasmFailures);

                    return 1; // failure result
                }
                else
                {
                    return 0; // success result
                }
            }

            public static List<AssemblyInfo> GenerateAssemblyWorklist(Config config)
            {
                List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();

                // CoreLib and the frameworks add specific files, not directories.
                // These files will all be put in the same output directory. This
                // works because they all have unique names, and live in the same
                // source directory already.
                if (config.CoreLib || config.DoFrameworks)
                {
                    foreach (var assembly in config.CoreLib ? s_CoreLibAssembly : s_frameworkAssemblies)
                    {
                        string fullPathAssembly = Path.Combine(config.CoreRoot, assembly);

                        if (!File.Exists(fullPathAssembly))
                        {
                            Console.Error.WriteLine("Warning: can't find framework assembly {0}", fullPathAssembly);
                            continue;
                        }

                        AssemblyInfo info = new AssemblyInfo
                        {
                            Path = fullPathAssembly,
                            OutputPath = ""
                        };

                        assemblyInfoList.Add(info);
                    }
                }

                // The tests are in a tree hierarchy of directories. We will output these to a tree
                // structure matching their source tree structure, to avoid name conflicts.
                if (config.Benchmarks || config.DoTestTree)
                {
                    string basepath = config.Benchmarks ? Utility.CombinePath(config.TestRoot, s_benchmarksPath) : config.TestRoot;
                    foreach (var dir in config.Benchmarks ? s_benchmarkDirectories : s_testDirectories)
                    {
                        string fullPathDir = Path.Combine(basepath, dir);

                        if (!Directory.Exists(fullPathDir))
                        {
                            Console.Error.WriteLine("Warning: can't find test directory {0}", fullPathDir);
                            continue;
                        }

                        // For the directory case create a stack and recursively find any
                        // assemblies for compilation.
                        List<AssemblyInfo> directoryAssemblyInfoList = IdentifyAssemblies(basepath, fullPathDir, config);

                        // Add info generated at this directory
                        assemblyInfoList.AddRange(directoryAssemblyInfoList);
                    }
                }

                return assemblyInfoList;
            }

            // Check to see if the passed filePath is to an assembly.
            private static bool IsAssembly(string filePath)
            {
                try
                {
                    System.Reflection.AssemblyName diffAssembly =
                        System.Runtime.Loader.AssemblyLoadContext.GetAssemblyName(filePath);
                }
                catch (System.IO.FileNotFoundException)
                {
                    // File not found - not an assembly
                    // TODO - should we log this case?
                    return false;
                }
                catch (System.BadImageFormatException)
                {
                    // Explictly not an assembly.
                    return false;
                }
                catch (System.IO.FileLoadException)
                {
                    // This is an assembly but it just happens to be loaded.
                    // (leave true in so as not to rely on fallthrough)
                    return true;
                }

                return true;
            }

            // Recursively search for assemblies from a path given by `rootPath`.
            // `basePath` specifies the root directory for the purpose of constructing a relative output path.
            //      It must be a prefix of `rootPath`.
            private static List<AssemblyInfo> IdentifyAssemblies(string basePath, string rootPath, Config config)
            {
                List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();
                string fullBasePath = Path.GetFullPath(basePath);
                string fullRootPath = Path.GetFullPath(rootPath);

                // Get files that could be assemblies, but discard currently ngen'd assemblies.
                var subFiles = Directory.EnumerateFiles(fullRootPath, "*", SearchOption.AllDirectories)
                    .Where(s => (s.EndsWith(".exe") || s.EndsWith(".dll")) && !s.Contains(".ni."));

                foreach (var filePath in subFiles)
                {
                    if (config.Verbose)
                    {
                        Console.WriteLine("Scanning: {0}", filePath);
                    }

                    // skip if not an assembly
                    if (!IsAssembly(filePath))
                    {
                        continue;
                    }

                    string directoryName = Path.GetDirectoryName(filePath);
                    string fullDirectoryName = Path.GetFullPath(directoryName);
                    string outputPath = fullDirectoryName.Substring(fullBasePath.Length).TrimStart(Path.DirectorySeparatorChar);

                    AssemblyInfo info = new AssemblyInfo
                    {
                        Path = filePath,
                        OutputPath = outputPath
                    };

                    assemblyInfoList.Add(info);
                }

                return assemblyInfoList;
            }
        }
    }
}

