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

namespace ManagedCodeGen
{
    public class corediff
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
            private string _baseExe = null;
            private string _diffExe = null;
            private string _outputPath = null;
            private bool _analyze = false;
            private string _tag = null;
            private string _platformPath = null;
            private string _testPath = null;
            private bool _corlibOnly = false;
            private bool _frameworksOnly = false;
            private bool _verbose = false;
            private string _jobName;
            private string _number;
            private bool _lastSuccessful;
            private string _jitDasmRoot;
            private string _rid;
            private string _branchName;

            private JObject _jObj;
            private bool _asmdiffLoaded = false;

            public Config(string[] args)
            {
                // Get configuration values from JIT_DASM_ROOT/asmdiff.json

                LoadFileConfig();

                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    // Diff command section.
                    syntax.DefineCommand("diff", ref _command, Commands.Diff, "Run asm diff of base/diff.");
                    syntax.DefineOption("b|base", ref _baseExe, "The base compiler exe or tag.");
                    syntax.DefineOption("d|diff", ref _diffExe, "The diff compiler exe or tag.");
                    syntax.DefineOption("o|output", ref _outputPath, "The output path.");
                    syntax.DefineOption("a|analyze", ref _analyze, 
                        "Analyze resulting base, diff dasm directories.");
                    syntax.DefineOption("t|tag", ref _tag, 
                        "Name of root in output directory.  Allows for many sets of output.");
                    syntax.DefineOption("c|corlibonly", ref _corlibOnly, "Disasm *CorLib only");
                    syntax.DefineOption("f|frameworksonly", ref _frameworksOnly, "Disasm frameworks only");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output");
                    syntax.DefineOption("core_root", ref _platformPath, "Path to test CORE_ROOT.");
                    syntax.DefineOption("test_root", ref _testPath, "Path to test tree.");

                    // List command section.
                    syntax.DefineCommand("list", ref _command, Commands.List, 
                        "List defaults and available tools asmdiff.json.");
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
                Microsoft.DotNet.Cli.Utils.Command infoCmd = Microsoft.DotNet.Cli.Utils.Command.Create(
                    "dotnet", commandArgs);
                infoCmd.CaptureStdOut();
                infoCmd.CaptureStdErr();

                CommandResult result = infoCmd.Execute();

                if (result.ExitCode != 0)
                {
                    Console.WriteLine("dotnet --info returned non-zero");
                }

                var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    Regex pattern = new Regex(@"RID:\s*([A-Za-z0-9\.-]*)$");
                    Match match = pattern.Match(line);
                    if (match.Success)
                    {
                        _rid = match.Groups[1].Value;
                    }
                }
            }

            private void DeriveOutputTag()
            {
                if (_tag == null)
                {
                    int currentCount = 1;
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
                var tools = _jObj["tools"];

                foreach (var tool in tools)
                {
                    var tag = (string)tool["tag"];
                    var path = (string)tool["path"];

                    if (_baseExe == tag)
                    {
                        // passed base tag matches installed tool, reset path.
                        _baseExe = Path.Combine(path, "crossgen");
                    }

                    if (_diffExe == tag)
                    {
                        // passed diff tag matches installed tool, reset path.
                        _diffExe = Path.Combine(path, "crossgen");
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

                if ((_baseExe == null) && (_diffExe == null))
                {
                    _syntaxResult.ReportError("--base <path> or --diff <path> or both must be specified.");
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
                var token = _jObj["default"][tool];

                if (token != null)
                {
                    found = true;

                    string tag = _jObj["default"][tool].Value<string>();

                    // Extract set value for tool and see if we can find it
                    // in the installed tools.
                    var path = _jObj["tools"].Children()
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
                var token = _jObj["default"][name];

                if (token != null)
                {
                    found = true;

                    try
                    {
                        return token.Value<T>();
                    }
                    catch (System.FormatException e)
                    {
                        Console.WriteLine("Bad format for default {0}.  See asmdiff.json", name, e);
                    }
                }

                found = false;
                return default(T);
            }

            private void LoadFileConfig()
            {
                _jitDasmRoot = Environment.GetEnvironmentVariable("JIT_DASM_ROOT");

                if (_jitDasmRoot != null)
                {
                    string path = Path.Combine(_jitDasmRoot, "asmdiff.json");

                    if (File.Exists(path))
                    {
                        string configJson = File.ReadAllText(path);

                        _jObj = JObject.Parse(configJson);
                        
                        // Flag that the asmdiff.json is loaded.
                        _asmdiffLoaded = true;

                        // Check if there is any default config specified.
                        if (_jObj["default"] != null)
                        {
                            bool found;

                            // Find baseline tool if any.
                            string basePath = GetToolPath("base", out found);
                            if (found)
                            {
                                _baseExe = Path.Combine(basePath, "crossgen");
                            }

                            // Find diff tool if any
                            string diffPath = GetToolPath("diff", out found);
                            if (found)
                            {
                                _diffExe = Path.Combine(diffPath, "crossgen");
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
                        Console.WriteLine("Can't find asmdiff.json on {0}", _jitDasmRoot);
                    }
                }
                else
                {
                    Console.WriteLine("Environment variable JIT_DASM_ROOT not found.");
                }
            }

            void PrintTools()
            {
                var tools = _jObj["tools"];

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
                    Console.WriteLine("Error: asmdiff.json isn't loaded.");
                    return -1;
                }
                
                Console.WriteLine();
                
                // Check if there is any default config specified.
                if (_jObj["default"] != null)
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
            public string PlatformPath { get { return _platformPath; } }
            public string BaseExecutable { get { return _baseExe; } }
            public bool HasBaseExeutable { get { return (_baseExe != null); } }
            public string DiffExecutable { get { return _diffExe; } }
            public bool HasDiffExecutable { get { return (_diffExe != null); } }
            public string OutputPath { get { return _outputPath; } }
            public string Tag { get { return _tag; } }
            public bool HasTag { get { return (_tag != null); } }
            public bool CoreLibOnly { get { return _corlibOnly; } }
            public bool FrameworksOnly { get { return _frameworksOnly; } }
            public bool DoMSCorelib { get { return true; } }
            public bool DoFrameworks { get { return !_corlibOnly; } }
            public bool DoTestTree { get { return (!_corlibOnly && !_frameworksOnly); } }
            public bool Verbose { get { return _verbose; } }
            public bool DoAnalyze { get { return _analyze; } }
            public Commands DoCommand { get { return _command; } }
            public string JobName { get { return _jobName; } }
            public bool DoLastSucessful { get { return _lastSuccessful; } }
            public string JitDasmRoot { get { return _jitDasmRoot; } }
            public bool HasJitDasmRoot { get { return (_jitDasmRoot != null); } }
            public string RID { get { return _rid; } }
            public string Number { get { return _number; } }
            public string BranchName { get { return _branchName; } }
        }

        private static string[] s_testDirectories =
        {
            "Interop",
            "JIT"
        };

        private static string[] s_frameworkAssemblies =
        {
            "System.Private.CoreLib.dll",
            "mscorlib.dll",
            "System.Runtime.dll",
            "System.Runtime.Extensions.dll",
            "System.Runtime.Handles.dll",
            "System.Runtime.InteropServices.dll",
            "System.Runtime.InteropServices.PInvoke.dll",
            "System.Runtime.InteropServices.RuntimeInformation.dll",
            "System.Runtime.Numerics.dll",
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "System.Collections.dll",
            "System.Collections.Concurrent.dll",
            "System.Collections.Immutable.dll",
            "System.Collections.NonGeneric.dll",
            "System.Collections.Specialized.dll",
            "System.ComponentModel.dll",
            "System.Console.dll",
            "System.Dynamic.Runtime.dll",
            "System.IO.dll",
            "System.IO.Compression.dll",
            "System.Linq.dll",
            "System.Linq.Expressions.dll",
            "System.Linq.Parallel.dll",
            "System.Net.Http.dll",
            "System.Net.NameResolution.dll",
            "System.Net.Primitives.dll",
            "System.Net.Requests.dll",
            "System.Net.Security.dll",
            "System.Net.Sockets.dll",
            "System.Numerics.Vectors.dll",
            "System.Reflection.dll",
            "System.Reflection.DispatchProxy.dll",
            "System.Reflection.Emit.ILGeneration.dll",
            "System.Reflection.Emit.Lightweight.dll",
            "System.Reflection.Emit.dll",
            "System.Reflection.Extensions.dll",
            "System.Reflection.Metadata.dll",
            "System.Reflection.Primitives.dll",
            "System.Reflection.TypeExtensions.dll",
            "System.Text.Encoding.dll",
            "System.Text.Encoding.Extensions.dll",
            "System.Text.RegularExpressions.dll",
            "System.Xml.ReaderWriter.dll",
            "System.Xml.XDocument.dll",
            "System.Xml.XmlDocument.dll"
        };

        public static int Main(string[] args)
        {
            Config config = new Config(args);
            int ret = 0;
            
            switch (config.DoCommand)
            {
                case Commands.Diff:
                    {
                        ret = DiffCommand(config);
                    }
                    break;
                case Commands.List:
                    {
                        // List command: list loaded asmdiff.json in config object.
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

        public static int InstallCommand(Config config)
        {   
            var asmDiffPath = Path.Combine(config.JitDasmRoot, "asmdiff.json");
            string configJson = File.ReadAllText(asmDiffPath);
            string tag = String.Format("{0}-{1}", config.JobName, config.Number);
            string toolPath = Path.Combine(config.JitDasmRoot, "tools", tag);
            var jObj = JObject.Parse(configJson);
            var tools = (JArray)jObj["tools"];
            int ret = 0;

            // Early out if the tool is already installed.
            if (tools.Where(x => (string)x["tag"] == tag).Any())
            {
                Console.WriteLine("{0} is already installed in the asmdiff.json. Remove before re-install.", tag);
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
            
            Command cijobsCmd =  Command.Create("cijobs", cijobsArgs);

            // Wireup stdout/stderr so we can see outout.
            cijobsCmd.ForwardStdOut();
            cijobsCmd.ForwardStdErr();

            CommandResult result = cijobsCmd.Execute();

            if (result.ExitCode != 0)
            {
                Console.WriteLine("cijobs command returned with {0} failures", result.ExitCode);
                return result.ExitCode;
            }

            JObject newTool = new JObject();
            newTool.Add("tag", tag);
            // Derive underlying tool directory based on current RID.
            string[] platStrings = config.RID.Split('.');
            string platformPath = Path.Combine(toolPath, "Product");
            foreach (var dir in Directory.EnumerateDirectories(platformPath))
            {
                if (Path.GetFileName(dir).ToUpper().Contains(platStrings[0].ToUpper()))
                {
                    newTool.Add("path", Path.GetFullPath(dir));
                    tools.Last.AddAfterSelf(newTool);
                    break;
                }
            }

            // Overwrite current asmdiff.json with new data.
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
        
        public static int DiffCommand(Config config)
        {
            string diffString = "System.Private.CoreLib.dll";

            if (config.DoFrameworks)
            {
                diffString += ", framework assemblies";
            }

            if (config.DoTestTree)
            {
                diffString += ", " + config.TestRoot;
            }

            Console.WriteLine("Beginning diff of {0}!", diffString);

            // Add each framework assembly to commandArgs

            // Create subjob that runs mcgdiff, which should be in path, with the 
            // relevent coreclr assemblies/paths.

            string frameworkArgs = String.Join(" ", s_frameworkAssemblies);
            string testArgs = String.Join(" ", s_testDirectories);


            List<string> commandArgs = new List<string>();

            // Set up CoreRoot
            commandArgs.Add("--platform");
            commandArgs.Add(config.CoreRoot);

            commandArgs.Add("--output");
            commandArgs.Add(config.OutputPath);

            if (config.HasBaseExeutable)
            {
                commandArgs.Add("--base");
                commandArgs.Add(config.BaseExecutable);
            }

            if (config.HasDiffExecutable)
            {
                commandArgs.Add("--diff");
                commandArgs.Add(config.DiffExecutable);
            }

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
                commandArgs.Add(fullPathAssembly);
            }
            else
            {
                // Set up full framework paths
                foreach (var assembly in s_frameworkAssemblies)
                {
                    string coreRoot = config.CoreRoot;
                    string fullPathAssembly = Path.Combine(coreRoot, assembly);

                    if (!File.Exists(fullPathAssembly))
                    {
                        Console.WriteLine("can't find framework assembly {0}", fullPathAssembly);
                        continue;
                    }

                    commandArgs.Add(fullPathAssembly);
                }

                if (config.TestRoot != null)
                {
                    foreach (var dir in s_testDirectories)
                    {
                        string testRoot = config.TestRoot;
                        string fullPathDir = Path.Combine(testRoot, dir);

                        if (!Directory.Exists(fullPathDir))
                        {
                            Console.WriteLine("can't find test directory {0}", fullPathDir);
                            continue;
                        }

                        commandArgs.Add(fullPathDir);
                    }
                }
            }

            Console.WriteLine("Diff command: {0} {1}", s_asmTool, String.Join(" ", commandArgs));

            Command diffCmd = Command.Create(
                        s_asmTool,
                        commandArgs);

            // Wireup stdout/stderr so we can see outout.
            diffCmd.ForwardStdOut();
            diffCmd.ForwardStdErr();

            CommandResult result = diffCmd.Execute();

            if (result.ExitCode != 0)
            {
                Console.WriteLine("Dasm command returned with {0} failures", result.ExitCode);
                return result.ExitCode;
            }

            // Analyze completed run.

            if (config.DoAnalyze == true)
            {
                List<string> analysisArgs = new List<string>();

                analysisArgs.Add("--base");
                analysisArgs.Add(Path.Combine(config.OutputPath, "base"));
                analysisArgs.Add("--diff");
                analysisArgs.Add(Path.Combine(config.OutputPath, "diff"));
                analysisArgs.Add("--recursive");

                Console.WriteLine("Analyze command: {0} {1}",
                    s_analysisTool, String.Join(" ", analysisArgs));

                Command analyzeCmd = Command.Create(s_analysisTool, analysisArgs);

                // Wireup stdout/stderr so we can see outout.
                analyzeCmd.ForwardStdOut();
                analyzeCmd.ForwardStdErr();

                CommandResult analyzeResult = analyzeCmd.Execute();
            }

            return result.ExitCode;
        }
    }
}
