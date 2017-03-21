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

        public class Config
        {
            private ArgumentSyntax _syntaxResult;
            private Commands _command = Commands.Diff;
            private string _basePath = null;
            private string _diffPath = null;
            private string _crossgenExe = null;
            private string _outputPath = null;
            private bool _analyze = false;
            private string _tag = null;
            private bool _sequential = false;
            private string _platformPath = null;
            private string _testPath = null;
            private bool _corlibOnly = false;
            private bool _frameworksOnly = false;
            private bool _benchmarksOnly = false;
            private bool _verbose = false;
            private string _jobName;
            private string _number;
            private bool _lastSuccessful;
            private string _jitUtilsRoot;
            private string _rid;
            private string _platformName;
            private string _moniker;
            private string _branchName;

            private JObject _jObj;
            private bool _asmdiffLoaded = false;
            private bool _noJitUtilsRoot = false;

            public Config(string[] args)
            {
                // Get configuration values from JIT_UTILS_ROOT/config.json

                LoadFileConfig();

                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    // Diff command section.
                    syntax.DefineCommand("diff", ref _command, Commands.Diff, "Run asm diff of base/diff.");
                    syntax.DefineOption("b|base", ref _basePath, "The base compiler directory or tag." +
                                        " Will use crossgen or clrjit from this directory, depending on" +
                                        " whether --crossgen is specified.");
                    syntax.DefineOption("d|diff", ref _diffPath, "The diff compiler directory or tag." +
                                        " Will use crossgen or clrjit from this directory, depending on" +
                                        " whether --crossgen is specified.");
                    syntax.DefineOption("crossgen", ref _crossgenExe, "The crossgen compiler exe." +
                                        " When this is specified, will use clrjit from the --base and" +
                                        " --diff directories with this crossgen");
                    syntax.DefineOption("o|output", ref _outputPath, "The output path.");
                    syntax.DefineOption("a|analyze", ref _analyze, 
                        "Analyze resulting base, diff dasm directories.");
                    syntax.DefineOption("s|sequential", ref _sequential, "Run sequentially; don't do parallel compiles.");
                    syntax.DefineOption("t|tag", ref _tag, 
                        "Name of root in output directory.  Allows for many sets of output.");
                    syntax.DefineOption("c|corlibonly", ref _corlibOnly, "Disasm *CorLib only");
                    syntax.DefineOption("f|frameworksonly", ref _frameworksOnly, "Disasm frameworks only");
                    syntax.DefineOption("benchmarksonly", ref _benchmarksOnly, "Disasm core benchmarks only");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output");
                    syntax.DefineOption("core_root", ref _platformPath, "Path to test CORE_ROOT.");
                    syntax.DefineOption("test_root", ref _testPath, "Path to test tree.");

                    // List command section.
                    syntax.DefineCommand("list", ref _command, Commands.List, 
                        "List defaults and available tools config.json.");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output");

                    // Install command section.
                    syntax.DefineCommand("install", ref _command, Commands.Install, "Install tool in config.");
                    syntax.DefineOption("j|job", ref _jobName, "Name of the job.");
                    syntax.DefineOption("n|number", ref _number, "Job number.");
                    syntax.DefineOption("l|last_successful", ref _lastSuccessful, "Last successful build.");
                    syntax.DefineOption("b|branch", ref _branchName, "Name of branch.");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output");

                });


                // Run validation code on parsed input to ensure we have a sensible scenario.

                SetRID();

                Validate();

                ExpandToolTags();

                DeriveOutputTag();

                // Now that output path and tag are guaranteed to be set, update
                // the output path to included the tag.
                _outputPath = Path.Combine(_outputPath, _tag);
            }

            private void SetRID()
            {
                // Extract system RID from dotnet cli
                List<string> commandArgs = new List<string> { "--info" };
                CommandResult result = TryCommand("dotnet", commandArgs, true);

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("dotnet --info returned non-zero");
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
                    case "Linux" :
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

                var tools = _jObj["asmdiff"]["tools"];

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
                switch(_command)
                {
                    case Commands.Diff:
                    {
                        ValidateDiff();
                    }
                    break;
                    case Commands.Install:
                    {
                        ValidateInstall();
                    }
                    break;
                    case Commands.List:
                    break;
                }
            }
            
            private void ValidateDiff()
            {
                if (_platformPath == null)
                {
                    _syntaxResult.ReportError("Specifiy --core_root <path>");
                }

                if ((_corlibOnly == false) &&
                    (_frameworksOnly == false) && (_testPath == null))
                {
                    _syntaxResult.ReportError("Specify --test_root <path>");
                }

                if (_outputPath == null)
                {
                    _syntaxResult.ReportError("Specify --output <path>");
                }

                if (!Directory.Exists(_outputPath))
                {
                    _syntaxResult.ReportError("Can't find --output path.");
                }

                if ((_basePath == null) && (_diffPath == null))
                {
                    _syntaxResult.ReportError("--base <path> or --diff <path> or both must be specified.");
                }

                if (_basePath != null && !Directory.Exists(_basePath))
                {
                    _syntaxResult.ReportError("Can't find --base directory.");
                }

                if (_diffPath != null && !Directory.Exists(_diffPath))
                {
                    _syntaxResult.ReportError("Can't find --diff directory.");
                }

                if (_crossgenExe != null && !File.Exists(_crossgenExe))
                {
                    _syntaxResult.ReportError("Can't find --crossgen executable.");
                }

            }
            private void ValidateInstall()
            {
                if (_jobName == null)
                {
                    _syntaxResult.ReportError("Specify --jobName <name>");
                }
                
                if ((_number == null) && !_lastSuccessful)
                {
                    _syntaxResult.ReportError("Specify --number or --last_successful to identify build to install.");
                }
            }

            public string GetToolPath(string tool, out bool found)
            {
                var token = _jObj["asmdiff"]["default"][tool];

                if (token != null)
                {
                    found = true;

                    string tag = _jObj["asmdiff"]["default"][tool].Value<string>();

                    // Extract set value for tool and see if we can find it
                    // in the installed tools.
                    var path = _jObj["asmdiff"]["tools"].Children()
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
                var token = _jObj["asmdiff"]["default"][name];

                if (token != null)
                {
                    found = true;

                    try
                    {
                        return token.Value<T>();
                    }
                    catch (System.FormatException e)
                    {
                        Console.Error.WriteLine("Bad format for default {0}.  See config.json", name, e);
                    }
                }

                found = false;
                return default(T);
            }

            private void LoadFileConfig()
            {
                _jitUtilsRoot = Environment.GetEnvironmentVariable("JIT_UTILS_ROOT");

                if (_jitUtilsRoot != null)
                {
                    string path = Path.Combine(_jitUtilsRoot, "config.json");

                    if (File.Exists(path))
                    {
                        string configJson = File.ReadAllText(path);

                        _jObj = JObject.Parse(configJson);
                        
                        // Flag that the config.json is loaded.
                        _asmdiffLoaded = true;

                        // Check if there is any default config specified.
                        if (_jObj["asmdiff"]["default"] != null)
                        {
                            bool found;

                            // Find baseline tool if any.
                            string basePath = GetToolPath("base", out found);
                            if (found && !Directory.Exists(basePath))
                            {
                                Console.WriteLine("Default base path {0} not found! Investigate config file entry and retry.", basePath);
                                Environment.Exit(-1);
                            }

                            // Find diff tool if any
                            string diffPath = GetToolPath("diff", out found);
                            if (found && !Directory.Exists(diffPath))
                            {
                                Console.WriteLine("Default diff path {0} not found! Investigate config file entry and retry.", diffPath);
                                Environment.Exit(-1);
                            }

                            // Find crossgen tool if any
                            string crossgenPath = GetToolPath("crossgen", out found);
                            if (found && !Directory.Exists(crossgenPath))
                            {
                                Console.WriteLine("Default crossgen path {0} not found! Investigate config file entry and retry.", crossgenPath);
                                Environment.Exit(-1);
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

                            // Set flag from default for analyze.
                            var analyze = ExtractDefault<bool>("analyze", out found);
                            _analyze = (found) ? analyze : _analyze;
                            
                            // Set flag from default for corlib only.
                            var corlibOnly = ExtractDefault<bool>("corlibonly", out found);
                            _corlibOnly = (found) ? corlibOnly : _corlibOnly;
                            
                            // Set flag from default for frameworks only.
                            var frameworksOnly = ExtractDefault<bool>("frameworksonly", out found);
                            _frameworksOnly = (found) ? frameworksOnly : _frameworksOnly;

                            // Set flag from default for frameworks only.
                            var benchmarksOnly = ExtractDefault<bool>("benchmarksonly", out found);
                            _benchmarksOnly = (found) ? frameworksOnly : _benchmarksOnly;

                            // Set flag from default for tag.
                            var tag = ExtractDefault<string>("tag", out found);
                            _tag = (found) ? tag : _tag;
                            
                            // Set flag from default for verbose.
                            var verbose = ExtractDefault<bool>("verbose", out found);
                            _verbose = (found) ? verbose : _verbose;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("Can't find config.json on {0}", _jitUtilsRoot);
                    }
                }
                else
                {
                    Console.WriteLine("Environment variable JIT_UTILS_ROOT not found - no configuration loaded.");
                    _noJitUtilsRoot = true;
                }
            }

            void PrintTools()
            {
                var tools = _jObj["asmdiff"]["tools"];

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
                }
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
                if (!_asmdiffLoaded)
                {
                    Console.Error.WriteLine("Error: config.json isn't loaded.");
                    return -1;
                }
                
                Console.WriteLine();
                
                // Check if there is any default config specified.
                if (_jObj["asmdiff"]["default"] != null)
                {
                    Console.WriteLine("Defaults:");

                    PrintDefault<string>("base");
                    PrintDefault<string>("diff");
                    PrintDefault<string>("output");
                    PrintDefault<string>("core_root");
                    PrintDefault<string>("test_root");
                    PrintDefault<string>("analyze");
                    PrintDefault<string>("corlibonly");
                    PrintDefault<string>("frameworksonly");
                    PrintDefault<string>("benchmarksonly");
                    PrintDefault<string>("tag");
                    PrintDefault<string>("verbose");
                    
                    Console.WriteLine();
                }

                // Print list of sthe installed tools.
                PrintTools();

                Console.WriteLine();

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
            public bool CoreLibOnly { get { return _corlibOnly; } }
            public bool FrameworksOnly { get { return _frameworksOnly; } }
            public bool BenchmarksOnly { get { return _benchmarksOnly; } }
            public bool DoMSCorelib { get { return !_benchmarksOnly; } }
            public bool DoFrameworks { get { return !_corlibOnly && !_benchmarksOnly; } }
            public bool DoTestTree { get { return (!_corlibOnly && !_frameworksOnly); } }
            public bool Verbose { get { return _verbose; } }
            public bool DoAnalyze { get { return _analyze; } }
            public Commands DoCommand { get { return _command; } }
            public string JobName { get { return _jobName; } }
            public bool DoLastSucessful { get { return _lastSuccessful; } }
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

        private static string[] s_benchmarksPath =
        {
            "JIT",
            "Performance",
            "CodeQuality"
        };

        private static string[] s_benchmarkDirectories =
        {
            "BenchI",
            "BenchF",
            "BenchmarksGame"
        };

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
                        // List command: list loaded config.json in config object.
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

        private static string FindJitLibrary(string path)
        {
            string clrjitPath = Path.Combine(path, "clrjit.dll");
            if (File.Exists(clrjitPath))
            {
                return clrjitPath;
            }

            clrjitPath = Path.Combine(path, "libclrjit.so");
            if (File.Exists(clrjitPath))
            {
                return clrjitPath;
            }

            return null;
        }

        private static string FindCrossgenExecutable(string path)
        {
            string crossgenPath = Path.Combine(path, "crossgen.exe");
            if (File.Exists(crossgenPath))
            {
                return crossgenPath;
            }

            crossgenPath = Path.Combine(path, "crossgen");
            if (File.Exists(crossgenPath))
            {
                return crossgenPath;
            }

            return null;
        }

        public static int InstallCommand(Config config)
        {   
            var asmDiffPath = Path.Combine(config.JitUtilsRoot, "config.json");
            string configJson = File.ReadAllText(asmDiffPath);
            string tag = String.Format("{0}-{1}", config.JobName, config.Number);
            string toolPath = Path.Combine(config.JitUtilsRoot, "tools", tag);
            var jObj = JObject.Parse(configJson);
            var tools = (JArray)jObj["asmdiff"]["tools"];
            int ret = 0;

            // Early out if the tool is already installed.
            if (tools.Where(x => (string)x["tag"] == tag).Any())
            {
                Console.Error.WriteLine("{0} is already installed in the config.json. Remove before re-install.", tag);
                return -1;
            }

            // Issue cijobs command to download bits            
            List<string> cijobsArgs = new List<string>();

            cijobsArgs.Add("copy");

            // Set up job name
            cijobsArgs.Add("--job");
            cijobsArgs.Add(config.JobName);
            
            if (config.BranchName != null)
            {
                cijobsArgs.Add("--branch");
                cijobsArgs.Add(config.BranchName);
                
            }
            
            if (config.DoLastSucessful)
            {
                cijobsArgs.Add("--last_successful");
            }
            else
            {
                // Set up job number
                cijobsArgs.Add("--number");
                cijobsArgs.Add(config.Number);
            }
            
            cijobsArgs.Add("--unzip");
            
            cijobsArgs.Add("--output");
            cijobsArgs.Add(toolPath);

            if (config.Verbose)
            {
                Console.WriteLine("ci command: {0} {1}", "cijobs", String.Join(" ", cijobsArgs));
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
            using (var file = File.CreateText(asmDiffPath))
            {
                using (JsonTextWriter writer = new JsonTextWriter(file))
                {
                    writer.Formatting = Formatting.Indented;
                    jObj.WriteTo(writer);
                }
            }
            return ret;
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

            private void StartDasmWorkSingle(List<string> args, string assemblyPath)
            {
                List<string> allArgs = new List<string>(args);
                allArgs.Add(assemblyPath);
                Task<int> task = Task<int>.Factory.StartNew(() => RunDasmTool(new DasmWorkItem(allArgs)));
                DasmWorkTasks.Add(task);

                if (m_config.Verbose)
                {
                    string command = String.Join(" ", allArgs);
                    Console.WriteLine("Started dasm command \"{0}\"", command);
                }

                if (m_config.Sequential)
                {
                    Task.WaitAll(task);
                }
            }

            // Returns a count of the number of failures.
            private void StartDasmWork(List<string> baseArgs, List<string> diffArgs, List<string> assemblyPaths)
            {
                foreach (var assemblyPath in assemblyPaths)
                {
                    if (baseArgs != null)
                    {
                        StartDasmWorkSingle(baseArgs, assemblyPath);
                    }
                    if (diffArgs != null)
                    {
                        StartDasmWorkSingle(diffArgs, assemblyPath);
                    }
                }
            }

            private List<string> ConstructArgs(List<string> commandArgs, List<string> assemblyPaths, string tag, string clrPath)
            {
                List<string> dasmArgs = commandArgs.ToList();
                dasmArgs.Add("--tag");
                dasmArgs.Add(tag);
                dasmArgs.Add("--crossgen");
                if (m_config.HasCrossgenExe)
                {
                    dasmArgs.Add(m_config.CrossgenExe);

                    var jitPath = FindJitLibrary(clrPath);
                    if (jitPath == null)
                    {
                        Console.Error.WriteLine("clrjit not found in " + clrPath);
                        return null;
                    }

                    dasmArgs.Add("--jit");
                    dasmArgs.Add(jitPath);
                }
                else
                {
                    var crossgenPath = FindCrossgenExecutable(clrPath);
                    if (crossgenPath == null)
                    {
                        Console.Error.WriteLine("crossgen not found in " + clrPath);
                        return  null;
                    }

                    dasmArgs.Add(crossgenPath);
                }

                return dasmArgs;
            }

            // Returns:
            // 0 on success,
            // Otherwise, a count of the number of failures generating asm, as reported by the asm tool.
            private int RunDasmTool(List<string> commandArgs, List<string> assemblyPaths)
            {
                List<string> baseArgs = null, diffArgs = null;

                if (m_config.HasBasePath)
                {
                    baseArgs = ConstructArgs(commandArgs, assemblyPaths, "base", m_config.BasePath);
                }

                if (m_config.HasDiffPath)
                {
                    diffArgs = ConstructArgs(commandArgs, assemblyPaths, "diff", m_config.DiffPath);
                }

                StartDasmWork(baseArgs, diffArgs, assemblyPaths);

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
                string diffString;

                if (config.BenchmarksOnly)
                {
                    diffString = "benchstones, benchmarks game";
                }
                else
                {
                    diffString = "System.Private.CoreLib.dll";

                    if (config.DoFrameworks)
                    {
                        diffString += ", framework assemblies";
                    }

                    if (config.DoTestTree)
                    {
                        diffString += ", " + config.TestRoot;
                    }
                }

                Console.WriteLine("Beginning diff of {0}!", diffString);

                // Add each framework assembly to commandArgs

                // Create subjob that runs jit-dasm, which should be in path, with the 
                // relevent coreclr assemblies/paths.

                string frameworkArgs = String.Join(" ", s_frameworkAssemblies);
                string testArgs = String.Join(" ", s_testDirectories);

                List<string> commandArgs = new List<string>();

                // Set up CoreRoot
                commandArgs.Add("--platform");
                commandArgs.Add(config.CoreRoot);

                commandArgs.Add("--output");
                commandArgs.Add(config.OutputPath);

                List<string> assemblyArgs = new List<string>();

                if (config.DoTestTree)
                {
                    commandArgs.Add("--recursive");
                }

                if (config.Verbose)
                {
                    commandArgs.Add("--verbose");
                }

                if (config.CoreLibOnly)
                {
                    string coreRoot = config.CoreRoot;
                    string fullPathAssembly = Path.Combine(coreRoot, "System.Private.CoreLib.dll");
                    assemblyArgs.Add(fullPathAssembly);
                }
                else
                {
                    if (config.DoFrameworks)
                    {
                        // Set up full framework paths
                        foreach (var assembly in s_frameworkAssemblies)
                        {
                            string coreRoot = config.CoreRoot;
                            string fullPathAssembly = Path.Combine(coreRoot, assembly);

                            if (!File.Exists(fullPathAssembly))
                            {
                                Console.Error.WriteLine("can't find framework assembly {0}", fullPathAssembly);
                                continue;
                            }

                            assemblyArgs.Add(fullPathAssembly);
                        }
                    }

                    if (config.TestRoot != null)
                    {
                        string basepath = config.TestRoot;
                        if (config.BenchmarksOnly)
                        {
                            foreach (var dir in s_benchmarksPath)
                            {
                                basepath = Path.Combine(basepath, dir);
                            }
                        }
                        foreach (var dir in config.BenchmarksOnly ? s_benchmarkDirectories : s_testDirectories)
                        {
                            string fullPathDir = Path.Combine(basepath, dir);

                            if (!Directory.Exists(fullPathDir))
                            {
                                Console.Error.WriteLine("can't find test directory {0}", fullPathDir);
                                continue;
                            }

                            assemblyArgs.Add(fullPathDir);
                        }
                    }
                }

                DiffTool diffTool = new DiffTool(config);
                int dasmFailures = diffTool.RunDasmTool(commandArgs, assemblyArgs);

                // Analyze completed run.

                if (config.DoAnalyze)
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
        }
    }
}

