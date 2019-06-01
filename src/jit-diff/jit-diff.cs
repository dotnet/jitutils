// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using CommandResult = Microsoft.DotNet.Cli.Utils.CommandResult;
using Command = System.CommandLine.Command;
using System.CommandLine.Invocation;

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

    public partial class jitdiff
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
        private static string[] s_defaultDiffDirectoryPath = { "bin", "diffs" };

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

        private static string GetBuildOS(string platformMoniker)
        {
            switch (platformMoniker)
            {
                case "Windows":
                    return "Windows_NT";
                case "Linux":
                    return "Linux";
                case "OSX":
                    return "OSX";
                default:
                    Console.Error.WriteLine("No platform mapping! (Platform moniker = {0})", platformMoniker);
                    return null;
            }
        }

        public class Config
        {
            public static string s_DefaultBaseDiff = "^^^";
            public bool BaseSpecified = false;    // True if user specified "--base" or "--base <path>" or "--base <tag>"
            public bool DiffSpecified = false;    // True if user specified "--diff" or "--diff <path>" or "--diff <tag>"
            private JObject _jObj;
            private bool _configFileLoaded = false;
            private bool _noJitUtilsRoot = false;

            public void SetRID()
            {
                // Extract system RID from dotnet cli
                List<string> commandArgs = new List<string> { "--info" };
                CommandResult result = Utility.TryCommand("dotnet", commandArgs, true);

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
                        Rid = ridMatch.Groups[1].Value;
                        continue;
                    }

                    Regex platPattern = new Regex(@"Platform:\s*([A-Za-z0-9]*)$");
                    Match platMatch = platPattern.Match(line);
                    if (platMatch.Success)
                    {
                        PlatformName = platMatch.Groups[1].Value;
                        continue;
                    }
                }
            }

            public string PlatformMoniker
            {
                get
                {
                    switch (PlatformName)
                    {
                        case "Windows":
                            return "Windows";
                        case "Linux":
                            return "Linux";
                        case "Darwin":
                            return "OSX";
                        default:
                            Console.Error.WriteLine("No platform mapping! (Platform name = {0})", PlatformName);
                            return null;
                    }
                }
            }

            public void SetDiffDefaults()
            {
                bool needOutputPath = (Output == null);                            // We need to find --output
                bool needCoreRoot = (Core_Root == null);                            // We need to find --core_root

                // It's not clear we should find a default for crossgen: in the current code, if crossgen is specified,
                // then we always use that. If not specified, we find crossgen in core_root. That seems appropriate to
                // continue, without this default.
                // bool needCrossgen = (_crossgenExe == null);                             // We need to find --crossgen
                bool needCrossgen = false;

                bool needBasePath = BaseSpecified && (Base == null);              // We need to find --base
                bool needDiffPath = DiffSpecified && (Diff == null);              // We need to find --diff
                bool needTestTree = (Benchmarks || Tests) && (TestPath == null);  // We need to find --test_root

                bool needDiffRoot = (Diff_Root == null) &&
                    (needOutputPath || needCoreRoot || needCrossgen || needDiffPath || needTestTree);

                // If --diff_root wasn't specified, see if we can figure it out from the current directory
                // using git.

                if (needDiffRoot)
                {
                    Diff_Root = Utility.GetRepoRoot(Verbose);
                }

                if (needOutputPath && (Diff_Root != null))
                {
                    Output = Utility.CombinePath(Diff_Root, s_defaultDiffDirectoryPath);
                    PathUtility.EnsureDirectoryExists(Output);

                    Console.WriteLine("Using --output {0}", Output);
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
                    //    test_root: c:\gh\coreclr\bin\tests\Windows_NT.x64.Release
                    //    Core_Root: c:\gh\coreclr\bin\tests\Windows_NT.x64.Release\Tests\Core_Root
                    //    base/diff: c:\gh\coreclr\bin\Product\Windows_NT.x64.Checked

                    List<string> archList;
                    List<string> buildList;

                    if (Arch == null)
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
                        archList = new List<string> { Arch };
                    }

                    foreach (var arch in archList)
                    {
                        if (Build == null)
                        {
                            buildList = new List<string> { "Checked", "Debug" };
                        }
                        else
                        {
                            buildList = new List<string> { Build };
                        }

                        foreach (var build in buildList)
                        {
                            var buildDirName = GetBuildOS(PlatformMoniker) + "." + arch + "." + build;
                            string tryBasePath = null, tryDiffPath = null;

                            if (needBasePath && (Base_Root != null))
                            {
                                tryBasePath = Path.Combine(Base_Root, "bin", "Product", buildDirName);
                                if (!Directory.Exists(tryBasePath))
                                {
                                    continue;
                                }
                            }

                            if (needDiffPath && (Diff_Root != null))
                            {
                                tryDiffPath = Path.Combine(Diff_Root, "bin", "Product", buildDirName);
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
                                Base= tryBasePath;
                                needBasePath = false;
                                Console.WriteLine($"Using --base {Base}");
                            }

                            if (tryDiffPath != null)
                            {
                                Diff = tryDiffPath;
                                needDiffPath = false;
                                Console.WriteLine($"Using --diff {Diff}");
                            }

                            if (Arch == null)
                            {
                                Arch = arch;
                                Console.WriteLine($"Using --arch {Arch}");
                            }
                            break;
                        }

                        if (Build == null)
                        {
                            buildList = new List<string> { "Release", "Checked", "Debug" };
                        }
                        else
                        {
                            buildList = new List<string> { Build };
                        }

                        foreach (var build in buildList)
                        {
                            var buildDirName = GetBuildOS(PlatformMoniker) + "." + arch + "." + build;
                            string tryPlatformPath = null, tryCrossgen = null, tryTestPath = null;

                            if (needCoreRoot && (Diff_Root != null))
                            {
                                tryPlatformPath = Path.Combine(Diff_Root, "bin", "tests", buildDirName, "Tests", "Core_Root");
                                if (!Directory.Exists(tryPlatformPath))
                                {
                                    continue;
                                }
                            }

                            if (needCrossgen && (Diff_Root != null))
                            {
                                tryCrossgen = Path.Combine(Diff_Root, "bin", "Product", buildDirName, GetCrossgenExecutableName(PlatformMoniker));
                                if (!File.Exists(tryCrossgen))
                                {
                                    continue;
                                }
                            }

                            if (needTestTree && (Diff_Root != null))
                            {
                                tryTestPath = Path.Combine(Diff_Root, "bin", "tests", buildDirName);
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
                                Core_Root = tryPlatformPath;
                                needCoreRoot = false;
                                Console.WriteLine("Using --core_root {0}", Core_Root);
                            }
                            if (tryCrossgen != null)
                            {
                                Crossgen = tryCrossgen;
                                needCrossgen = false;
                                Console.WriteLine("Using --crossgen {0}", Crossgen);
                            }
                            if (tryTestPath != null)
                            {
                                TestPath = tryTestPath;
                                needTestTree = false;
                                Console.WriteLine("Using --test_root {0}", TestPath);
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

                if (!CoreLib && !Frameworks && !Benchmarks && !Tests && (Assembly.Count == 0))
                {
                    // Setting --corelib as the default
                    Console.WriteLine("No assemblies specified; defaulting to corelib");
                    CoreLib = true;
                }

                if (Verbose)
                {
                    Console.WriteLine("After setting defaults:");
                    if (Base_Root != null)
                    {
                        Console.WriteLine("--base_root {0}", Base_Root);
                    }
                    if (Diff_Root != null)
                    {
                        Console.WriteLine("--diff_root {0}", Diff_Root);
                    }
                    if (Core_Root != null)
                    {
                        Console.WriteLine("--core_root {0}", Core_Root);
                    }
                    if (Crossgen != null)
                    {
                        Console.WriteLine("--crossgen {0}", Crossgen);
                    }
                    if (Arch != null)
                    {
                        Console.WriteLine("--arch {0}", Arch);
                    }
                    if (Build != null)
                    {
                        Console.WriteLine("--build {0}", Build);
                    }
                    if (Output != null)
                    {
                        Console.WriteLine("--output {0}", Output);
                    }
                    if (Base != null)
                    {
                        Console.WriteLine("--base {0}", Base);
                    }
                    if (Diff != null)
                    {
                        Console.WriteLine("--diff {0}", Diff);
                    }
                    if (TestPath != null)
                    {
                        Console.WriteLine("--test_root {0}", TestPath);
                    }
                }
            }

            public void DeriveOutputTag()
            {
                if (Tag == null)
                {
                    int currentCount = 0;
                    foreach (var dir in Directory.EnumerateDirectories(Output))
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
                    Tag = String.Format("dasmset_{0}", currentCount);
                }
            }

            public void ExpandToolTags()
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

                var tools = _jObj[s_configFileRootKey]["tools"];
                if (tools == null)
                {
                    return;
                }

                foreach (var tool in tools)
                {
                    var tag = (string)tool["tag"];
                    var path = (string)tool["path"];

                    if (Base == tag)
                    {
                        // passed base tag matches installed tool, reset path.
                        Base = path;
                    }

                    if (Diff == tag)
                    {
                        // passed diff tag matches installed tool, reset path.
                        Diff = path;
                    }
                }
            }

            //private void Validate()
            //{
            //    _validationError = false;

            //    switch (_command)
            //    {
            //        case Commands.Diff:
            //        case Commands.PmiDiff:
            //            ValidateDiff();
            //            break;
            //        case Commands.Install:
            //            ValidateInstall();
            //            break;
            //        case Commands.Uninstall:
            //            ValidateUninstall();
            //            break;
            //        case Commands.List:
            //            ValidateList();
            //            break;
            //    }

            //    if (_validationError)
            //    {
            //        DisplayDiffUsageMessage();
            //        Environment.Exit(-1);
            //    }
            //}

            public void DisplayDiffUsageMessage()
            {
                string[] diffExampleText = {
                    @"Examples:",
                    @"",
                    @"  jit-diff diff --output c:\diffs --corelib --core_root c:\coreclr\bin\tests\Windows_NT.x64.Release\Tests\Core_Root --base c:\coreclr_base\bin\Product\Windows_NT.x64.Checked --diff c:\coreclr\bin\Product\Windows_NT.x86.Checked",
                    @"      Generate diffs of prejitted code for System.Private.CoreLib.dll by specifying baseline and",
                    @"      diff compiler directories explicitly.",
                    @"",
                    @"  jit-diff diff --output c:\diffs --base c:\coreclr_base\bin\Product\Windows_NT.x64.Checked --diff",
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
                    @"  jit-diff diff --base --diff --pmi",
                    @"      Does the same as the prevous example (if run from c:\coreclr tree)",
                    @"      but shows diffs for jitted code, via PMI",
                    @"",
                    @"  jit-diff diff --diff",
                    @"      Only generates asm using the diff JIT -- does not generate asm from a baseline compiler --",
                    @"      using all computed defaults.",
                    @"",
                    @"  jit-diff diff --diff --pmi --assembly test.exe",
                    @"      Generates asm using the diff JIT, showing jitted code for all methods",
                    @"      in the assembly test.exe",
                    @"",
                    @"  jit-diff diff --diff --arch x86",
                    @"      Generate diffs, but for x86, even if there is an x64 compiler available.",
                    @"",
                    @"  jit-diff diff --diff --build Debug",
                    @"      Generate diffs, but using a Debug build, even if there is a Checked build available."
                    };
                foreach (var line in diffExampleText)
                {
                    Console.Error.WriteLine(line);
                }
            }

            private void DisplayErrorMessage(string error)
            {
                Console.Error.WriteLine("error: {0}", error);
                Error = true;
            }

            public void ValidateDiff()
            {
                if (Core_Root == null)
                {
                    DisplayErrorMessage("Specify --core_root <path>");
                }

                if (CoreLib && Frameworks)
                {
                    DisplayErrorMessage("Specify only one of --corelib or --frameworks");
                }

                if (Benchmarks && Tests)
                {
                    DisplayErrorMessage("Specify only one of --benchmarks or --tests");
                }

                if (Benchmarks && (TestPath == null))
                {
                    DisplayErrorMessage("--benchmarks requires specifying --test_root or --diff_root or running in the coreclr tree");
                }

                if (Tests && (TestPath == null))
                {
                    DisplayErrorMessage("--tests requires specifying --test_root or --diff_root or running in the coreclr tree");
                }

                if (Output == null)
                {
                    DisplayErrorMessage("Specify --output <path>");
                }
                else if (!Directory.Exists(Output))
                {
                    DisplayErrorMessage("Can't find --output path.");
                }

                if ((Base == null) && (Diff == null))
                {
                    DisplayErrorMessage("Specify either --base or --diff or both.");
                }

                if (Base != null && !Directory.Exists(Base))
                {
                    DisplayErrorMessage("Can't find --base directory.");
                }

                if (Diff != null && !Directory.Exists(Diff))
                {
                    DisplayErrorMessage("Can't find --diff directory.");
                }

                if (Crossgen != null && !File.Exists(Crossgen))
                {
                    DisplayErrorMessage("Can't find --crossgen executable.");
                }
            }

            private void ValidateInstall()
            {
                if (JobName == null)
                {
                    DisplayErrorMessage("Specify --job <name>");
                }

                if ((Number == null) && !LastSuccessful)
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
                if (Tag == null)
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

            public void LoadFileConfig()
            {
                JitUtilsRoot = Environment.GetEnvironmentVariable("JIT_UTILS_ROOT");
                if (JitUtilsRoot == null)
                {
                    _noJitUtilsRoot = true;
                    return;
                }

                string configFilePath = Path.Combine(JitUtilsRoot, s_configFileName);
                if (!File.Exists(configFilePath))
                {
                    Console.Error.WriteLine("Can't find {0}", configFilePath);
                    return;
                }

                string configJson = File.ReadAllText(configFilePath);

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
                            Base = basePath;
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
                            Diff = diffPath;
                        }
                    }

                    // Find crossgen tool if any
                    var crossgenExe = GetToolPath("crossgen", out found);
                    if (found)
                    {
                        if (!File.Exists(Crossgen))
                        {
                            Console.WriteLine("Default crossgen file {0} not found! Investigate config file entry.", crossgenExe);
                        }
                        else
                        {
                            Crossgen = crossgenExe;
                        }
                    }

                    // Set up output
                    var outputPath = ExtractDefault<string>("output", out found);
                    Output = (found) ? outputPath : Output;

                    // Setup platform path (core_root).
                    var platformPath = ExtractDefault<string>("core_root", out found);
                    Core_Root = (found) ? platformPath : Core_Root;

                    // Set up test path (test_root).
                    var testPath = ExtractDefault<string>("test_root", out found);
                    TestPath = (found) ? testPath : TestPath;

                    var baseRoot = ExtractDefault<string>("base_root", out found);
                    Base_Root = (found) ? baseRoot : Base_Root;

                    var diffRoot = ExtractDefault<string>("diff_root", out found);
                    Diff_Root = (found) ? diffRoot : Diff_Root;

                    var arch = ExtractDefault<string>("arch", out found);
                    Arch = (found) ? arch : Arch;

                    var build = ExtractDefault<string>("build", out found);
                    Build = (found) ? build : Build;

                    // Set flag from default for analyze.
                    var noanalyze = ExtractDefault<bool>("noanalyze", out found);
                    NoAnalyze = (found) ? noanalyze : NoAnalyze;

                    // Set flag from default for corelib.
                    var corelib = ExtractDefault<bool>("corelib", out found);
                    CoreLib = (found) ? corelib : CoreLib;

                    // Set flag from default for frameworks.
                    var frameworks = ExtractDefault<bool>("frameworks", out found);
                    Frameworks = (found) ? frameworks : Frameworks;

                    // Set flag from default for benchmarks.
                    var benchmarks = ExtractDefault<bool>("benchmarks", out found);
                    Benchmarks = (found) ? benchmarks : Benchmarks;

                    // Set flag from default for tests.
                    var tests = ExtractDefault<bool>("tests", out found);
                    Tests = (found) ? tests : Tests;

                    // Set flag from default for gcinfo.
                    var gcinfo = ExtractDefault<bool>("gcinfo", out found);
                    GcInfo = (found) ? gcinfo : GcInfo;

                    // Set flag from default for debuginfo.
                    var debuginfo = ExtractDefault<bool>("debuginfo", out found);
                    DebugInfo = (found) ? debuginfo : DebugInfo;

                    // Set flag from default for tag.
                    var tag = ExtractDefault<string>("tag", out found);
                    Tag = (found) ? tag : Tag;

                    // Set flag from default for verbose.
                    var verbose = ExtractDefault<bool>("verbose", out found);
                    Verbose = (found) ? verbose : Verbose;
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
                string configPath = Path.Combine(JitUtilsRoot, s_configFileName);
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

                    Console.WriteLine();
                }

                // Print list of the installed tools.

                var tools = asmdiffNode["tools"];
                if (tools != null)
                {
                    Console.WriteLine("Installed tools:");

                    foreach (var tool in tools.Children())
                    {
                        string tag = (string)tool["tag"];
                        string path = (string)tool["path"];
                        if (Verbose)
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

            public Commands Command { get; set; }
            public bool DoDiffCompiles { get { return DiffSpecified; } }
            public bool DoBaseCompiles { get { return BaseSpecified; } }
            public string Core_Root { get; set; }
            public string Test_Root { get; set; }
            public string Base { get; set; }
            public string Diff { get; set; }
            public string Crossgen { get; set; }
            public bool HasCrossgenExe { get { return (Crossgen != null); } }
            public string Output { get; set; }
            public bool Sequential { get; set; }
            public string TestPath { get; set; }
            public string Base_Root { get; set; }
            public string Diff_Root { get; set; }
            public string Tag { get; set; }
            public bool HasTag { get { return (Tag != null); } }
            public bool CoreLib { get; set; }
            public bool Frameworks { get; set; }
            public bool Benchmarks { get; set; }
            public bool Tests { get; set; }
            public bool GcInfo { get; set; }
            public bool DebugInfo { get; set; }
            public bool Verbose { get; set; }
            public bool NoAnalyze { get; set; }
            public bool DoAnalyze { get { return !NoAnalyze; } }
            public string JobName { get; set; }
            public bool LastSuccessful { get; set; }
            public string JitUtilsRoot { get; set; }
            public bool HasJitUtilsRoot { get { return (JitUtilsRoot != null); } }
            public string Rid { get; set; }
            public string Number { get; set; }
            public string BranchName { get; set; }
            public string PlatformName { get; set; }
            public string AltJit { get; set; }
            public string Arch { get; set; }
            public string Build { get; set; }
            public IReadOnlyList<string> Assembly { get; set; }
            public bool Tsv { get; set; }
            public bool Pmi { get; set; }
            public bool Cctors { get; set; }
            public bool Error { get; set; }
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

        private static string[] s_CoreLibAssembly =
        {
            "System.Private.CoreLib.dll"
        };

        // List of framework assemblies we will run diffs on.
        // Note: System.Private.CoreLib.dll should be first in this list.
        private static string[] s_frameworkAssemblies =
        {
            "System.Private.CoreLib.dll",
            "System.Private.Xml.dll",
            "Microsoft.CodeAnalysis.VisualBasic.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "Microsoft.Diagnostics.Tracing.TraceEvent.dll",
            "System.Data.Common.dll",
            "System.Private.DataContractSerialization.dll",
            "System.Linq.Parallel.dll",
            "Microsoft.CodeAnalysis.dll",
            "System.Linq.Expressions.dll",
            "Newtonsoft.Json.dll",
            "System.Net.Http.dll",
            "Microsoft.CSharp.dll",
            "System.ComponentModel.TypeConverter.dll",
            "NuGet.Protocol.Core.v3.dll",
            "xunit.execution.dotnet.dll",
            "CommandLine.dll",
            "Microsoft.DotNet.ProjectModel.dll",
            "System.Net.HttpListener.dll",
            "System.Threading.Tasks.Dataflow.dll",
            "System.Net.Mail.dll",
            "System.Net.Sockets.dll",
            "System.Runtime.Extensions.dll",
            "System.Collections.Immutable.dll",
            "System.Linq.dll",
            "System.Net.Security.dll",
            "Microsoft.VisualBasic.dll",
            "System.Collections.dll",
            "System.Private.Xml.Linq.dll",
            "System.Transactions.Local.dll",
            "NuGet.Packaging.dll",
            "System.Security.Cryptography.X509Certificates.dll",
            "System.Text.RegularExpressions.dll",
            "System.Net.Requests.dll",
            "System.Security.Cryptography.Algorithms.dll",
            "System.Runtime.Serialization.Formatters.dll",
            "System.IO.Compression.dll",
            "NuGet.Frameworks.dll",
            "System.Security.Permissions.dll",
            "System.Private.Uri.dll",
            "NuGet.ProjectModel.dll",
            "System.Security.AccessControl.dll",
            "System.Diagnostics.Process.dll",
            "System.Security.Cryptography.Cng.dll",
            "System.Collections.Concurrent.dll",
            "System.Linq.Queryable.dll",
            "Microsoft.DotNet.Cli.Utils.dll",
            "System.IO.FileSystem.dll",
            "System.Text.Encoding.CodePages.dll",
            "System.Net.Primitives.dll",
            "System.Reflection.Metadata.dll",
            "System.Runtime.Numerics.dll",
            "System.Security.Cryptography.Csp.dll",
            "System.Net.NetworkInformation.dll",
            "System.Numerics.Vectors.dll",
            "System.Net.WebClient.dll",
            "NuGet.Configuration.dll",
            "System.Console.dll",
            "System.CommandLine.dll",
            "System.ComponentModel.Annotations.dll",
            "xunit.assert.dll",
            "System.Diagnostics.TraceSource.dll",
            "System.Drawing.Primitives.dll",
            "Microsoft.Extensions.DependencyModel.dll",
            "System.Security.Principal.Windows.dll",
            "System.Collections.NonGeneric.dll",
            "NuGet.DependencyResolver.Core.dll",
            "System.Threading.Tasks.Parallel.dll",
            "System.IO.Pipes.dll",
            "System.Collections.Specialized.dll",
            "System.Net.WebSockets.Client.dll",
            "NuGet.Versioning.dll",
            "jit-analyze.dll",
            "System.ObjectModel.dll",
            "NuGet.Protocol.Core.Types.dll",
            "xunit.core.dll",
            "System.Net.WebSockets.dll",
            "System.Security.Claims.dll",
            "NuGet.Common.dll",
            "System.Net.Ping.dll",
            "xunit.performance.metrics.dll",
            "System.Security.Cryptography.Primitives.dll",
            "Microsoft.Win32.Registry.dll",
            "System.IO.IsolatedStorage.dll",
            "System.Net.NameResolution.dll",
            "NuGet.RuntimeModel.dll",
            "System.Threading.dll",
            "xunit.performance.execution.dll",
            "System.Reflection.DispatchProxy.dll",
            "System.Diagnostics.DiagnosticSource.dll",
            "System.Security.Cryptography.Encoding.dll",
            "NuGet.LibraryModel.dll",
            "System.ComponentModel.Primitives.dll",
            "System.IO.FileSystem.AccessControl.dll",
            "System.IO.FileSystem.Watcher.dll",
            "System.IO.MemoryMappedFiles.dll",
            "System.Net.WebHeaderCollection.dll",
            "NuGet.Packaging.Core.Types.dll",
            "System.Web.HttpUtility.dll",
            "System.Threading.Thread.dll",
            "NuGet.Packaging.Core.dll",
            "System.Runtime.InteropServices.dll",
            "jit-dasm.dll",
            "System.Resources.Writer.dll",
            "System.Memory.dll",
            "System.IO.FileSystem.DriveInfo.dll",
            "System.Diagnostics.Tracing.dll",
            "System.ComponentModel.EventBasedAsync.dll",
            "System.Net.ServicePoint.dll",
            "Microsoft.DotNet.InternalAbstractions.dll",
            "System.Diagnostics.TextWriterTraceListener.dll",
            "System.IO.Compression.ZipFile.dll",
            "System.Security.Cryptography.OpenSsl.dll",
            "System.Reflection.TypeExtensions.dll",
            "xunit.performance.core.dll",
            "System.Runtime.Serialization.Primitives.dll",
            "System.Diagnostics.FileVersionInfo.dll",
            "System.Diagnostics.StackTrace.dll",
            "System.Net.WebProxy.dll",
            "NuGet.Repositories.dll",
            "System.Runtime.InteropServices.RuntimeInformation.dll",
            "System.Runtime.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Diagnostics.Debug.dll",
            "Microsoft.Win32.Primitives.dll",
            "System.Runtime.CompilerServices.VisualC.dll",
            "System.Diagnostics.Tools.dll",
            "System.ComponentModel.dll",
            "System.Xml.XPath.XDocument.dll"
        };

        public static int Main(string[] args)
        {
            Command diffCommand = new Command("diff", "Run asm diffs via crossgen or pmi");
            {
                // how to hanle the dir/tag/aspect..?
                Option baseOption = new Option("--base", "The base compiler directory or tag. Will use crossgen, corerun, or clrjit from this directory.",
                    new Argument<string>(Config.s_DefaultBaseDiff) { Arity = ArgumentArity.ZeroOrOne });
                baseOption.AddAlias("-b");
                Option baseRootOption = new Option("--base_root", "Path to root of base dotnet/coreclr repo.", new Argument<string>());
                Option diffOption = new Option("--diff", "The diff compiler directory or tag. Will use crossgen, corerun, or clrjit from this directory.", 
                    new Argument<string>(Config.s_DefaultBaseDiff) { Arity = ArgumentArity.ZeroOrOne });
                diffOption.AddAlias("-d");
                Option diffRootOption = new Option("--diff_root", "Path to root of diff dotnet/coreclr repo.", new Argument<string>());
                Option crossgenOption = new Option("--crossgen", "The crossgen compiler exe. When this is specified, will use clrjit from the --base and " +
                    "--diff directories with this crossgen.", new Argument<string>());
                Option outputOption = new Option("--output", "The output path.", new Argument<string>());
                outputOption.AddAlias("-o");
                Option noAnalyzeOption = new Option("--noanalyze", "Do not analyze resulting base, diff dasm directories. (By default, the directories are analyzed for diffs.)", new Argument<bool>());
                Option sequentialOption = new Option("--sequential", "Run sequentially; don't do parallel compiles.", new Argument<bool>());
                sequentialOption.AddAlias("-s");
                Option tagOption = new Option("--tag", "Name of root in output directory. Allows for many sets of output.", new Argument<string>());
                tagOption.AddAlias("-t");
                Option corelibOption = new Option("--corelib", "Diff System.Private.CoreLib.dll.", new Argument<bool>());
                corelibOption.AddAlias("-c");
                Option frameworksOption = new Option("--frameworks", "Diff frameworks.", new Argument<bool>());
                frameworksOption.AddAlias("-f");
                Option benchmarksOption = new Option("--benchmarks", "Diff core benchmarks.", new Argument<bool>());
                Option testsOption = new Option("--tests", "Diff all tests.", new Argument<bool>());
                Option gcInfoOption = new Option("--gcinfo", "Add GC info to the disasm output.", new Argument<bool>());
                Option debugInfoOption = new Option("--debuginfo", "Add Debug info to the disasm output.", new Argument<bool>());
                Option verboseOption = new Option("--verbose", "Enable verbose output.", new Argument<bool>());
                verboseOption.AddAlias("-v");
                Option coreRootOption = new Option("--core_root", "Path to test CORE_ROOT.", new Argument<string>());
                Option testRootOption = new Option("--test_root", "Path to test tree.Use with--benchmarks or--tests.", new Argument<string>());
                Option archOption = new Option("--arch", "Architecture to diff (x86, x64).", new Argument<string>());
                Option buildOption = new Option("--build", "Build flavor to diff (Checked, Debug).", new Argument<string>());
                Option altJitOption = new Option("--altjit", "If set, the name of the altjit to use (e.g., protononjit.dll).", new Argument<string>());
                Option pmiOption = new Option("--pmi", "Run asm diffs via pmi.", new Argument<bool>());
                Option cctorsOption = new Option("--cctors", "With --pmi, jit and run cctors before jitting other methods", new Argument<bool>());
                Option tsvOption = new Option("--tsv", "Dump analysis data to diffs.tsv in output directory.", new Argument<bool>());
                Option assemblies = new Option("--assembly", "Run asm diffs on a given set of assemblies. An individual item can be an assembly or a directory tree containing assemblies.",
                    new Argument<string>() { Arity = ArgumentArity.OneOrMore });

                diffCommand.AddOption(baseOption);
                diffCommand.AddOption(baseRootOption);
                diffCommand.AddOption(diffOption);
                diffCommand.AddOption(diffRootOption);
                diffCommand.AddOption(crossgenOption);
                diffCommand.AddOption(outputOption);
                diffCommand.AddOption(noAnalyzeOption);
                diffCommand.AddOption(sequentialOption);
                diffCommand.AddOption(tagOption);
                diffCommand.AddOption(corelibOption);
                diffCommand.AddOption(frameworksOption);
                diffCommand.AddOption(benchmarksOption);
                diffCommand.AddOption(testsOption);
                diffCommand.AddOption(gcInfoOption);
                diffCommand.AddOption(debugInfoOption);
                diffCommand.AddOption(verboseOption);
                diffCommand.AddOption(coreRootOption);
                diffCommand.AddOption(testRootOption);
                diffCommand.AddOption(archOption);
                diffCommand.AddOption(buildOption);
                diffCommand.AddOption(altJitOption);
                diffCommand.AddOption(pmiOption);
                diffCommand.AddOption(cctorsOption);
                diffCommand.AddOption(tsvOption);
                diffCommand.AddOption(assemblies);

                diffCommand.Handler = CommandHandler.Create<Config>((config) =>
                {
                    // --base and --diff are tricky to model since they have 3 behaviors: 
                    // not specified, specified with no arg, and specified with arg.
                    // Try and sort that here...
                    if (config.Base != Config.s_DefaultBaseDiff)
                    {
                        config.BaseSpecified = true;

                        if (config.Base == String.Empty)
                        {
                            config.Base = null;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Base NOT specified!");
                        config.Base = null;
                    }

                    if (config.Diff != Config.s_DefaultBaseDiff)
                    {
                        config.DiffSpecified = true;

                        if (config.Diff == String.Empty)
                        {
                            config.Diff = null;
                        }
                    }
                    else
                    {
                        config.Diff = null;
                    }

                    if (config.Pmi)
                    {
                        config.Command = Commands.PmiDiff;
                    }
                    else
                    {
                        config.Command = Commands.Diff;
                    }

                    config.LoadFileConfig();
                    config.SetRID();
                    config.ExpandToolTags();
                    config.SetDiffDefaults();
                    config.ValidateDiff();

                    if (config.Error)
                    {
                        config.DisplayDiffUsageMessage();
                        return -1;
                    }

                    config.DeriveOutputTag();

                    // Elswhere we expect this to always be set to something.
                    if (config.Assembly == null)
                    {
                        config.Assembly = new List<string>();
                    }

                    return DiffTool.DiffCommand(config);
                });
            }

            Command listCommand = new Command("list", "List defaults and available tools in " + s_configFileName + ".");
            {
            }

            Command installCommand = new Command("install", "Install tool in " + s_configFileName + ".");
            {
            }

            Command uninstallCommand = new Command("uninstall", "Uninstall tool from " + s_configFileName + ".");

            RootCommand rootCommand = new RootCommand();
            rootCommand.AddCommand(diffCommand);
            rootCommand.AddCommand(listCommand);
            rootCommand.AddCommand(installCommand);
            rootCommand.AddCommand(uninstallCommand);

            //// Get configuration values from JIT_UTILS_ROOT/config.json
            //LoadFileConfig();

            //    // List command section.
            //    syntax.DefineCommand("list", ref _command, Commands.List,
            //        "List defaults and available tools in " + s_configFileName + ".");
            //    syntax.DefineOption("v|verbose", ref Verbose, "Enable verbose output.");

            //    // Install command section.
            //    syntax.DefineCommand("install", ref _command, Commands.Install, "Install tool in " + s_configFileName + ".");
            //    syntax.DefineOption("j|job", ref JobName, "Name of the job.");
            //    syntax.DefineOption("n|number", ref Number, "Job number.");
            //    syntax.DefineOption("l|last_successful", ref LastSuccessful, "Last successful build.");
            //    syntax.DefineOption("b|branch", ref BranchName, "Name of branch.");
            //    syntax.DefineOption("v|verbose", ref Verbose, "Enable verbose output.");

            //    // Uninstall command section.s
            //    syntax.DefineCommand("uninstall", ref _command, Commands.Uninstall, "Uninstall tool from " + s_configFileName + ".");
            //    syntax.DefineOption("t|tag", ref Tag, "Name of tool tag in config file.");

            //    _baseSpecified = baseOption.IsSpecified;
            //    _diffSpecified = diffOption.IsSpecified;

            //});

            //SetRID();

            //ExpandToolTags();

            //SetDefaults();

            //Validate();

            //if (_command == Commands.Diff || _command == Commands.PmiDiff)
            //{
            //    // Do additional initialization relevant for just the "diff" command.

            //    DeriveOutputTag();

            //    // Now that output path and tag are guaranteed to be set, update
            //    // the output path to included the tag.
            //    OutputPath = Path.Combine(OutputPath, Tag);
            //}
            // Config config = new Config(args);
            // int ret = 0;

            //switch (config.DoCommand)
            //{
            //    case Commands.Diff:
            //    case Commands.PmiDiff:
            //        {
            //            ret = DiffTool.DiffCommand(config);
            //        }
            //        break;
            //    case Commands.List:
            //        {
            //            // List command: list loaded configuration
            //            ret = config.ListCommand();
            //        }
            //        break;
            //    case Commands.Install:
            //        {
            //            ret = InstallCommand(config);
            //        }
            //        break;
            //    case Commands.Uninstall:
            //        {
            //            ret = UninstallCommand(config);
            //        }
            //        break;
            //}

            return rootCommand.InvokeAsync(args).Result;
        }
    }
}

