// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace ManagedCodeGen
{
    public class AssemblyInfo
    {
        // Contains full path to assembly.
        public string Path { get; set; }

        // Contains relative path within output directory for given assembly.
        // This allows for different output directories per tool.
        public string OutputPath { get; set; }
    }

    public partial class @jitdiff
    {
        // Supported commands.  List to view information from the CI system, and Copy to download artifacts.
        public enum Commands
        {
            Install,
            Uninstall,
            Diff,
            PmiDiff,
            List
        }

        private static string s_asmToolPrejit = "jit-dasm";
        private static string s_asmToolJit = "jit-dasm-pmi";
        private static string s_analysisTool = "jit-analyze";
        private static string s_configFileName = "config.json";
        private static string s_configFileRootKey = "asmdiff";
        private static string[] s_defaultDiffDirectoryPath = { "artifacts", "diffs" };

        private static string GetJitLibraryName(string platformMoniker)
        {
            switch (platformMoniker)
            {
                case "Windows":
                    return "clrjit.dll";
                case "Linux":
                    return "libclrjit.so";
                case "OSX":
                    return "libclrjit.dylib";
                default:
                    Console.Error.WriteLine("No platform mapping! (Platform moniker = {0})", platformMoniker);
                    return null;
            }
        }

        private static string GetCrossgenExecutableName(string platformMoniker)
        {
            switch (platformMoniker)
            {
                case "Windows":
                    return "crossgen.exe";
                case "Linux":
                case "OSX":
                    return "crossgen";
                default:
                    Console.Error.WriteLine("No platform mapping! (Platform moniker = {0})", platformMoniker);
                    return null;
            }
        }

        private static string GetCorerunExecutableName(string platformMoniker)
        {
            switch (platformMoniker)
            {
                case "Windows":
                    return "corerun.exe";
                case "Linux":
                case "OSX":
                    return "corerun";
                default:
                    Console.Error.WriteLine("No platform mapping! (Platform moniker = {0})", platformMoniker);
                    return null;
            }
        }

        private static string GetOSArtifactDirComponent(string platformMoniker)
        {
            switch (platformMoniker)
            {
                case "Windows":
                    return "windows";
                case "Linux":
                    return "Linux";
                case "OSX":
                    return "OSX";
                default:
                    Console.Error.WriteLine("No platform mapping! (Platform moniker = {0})", platformMoniker);
                    return null;
            }
        }

        private static string GetConfigurationArtifactDir(string platformMoniker, string arch, string buildType)
        {
            return GetOSArtifactDirComponent(platformMoniker) + "." + arch + "." + buildType;
        }

        public class Config
        {
            private Commands _command = Commands.Diff;
            private bool _baseSpecified = false;    // True if user specified "--base" or "--base <path>" or "--base <tag>"
            private bool _diffSpecified = false;    // True if user specified "--diff" or "--diff <path>" or "--diff <tag>"
            private string _basePath = null;        // Non-null if user specified "--base <path>" or "--base <tag>"
            private string _diffPath = null;        // Non-null if user specified "--diff <path>" or "--diff <tag>"
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
            private string _altjit = null;
            private bool _corelib = false;
            private bool _frameworks = false;
            private bool _benchmarks = false;
            private bool _tests = false;
            private bool _gcinfo = false;
            private bool _debuginfo = false;
            private bool _verbose = false;
            private bool _noDiffable = false;
            private bool _tier0 = false;
            private string _jobName = null;
            private string _number = null;
            private bool _lastSuccessful = false;
            private string _jitUtilsRoot = null;
            private string _rid = null;
            private string _platformName = null;
            private string _branchName = null;
            private bool _pmi = false;
            private IReadOnlyList<string> _assemblyList = [];
            private bool _tsv;
            private bool _cctors;
            private int  _count = 20;
            private string _metric = "CodeSize";
            private JsonObject _jObj;
            private bool _configFileLoaded = false;
            private bool _noJitUtilsRoot = false;
            private bool _validationError = false;

            internal Config(JitDiffRootCommand command)
            {
                // Get configuration values from JIT_UTILS_ROOT/config.json.
                LoadFileConfig();

                ParseResult result = command.Result;
                _command = command.SelectedCommand;

                bool IsSpecified<T>(Option<T> option) => result.GetResult(option) != null;
                T Get<T>(Option<T> option) => result.GetValue(option);

                if (_command == Commands.Diff)
                {
                    _baseSpecified = IsSpecified(command.BasePath);
                    if (_baseSpecified)
                    {
                        _basePath = Get(command.BasePath);
                    }

                    _diffSpecified = IsSpecified(command.DiffPath);
                    if (_diffSpecified)
                    {
                        _diffPath = Get(command.DiffPath);
                    }

                    if (IsSpecified(command.CrossgenExe)) _crossgenExe = Get(command.CrossgenExe);
                    if (IsSpecified(command.OutputPath)) _outputPath = Get(command.OutputPath);
                    if (IsSpecified(command.NoAnalyze)) _noanalyze = Get(command.NoAnalyze);
                    if (IsSpecified(command.Sequential)) _sequential = Get(command.Sequential);
                    if (IsSpecified(command.Tag)) _tag = Get(command.Tag);
                    if (IsSpecified(command.CoreLib)) _corelib = Get(command.CoreLib);
                    if (IsSpecified(command.Frameworks)) _frameworks = Get(command.Frameworks);
                    if (IsSpecified(command.Metric)) _metric = Get(command.Metric);
                    if (IsSpecified(command.Benchmarks)) _benchmarks = Get(command.Benchmarks);
                    if (IsSpecified(command.Tests)) _tests = Get(command.Tests);
                    if (IsSpecified(command.GCInfo)) _gcinfo = Get(command.GCInfo);
                    if (IsSpecified(command.DebugInfo)) _debuginfo = Get(command.DebugInfo);
                    if (IsSpecified(command.Verbose)) _verbose = Get(command.Verbose);
                    if (IsSpecified(command.NoDiffable)) _noDiffable = Get(command.NoDiffable);
                    if (IsSpecified(command.CoreRoot)) _platformPath = Get(command.CoreRoot);
                    if (IsSpecified(command.TestRoot)) _testPath = Get(command.TestRoot);
                    if (IsSpecified(command.BaseRoot)) _baseRoot = Get(command.BaseRoot);
                    if (IsSpecified(command.DiffRoot)) _diffRoot = Get(command.DiffRoot);
                    if (IsSpecified(command.Arch)) _arch = Get(command.Arch);
                    if (IsSpecified(command.Build)) _build = Get(command.Build);
                    if (IsSpecified(command.AltJit)) _altjit = Get(command.AltJit);
                    if (IsSpecified(command.Pmi)) _pmi = Get(command.Pmi);
                    if (IsSpecified(command.Cctors)) _cctors = Get(command.Cctors);
                    if (IsSpecified(command.AssemblyList)) _assemblyList = Get(command.AssemblyList);
                    if (IsSpecified(command.Tsv)) _tsv = Get(command.Tsv);
                    if (IsSpecified(command.Tier0)) _tier0 = Get(command.Tier0);
                    if (IsSpecified(command.Count)) _count = Get(command.Count);

                    if (_pmi)
                    {
                        _command = Commands.PmiDiff;
                    }
                }
                else if (_command == Commands.List)
                {
                    if (IsSpecified(command.Verbose)) _verbose = Get(command.Verbose);
                }
                else if (_command == Commands.Install)
                {
                    if (IsSpecified(command.JobName)) _jobName = Get(command.JobName);
                    if (IsSpecified(command.Number)) _number = Get(command.Number);
                    if (IsSpecified(command.LastSuccessful)) _lastSuccessful = Get(command.LastSuccessful);
                    if (IsSpecified(command.BranchName)) _branchName = Get(command.BranchName);
                    if (IsSpecified(command.Verbose)) _verbose = Get(command.Verbose);
                }
                else if (_command == Commands.Uninstall)
                {
                    if (IsSpecified(command.Tag)) _tag = Get(command.Tag);
                }

                SetRID();

                ExpandToolTags();

                SetDefaults();

                Validate();

                if (_command == Commands.Diff || _command == Commands.PmiDiff)
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
                ProcessResult result = Utility.ExecuteProcess("dotnet", commandArgs, true);

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("dotnet --info returned non-zero");
                    Environment.Exit(-1);
                }

                var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                Regex ridPattern = new Regex(@"RID:\s*([A-Za-z0-9\.-]*)$");
                Regex platPattern = new Regex(@"Platform:\s*([A-Za-z0-9]*)$");

                bool isRidFound = false, isPlatFound = false;
                foreach (var line in lines)
                {
                    Match ridMatch = ridPattern.Match(line);
                    if (ridMatch.Success)
                    {
                        _rid = ridMatch.Groups[1].Value;
                        isRidFound = true;
                        if (isPlatFound)
                        {
                            break;
                        }
                        continue;
                    }

                    Match platMatch = platPattern.Match(line);
                    if (platMatch.Success)
                    {
                        _platformName = platMatch.Groups[1].Value;
                        isPlatFound = true;
                        if (isRidFound)
                        {
                            break;
                        }
                        continue;
                    }
                }

                if (_rid == null)
                {
                    Console.WriteLine("Couldn't find RID in 'dotnet --info' output:");
                    Console.WriteLine("stdout:");
                    Console.WriteLine("{0}", result.StdOut);
                    Console.WriteLine("stderr:");
                    Console.WriteLine("{0}", result.StdErr);
                }

                if (_platformName == null)
                {
                    Console.WriteLine("Couldn't find Platform in 'dotnet --info' output:");
                    Console.WriteLine("stdout:");
                    Console.WriteLine("{0}", result.StdOut);
                    Console.WriteLine("stderr:");
                    Console.WriteLine("{0}", result.StdErr);

                    if (OperatingSystem.IsWindows())
                    {
                        _platformName = "Windows";
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        _platformName = "Linux";
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        _platformName = "Darwin";
                    }

                    if (_platformName != null)
                    {
                        Console.WriteLine($"Falling back to {_platformName} based on OS detection.");
                    }
                }
            }

            public string PlatformMoniker
            {
                get
                {
                    switch (_platformName)
                    {
                        case "Windows":
                            return "Windows";
                        case "Linux":
                            return "Linux";
                        case "Darwin":
                            return "OSX";
                        default:
                            Console.Error.WriteLine("No platform mapping! (Platform name = {0})", _platformName);
                            return null;
                    }
                }
            }

            private void SetDefaults()
            {
                // Figure out what we need to set from defaults.

                switch (_command)
                {
                    case Commands.Diff:
                    case Commands.PmiDiff:
                        break;
                    case Commands.Install:
                    case Commands.Uninstall:
                    case Commands.List:
                        // Don't need any defaults.
                        return;
                }

                bool needOutputPath = (_outputPath == null);                            // We need to find --output
                bool needCoreRoot = (_platformPath == null);                            // We need to find --core_root

                // It's not clear we should find a default for crossgen: in the current code, if crossgen is specified,
                // then we always use that. If not specified, we find crossgen in core_root. That seems appropriate to
                // continue, without this default.
                // bool needCrossgen = (_crossgenExe == null);                             // We need to find --crossgen
                bool needCrossgen = false;

                bool needBasePath = _baseSpecified && (_basePath == null);              // We need to find --base
                bool needDiffPath = _diffSpecified && (_diffPath == null);              // We need to find --diff
                bool needTestTree = (Benchmarks || DoTestTree) && (_testPath == null);  // We need to find --test_root

                bool needDiffRoot = (_diffRoot == null) &&
                    (needOutputPath || needCoreRoot || needCrossgen || needDiffPath || needTestTree);

                // If --diff_root wasn't specified, see if we can figure it out from the current directory
                // using git.

                if (needDiffRoot)
                {
                    _diffRoot = Utility.GetRepoRoot(Verbose);
                }

                if (needOutputPath && (_diffRoot != null))
                {
                    _outputPath = Utility.CombinePath(_diffRoot, s_defaultDiffDirectoryPath);
                    Utility.EnsureDirectoryExists(_outputPath);

                    Console.WriteLine("Using --output {0}", _outputPath);
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
                    // must be the same build flavor (e.g., Checked).
                    //
                    // Then, if necessary, try all build flavors to find both a Core_Root and, if
                    // necessary, --test_root. --test_root and --core_root must be the same build
                    // flavor. If these aren't "Release", a warning is given, as it is considered
                    // best practice to use Release builds for these.
                    //
                    // If the user already specified one of --core_root, --base, or --diff, we leave
                    // that alone, and only try to fill in the remaining ones with an appropriate
                    // default.
                    //
                    // --core_root, --test_root, and --diff are found within the --diff_root tree.
                    // --base is found within the --base_root tree.
                    //
                    // --core_root and --test_root build flavor defaults to (in order): Release, Checked, Debug.
                    // --base and --diff flavor defaults to (in order): Checked, Debug.
                    //
                    // --crossgen and --core_root need to be from the same build.
                    //
                    // E.g.:
                    //    test_root: c:\gh\runtime\artifacts\tests\coreclr\windows.x64.Release
                    //    Core_Root: c:\gh\runtime\artifacts\tests\coreclr\windows.x64.Release\Tests\Core_Root
                    //    base/diff: c:\gh\runtime\artifacts\bin\coreclr\windows.x64.Checked

                    List<string> archList;
                    List<string> buildList;

                    if (_arch == null)
                    {
                        Architecture arch = RuntimeInformation.ProcessArchitecture;
                        switch (arch)
                        {
                            case Architecture.X64:
                                archList = new List<string> { "x64", "x86" };
                                break;
                            case Architecture.X86:
                                archList = new List<string> { "x86", "x64" };
                                break;
                            case Architecture.Arm:
                                archList = new List<string> { "arm" };
                                break;
                            case Architecture.Arm64:
                                archList = new List<string> { "arm64" };
                                break;
                            default:
                                // Unknown. Assume x64.
                                archList = new List<string> { "x64", "x86" };
                                break;
                        }
                    }
                    else
                    {
                        archList = new List<string> { _arch };
                    }

                    foreach (var arch in archList)
                    {
                        if (_build == null)
                        {
                            buildList = new List<string> { "Checked", "Debug" };
                        }
                        else
                        {
                            buildList = new List<string> { _build };
                        }

                        foreach (var build in buildList)
                        {
                            var buildDirName = GetConfigurationArtifactDir(PlatformMoniker, arch, build);
                            string tryBasePath = null, tryDiffPath = null;

                            if (needBasePath && (_baseRoot != null))
                            {
                                tryBasePath = Path.Combine(_baseRoot, "artifacts", "bin", "coreclr", buildDirName);
                                if (!Directory.Exists(tryBasePath))
                                {
                                    continue;
                                }
                            }

                            if (needDiffPath && (_diffRoot != null))
                            {
                                tryDiffPath = Path.Combine(_diffRoot, "artifacts", "bin", "coreclr", buildDirName);
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
                                Console.WriteLine($"Using --base {_basePath}");
                            }

                            if (tryDiffPath != null)
                            {
                                _diffPath = tryDiffPath;
                                needDiffPath = false;
                                Console.WriteLine($"Using --diff {_diffPath}");
                            }

                            if (_arch == null)
                            {
                                _arch = arch;
                                Console.WriteLine($"Using --arch {_arch}");
                            }
                            break;
                        }

                        if (_build == null)
                        {
                            buildList = new List<string> { "Release", "Checked", "Debug" };
                        }
                        else
                        {
                            buildList = new List<string> { _build };
                        }

                        foreach (var build in buildList)
                        {
                            var buildDirName = GetConfigurationArtifactDir(PlatformMoniker, arch, build);
                            string tryPlatformPath = null, tryCrossgen = null, tryTestPath = null;

                            if (needCoreRoot && (_diffRoot != null))
                            {
                                tryPlatformPath = Path.Combine(_diffRoot, "artifacts", "tests", "coreclr", buildDirName, "Tests", "Core_Root");
                                if (!Directory.Exists(tryPlatformPath))
                                {
                                    continue;
                                }
                            }

                            if (needCrossgen && (_diffRoot != null))
                            {
                                tryCrossgen = Path.Combine(_diffRoot, "artifacts", "bin", "coreclr", buildDirName, GetCrossgenExecutableName(PlatformMoniker));
                                if (!File.Exists(tryCrossgen))
                                {
                                    continue;
                                }
                            }

                            if (needTestTree && (_diffRoot != null))
                            {
                                tryTestPath = Path.Combine(_diffRoot, "artifacts", "tests", "coreclr", buildDirName);
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
                                Console.WriteLine("Using --core_root {0}", _platformPath);
                            }
                            if (tryCrossgen != null)
                            {
                                _crossgenExe = tryCrossgen;
                                needCrossgen = false;
                                Console.WriteLine("Using --crossgen {0}", _crossgenExe);
                            }
                            if (tryTestPath != null)
                            {
                                _testPath = tryTestPath;
                                needTestTree = false;
                                Console.WriteLine("Using --test_root {0}", _testPath);
                            }

                            if (build != "Release")
                            {
                                Console.WriteLine();
                                Console.WriteLine("Warning: it is best practice to use a Release build for --core_root, --crossgen, and --test_root.");
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
                        Console.Error.WriteLine("error: didn't find --core_root default");
                    }
                    if (needCrossgen)
                    {
                        Console.Error.WriteLine("error: didn't find --crossgen default");
                    }
                    if (needTestTree)
                    {
                        Console.Error.WriteLine("error: didn't find --test_path default");
                    }
                    if (needBasePath)
                    {
                        Console.Error.WriteLine("error: didn't find --base default");
                    }
                    if (needDiffPath)
                    {
                        Console.Error.WriteLine("error: didn't find --diff default");
                    }
                }

                if (!_corelib && !_frameworks && !_benchmarks && !_tests && (_assemblyList.Count == 0))
                {
                    // Setting --corelib as the default
                    Console.WriteLine("No assemblies specified; defaulting to corelib");
                    _corelib = true;
                }

                if (Verbose)
                {
                    Console.WriteLine("After setting defaults:");
                    if (_baseRoot != null)
                    {
                        Console.WriteLine("--base_root {0}", _baseRoot);
                    }
                    if (_diffRoot != null)
                    {
                        Console.WriteLine("--diff_root {0}", _diffRoot);
                    }
                    if (_platformPath != null)
                    {
                        Console.WriteLine("--core_root {0}", _platformPath);
                    }
                    if (_crossgenExe != null)
                    {
                        Console.WriteLine("--crossgen {0}", _crossgenExe);
                    }
                    if (_arch != null)
                    {
                        Console.WriteLine("--arch {0}", _arch);
                    }
                    if (_build != null)
                    {
                        Console.WriteLine("--build {0}", _build);
                    }
                    if (_outputPath != null)
                    {
                        Console.WriteLine("--output {0}", _outputPath);
                    }
                    if (_basePath != null)
                    {
                        Console.WriteLine("--base {0}", _basePath);
                    }
                    if (_diffPath != null)
                    {
                        Console.WriteLine("--diff {0}", _diffPath);
                    }
                    if (_testPath != null)
                    {
                        Console.WriteLine("--test_root {0}", _testPath);
                    }
                }
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
                if (!_configFileLoaded)
                {
                    // Early out if there is no JIT_UTILS_ROOT.
                    return;
                }

                if (_jObj[s_configFileRootKey] == null)
                {
                    // No configuration for this tool
                    return;
                }

                var tools = (JsonArray)_jObj[s_configFileRootKey]["tools"];
                if (tools == null)
                {
                    return;
                }

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
                    case Commands.PmiDiff:
                        ValidateDiff();
                        break;
                    case Commands.Install:
                        ValidateInstall();
                        break;
                    case Commands.Uninstall:
                        ValidateUninstall();
                        break;
                    case Commands.List:
                        ValidateList();
                        break;
                }

                if (_validationError)
                {
                    jitdiff.DisplayCommandHelp(_command);
                    Environment.Exit(-1);
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
                    DisplayErrorMessage("--benchmarks requires specifying --test_root or --diff_root or running in the runtime tree");
                }

                if (_tests && (_testPath == null))
                {
                    DisplayErrorMessage("--tests requires specifying --test_root or --diff_root or running in the runtime tree");
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

                if ((_number == null) && !_lastSuccessful)
                {
                    DisplayErrorMessage("Specify --number or --last_successful to identify build to install.");
                }

                if (_noJitUtilsRoot)
                {
                    DisplayErrorMessage("JIT_UTILS_ROOT environment variable not set.");
                }
                else if (!_configFileLoaded)
                {
                    DisplayErrorMessage(s_configFileName + " file not loaded.");
                }
                if (!_configFileLoaded)
                {
                    DisplayErrorMessage("\"install\" command requires a valid " + s_configFileName + " file loaded from the directory specified by the JIT_UTILS_ROOT environment variable.");
                }
            }

            private void ValidateUninstall()
            {
                if (_tag == null)
                {
                    DisplayErrorMessage("Specify --tag <tag>");
                }

                if (_noJitUtilsRoot)
                {
                    DisplayErrorMessage("JIT_UTILS_ROOT environment variable not set.");
                }
                else if (!_configFileLoaded)
                {
                    DisplayErrorMessage(s_configFileName + " file not loaded.");
                }
                if (!_configFileLoaded)
                {
                    DisplayErrorMessage("\"uninstall\" command requires a valid " + s_configFileName + " file loaded from the directory specified by the JIT_UTILS_ROOT environment variable.");
                }
            }

            private void ValidateList()
            {
                if (_noJitUtilsRoot)
                {
                    DisplayErrorMessage("JIT_UTILS_ROOT environment variable not set.");
                }
                else if (!_configFileLoaded)
                {
                    DisplayErrorMessage(s_configFileName + " file not loaded.");
                }
                if (!_configFileLoaded)
                {
                    DisplayErrorMessage("\"list\" command requires a valid " + s_configFileName + " file loaded from the directory specified by the JIT_UTILS_ROOT environment variable.");
                }
            }

            public string GetToolPath(string tool, out bool found)
            {
                if (!_configFileLoaded || (_jObj[s_configFileRootKey] == null) || (_jObj[s_configFileRootKey]["default"] == null))
                {
                    // This should never happen; the caller should ensure that.
                    found = false;
                    return null;
                }

                var token = _jObj[s_configFileRootKey]["default"][tool];

                if (token != null)
                {
                    found = true;

                    string tag = token.ToString();

                    // Extract set value for tool and see if we can find it
                    // in the installed tools.
                    var tools = (JsonArray)_jObj[s_configFileRootKey]["tools"];
                    foreach (JsonNode installedTool in tools)
                    {
                        if ((string)installedTool["tag"] == tag)
                        {
                            // If the tag resolves to a tool, return the resolved path.
                            return (string)installedTool["path"];
                        }
                    }

                    // If the tag doesn't resolve to an installed tool, return it as
                    // a possible path.
                    return tag;
                }

                found = false;
                return null;
            }

            public T ExtractDefault<T>(string name, out bool found)
            {
                if (!_configFileLoaded || (_jObj[s_configFileRootKey] == null) || (_jObj[s_configFileRootKey]["default"] == null))
                {
                    // This should never happen; the caller should ensure that.
                    found = false;
                    return default(T);
                }

                var token = _jObj[s_configFileRootKey]["default"][name];

                if (token != null)
                {
                    found = true;

                    try
                    {
                        return token.GetValue<T>();
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

                string configFilePath = Path.Combine(_jitUtilsRoot, s_configFileName);
                if (!File.Exists(configFilePath))
                {
                    Console.Error.WriteLine("Can't find {0}", configFilePath);
                    return;
                }

                string configJson = File.ReadAllText(configFilePath);

                try
                {
                    _jObj = (JsonObject)JsonObject.Parse(configJson);
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine("Error reading config file: {0}", ex.Message);
                    Console.Error.WriteLine("Continuing; ignoring config file.");
                    return;
                }

                // Flag that the config file is loaded.
                _configFileLoaded = true;

                Console.WriteLine("Environment variable JIT_UTILS_ROOT found - configuration loaded.");

                // Now process the configuration file data.

                if (_jObj[s_configFileRootKey] == null)
                {
                    Console.Error.WriteLine("Warning: no {0} section of config file {1}", s_configFileRootKey, configFilePath);
                }
                // Check if there is any default config specified.
                else if (_jObj[s_configFileRootKey]["default"] != null)
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
                            Console.WriteLine("Default crossgen file {0} not found! Investigate config file entry.", crossgenExe);
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

                    // Set flag from default for debuginfo.
                    var debuginfo = ExtractDefault<bool>("debuginfo", out found);
                    _debuginfo = (found) ? debuginfo : _debuginfo;

                    // Set flag from default for tag.
                    var tag = ExtractDefault<string>("tag", out found);
                    _tag = (found) ? tag : _tag;

                    // Set flag from default for verbose.
                    var verbose = ExtractDefault<bool>("verbose", out found);
                    _verbose = (found) ? verbose : _verbose;

                    // Set flag from default for tier0.
                    var tier0 = ExtractDefault<bool>("tier0", out found);
                    _tier0 = (found) ? tier0 : _tier0;
                }
            }

            public enum DefaultType
            {
                DT_path,
                DT_file,
                DT_bool,
                DT_string
            }

            void PrintDefault(string parameter, DefaultType dt)
            {
                bool found;
                var defaultValue = ExtractDefault<string>(parameter, out found);
                if (found)
                {
                    Console.WriteLine("\t{0}: {1}", parameter, defaultValue);

                    switch (dt)
                    {
                        case DefaultType.DT_path:
                            if (!Directory.Exists(defaultValue))
                            {
                                Console.WriteLine("\t\tWarning: path not found");
                            }
                            break;
                        case DefaultType.DT_file:
                            if (!File.Exists(defaultValue))
                            {
                                Console.WriteLine("\t\tWarning: file not found");
                            }
                            break;
                        case DefaultType.DT_bool:
                            break;
                        case DefaultType.DT_string:
                            break;
                    }
                }
            }

            public int ListCommand()
            {
                string configPath = Path.Combine(_jitUtilsRoot, s_configFileName);
                Console.WriteLine("Listing {0} key in {1}", s_configFileRootKey, configPath);
                Console.WriteLine();

                var asmdiffNode = _jObj[s_configFileRootKey];
                if (asmdiffNode == null)
                {
                    Console.WriteLine("No {0} key data.", s_configFileRootKey);
                    return 0;
                }

                // Check if there is any default config specified.
                if (asmdiffNode["default"] != null)
                {
                    Console.WriteLine("Defaults:");

                    PrintDefault("base", DefaultType.DT_path);
                    PrintDefault("diff", DefaultType.DT_path);
                    PrintDefault("crossgen", DefaultType.DT_file);
                    PrintDefault("output", DefaultType.DT_path);
                    PrintDefault("core_root", DefaultType.DT_path);
                    PrintDefault("test_root", DefaultType.DT_path);
                    PrintDefault("base_root", DefaultType.DT_path);
                    PrintDefault("diff_root", DefaultType.DT_path);
                    PrintDefault("arch", DefaultType.DT_string);
                    PrintDefault("build", DefaultType.DT_string);
                    PrintDefault("noanalyze", DefaultType.DT_bool);
                    PrintDefault("corelib", DefaultType.DT_bool);
                    PrintDefault("frameworks", DefaultType.DT_bool);
                    PrintDefault("benchmarks", DefaultType.DT_bool);
                    PrintDefault("tests", DefaultType.DT_bool);
                    PrintDefault("gcinfo", DefaultType.DT_bool);
                    PrintDefault("debuginfo", DefaultType.DT_bool);
                    PrintDefault("tag", DefaultType.DT_string);
                    PrintDefault("verbose", DefaultType.DT_bool);
                    PrintDefault("tier0", DefaultType.DT_bool);
                    Console.WriteLine();
                }

                // Print list of the installed tools.

                var tools = (JsonArray)asmdiffNode["tools"];
                if (tools != null)
                {
                    Console.WriteLine("Installed tools:");

                    foreach (JsonObject tool in tools)
                    {
                        string tag = (string)tool["tag"];
                        string path = (string)tool["path"];
                        if (_verbose)
                        {
                            Console.WriteLine("\t{0}: {1}", tag, path);
                        }
                        else
                        {
                            Console.WriteLine("\t{0}", tag);
                        }
                        if (!Directory.Exists(path))
                        {
                            Console.WriteLine("\t\tWarning: tool not found");
                        }
                    }

                    Console.WriteLine();
                }

                return 0;
            }

            public bool DoDiffCompiles { get { return _diffSpecified; } }
            public bool DoBaseCompiles { get { return _baseSpecified; } }
            public string CoreRoot { get { return _platformPath; } }
            public string TestRoot { get { return _testPath; } }
            public string BasePath { get { return _basePath; } }
            public string DiffPath { get { return _diffPath; } }
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
            public bool GenerateDebugInfo { get { return _debuginfo; } }
            public bool Verbose { get { return _verbose; } }
            public bool NoDiffable { get { return _noDiffable; } }
            public bool Tier0 { get { return _tier0; } }
            public bool DoAnalyze { get { return !_noanalyze; } }
            public Commands DoCommand { get { return _command; } }
            public string JobName { get { return _jobName; } }
            public bool DoLastSucessful { get { return _lastSuccessful; } }
            public string JitUtilsRoot { get { return _jitUtilsRoot; } }
            public bool HasJitUtilsRoot { get { return (_jitUtilsRoot != null); } }
            public string RID { get { return _rid; } }
            public string Number { get { return _number; } }
            public string BranchName { get { return _branchName; } }
            public string AltJit { get { return _altjit; } }
            public string Arch {  get { return _arch;  } }
            public IReadOnlyList<string> AssemblyList => _assemblyList;
            public bool tsv {  get { return _tsv;  } }
            public bool Cctors => _cctors;
            public int  Count { get { return _count; } }
            public string Metric => _metric;
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
            "BenchmarksGame",
            "Benchstones",
            "Burgers",
            "Bytemark",
            "Devirtualization",
            "FractalPerf",
            "Inlining",
            "Layout",
            "Linq",
            "Math",
            "Roslyn",
            "SciMark",
            "Serialization",
            "SIMD",
            "Span",
            "V8"
        };

        private const string CoreLibAssemblyName = "System.Private.CoreLib.dll";

        private static RootCommand CreateRootCommand(string[] args) =>
            new JitDiffRootCommand(args)
                .UseVersion()
                .UseExtendedHelp(PrintExtendedHelp);

        internal static void DisplayCommandHelp(Commands command)
        {
            string[] helpArgs = command switch
            {
                Commands.Diff or Commands.PmiDiff => ["diff", "--help"],
                Commands.List => ["list", "--help"],
                Commands.Install => ["install", "--help"],
                Commands.Uninstall => ["uninstall", "--help"],
                _ => ["--help"]
            };

            CreateRootCommand([]).Parse(helpArgs).Invoke();
        }

        public static void PrintExtendedHelp(ParseResult parseResult)
        {
            if (!string.Equals(parseResult.CommandResult.Command.Name, "diff", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Console.WriteLine("""

            Examples:

            jit-diff diff --output c:\diffs --corelib --core_root c:\runtime\artifacts\tests\coreclr\windows.x64.Release\Tests\Core_Root --base c:\runtime_base\artifacts\bin\coreclr\windows.x64.Checked --diff c:\runtime\artifacts\bin\coreclr\windows.x64.Checked
                Generate corelib prejit diffs by specifying explicit baseline and diff compiler directories.

            jit-diff diff --output c:\diffs --base --base_root c:\runtime_base --diff
                If run from a dotnet/runtime clone, infer the remaining paths and architecture/build defaults.

            jit-diff diff --base --diff
                Same as above, but use the default output directory under artifacts\diffs.

            jit-diff diff --base --diff --pmi
                Diff jitted code via PMI instead of prejitting with crossgen.

            jit-diff diff --diff --pmi --assembly test.exe
                Diff all jitted methods for a specific assembly.

            jit-diff diff --diff --arch x86
                Force x86 diffs even when x64 defaults are available.
            """);
        }

        public static int Main(string[] args)
        {
            var command = CreateRootCommand(args);
            return command.Parse(args).Invoke();
        }
    }
}
