// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

///////////////////////////////////////////////////////////////////////////////
//
//  jitdasm - The managed code gen diff tool scripts the generation of
//  diffable assembly code output from the crossgen ahead of time compilation
//  tool.  This enables quickly generating A/B comparisons of .Net codegen
//  tools to validate ongoing development.
//
//  Scenario 1: Pass A and B compilers to jitdasm.  Using the --base and --diff
//  arguments pass two seperate compilers and a passed set of assemblies.  This 
//  is the most common scenario.
//
//  Scenario 2: Iterativly call jitdasm with a series of compilers tagging
//  each run.  Allows for producing a broader set of results like 'base',
//  'experiment1', 'experiment2', and 'experiment3'.  This tagging is only
//  allowed in the case where a single compiler is passed to avoid any
//  confusion in the generated results.
//

using System;
using System.Diagnostics;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Linq;
using System.Text;

namespace ManagedCodeGen
{
    public enum CodeGenerator
    {
        Crossgen,
        Crossgen2
    }

    // Define options to be parsed 
    public class Config
    {
        private ArgumentSyntax _syntaxResult;
        private string _altjit = null;
        private string _crossgenExe = null;
        private string _jitPath = null;
        private string _rootPath = null;
        private string _fileName = null;
        private IReadOnlyList<string> _assemblyList = Array.Empty<string>();
        private bool _wait = false;
        private bool _recursive = false;
        private IReadOnlyList<string> _methods = Array.Empty<string>();
        private IReadOnlyList<string> _platformPaths = Array.Empty<string>();
        private bool _dumpGCInfo = false;
        private bool _dumpDebugInfo = false;
        private bool _verbose = false;
        private bool _noDiffable = false;
        private CodeGenerator _codeGenerator;

        public Config(string[] args)
        {
            _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
            {
                syntax.DefineOption("altjit", ref _altjit, "If set, the name of the altjit to use (e.g., clrjit_win_arm64_x64.dll).");
                syntax.DefineOption("c|crossgen", ref _crossgenExe, "The crossgen or crossgen2 compiler exe.");
                syntax.DefineOption("j|jit", ref _jitPath, "The full path to the jit library.");
                syntax.DefineOption("o|output", ref _rootPath, "The output path.");
                syntax.DefineOption("f|file", ref _fileName, "Name of file to take list of assemblies from. Both a file and assembly list can be used.");
                syntax.DefineOption("gcinfo", ref _dumpGCInfo, "Add GC info to the disasm output.");
                syntax.DefineOption("debuginfo", ref _dumpDebugInfo, "Add Debug info to the disasm output.");
                syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");
                syntax.DefineOption("nodiffable", ref _noDiffable, "Generate non-diffable asm (pointer values will be left in output).");
                var waitArg = syntax.DefineOption("w|wait", ref _wait, "Wait for debugger to attach.");
                waitArg.IsHidden = true;

                syntax.DefineOption("r|recursive", ref _recursive, "Scan directories recursively.");
                syntax.DefineOptionList("p|platform", ref _platformPaths, "Path to platform assemblies");
                var methodsArg = syntax.DefineOptionList("m|methods", ref _methods,
                    "List of methods to disasm.");
                methodsArg.IsHidden = true;

                // Warning!! - Parameters must occur after options to preserve parsing semantics.

                syntax.DefineParameterList("assembly", ref _assemblyList, "The list of assemblies or directories to scan for assemblies.");
            });

            // Run validation code on parsed input to ensure we have a sensible scenario.

            Validate();
        }

        // Validate arguments
        //
        // Pass a single tool as --crossgen. Optionally specify a jit for crossgen to use.
        //
        private void Validate()
        {
            if (_crossgenExe == null)
            {
                _syntaxResult.ReportError("Specify --crossgen.");
            }

            if ((_fileName == null) && (_assemblyList.Count == 0))
            {
                _syntaxResult.ReportError("No input: Specify --file <arg> or list input assemblies.");
            }

            // Check that we can find the crossgenExe
            if (_crossgenExe != null)
            {
                if (!File.Exists(_crossgenExe))
                {
                    _syntaxResult.ReportError("Can't find --crossgen tool.");
                }
                else
                {
                    // Set to full path for command resolution logic.
                    string fullCrossgenPath = Path.GetFullPath(_crossgenExe);
                    _crossgenExe = fullCrossgenPath;

                    switch (Path.GetFileNameWithoutExtension(fullCrossgenPath).ToLower())
                    {
                        case "crossgen":
                            _codeGenerator = CodeGenerator.Crossgen;
                            break;
                        case "crossgen2":
                            _codeGenerator = CodeGenerator.Crossgen2;
                            break;
                        default:
                            _syntaxResult.ReportError("--crossgen tool should be crossgen or crossgen2.");
                            break;
                    }
                }
            }

            // Check that we can find the jit library.
            if (_jitPath != null)
            {
                if (!File.Exists(_jitPath))
                {
                    _syntaxResult.ReportError("Can't find --jit library.");
                }
                else
                {
                    // Set to full path for command resolution logic.
                    string fullJitPath = Path.GetFullPath(_jitPath);
                    _jitPath = fullJitPath;
                }
            }

            if (_fileName != null)
            {
                if (!File.Exists(_fileName))
                {
                    var message = String.Format("Error reading input file {0}, file not found.", _fileName);
                    _syntaxResult.ReportError(message);
                }
            }
        }

        public bool HasUserAssemblies { get { return AssemblyList.Count > 0; } }
        public bool WaitForDebugger { get { return _wait; } }
        public bool UseJitPath { get { return (_jitPath != null); } }
        public bool Recursive { get { return _recursive; } }
        public bool UseFileName { get { return (_fileName != null); } }
        public bool DumpGCInfo { get { return _dumpGCInfo; } }
        public bool DumpDebugInfo { get { return _dumpDebugInfo; } }
        public bool DoVerboseOutput { get { return _verbose; } }
        public bool NoDiffable { get { return _noDiffable; } }
        public string CrossgenExecutable { get { return _crossgenExe; } }
        public string JitPath { get { return _jitPath; } }
        public string AltJit { get { return _altjit; } }
        public string RootPath { get { return _rootPath; } }
        public IReadOnlyList<string> PlatformPaths { get { return _platformPaths; } }
        public string FileName { get { return _fileName; } }
        public IReadOnlyList<string> AssemblyList { get { return _assemblyList; } }
        public CodeGenerator CodeGenerator { get { return _codeGenerator; } }
    }

    public class AssemblyInfo
    {
        public string Name { get; set; }
        // Contains path to assembly.
        public string Path { get; set; }
        // Contains relative path within output directory for given assembly.
        // This allows for different output directories per tool.
        public string OutputPath { get; set; }
    }

    public class jitdasm
    {
        public static int Main(string[] args)
        {
            // Error count will be returned.  Start at 0 - this will be incremented
            // based on the error counts derived from the DisasmEngine executions.
            int errorCount = 0;

            // Parse and store comand line options.
            var config = new Config(args);

            // Stop to attach a debugger if desired.
            if (config.WaitForDebugger)
            {
                WaitForDebugger();
            }

            // Builds assemblyInfoList on jitdasm

            List<AssemblyInfo> assemblyWorkList = GenerateAssemblyWorklist(config);
            
            // The disasm engine encapsulates a particular set of diffs.  An engine is
            // produced with a given code generator and assembly list, which then produces
            // a set of disasm outputs.


            DisasmEngine crossgenDisasm;

            if (config.CodeGenerator == CodeGenerator.Crossgen)
            {
                crossgenDisasm = new CrossgenDisasmEngine(config.CrossgenExecutable, config, config.RootPath, assemblyWorkList);
            }
            else
            {
                crossgenDisasm = new Crossgen2DisasmEngine(config.CrossgenExecutable, config, config.RootPath, assemblyWorkList);
            }
            crossgenDisasm.GenerateAsm();
            
            if (crossgenDisasm.ErrorCount > 0)
            {
                Console.Error.WriteLine("{0} errors compiling set.", crossgenDisasm.ErrorCount);
                errorCount += crossgenDisasm.ErrorCount;
            }

            return errorCount;
        }

        private static void WaitForDebugger()
        {
            Console.WriteLine("Wait for a debugger to attach. Press ENTER to continue");
            Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");
            Console.ReadLine();
        }

        public static List<AssemblyInfo> GenerateAssemblyWorklist(Config config)
        {
            bool verbose = config.DoVerboseOutput;
            List<string> assemblyList = new List<string>();
            List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();

            if (config.UseFileName)
            {
                assemblyList = new List<string>();
                string inputFile = config.FileName;

                // Open file, read assemblies one per line, and add them to the assembly list.
                using (var inputStream = System.IO.File.Open(inputFile, FileMode.Open))
                {
                    using (var inputStreamReader = new StreamReader(inputStream))
                    {
                        string line;
                        while ((line = inputStreamReader.ReadLine()) != null)
                        {
                            // Each line is a path to an assembly.
                            if (!File.Exists(line))
                            {
                                Console.WriteLine("Can't find {0} skipping...", line);
                                continue;
                            }

                            assemblyList.Add(line);
                        }
                    }
                }
            }

            if (config.HasUserAssemblies)
            {
                // Append command line assemblies
                assemblyList.AddRange(config.AssemblyList);
            }

            // Process worklist and produce the info needed for the disasm engines.
            foreach (var path in assemblyList)
            {
                FileAttributes attr;

                if (File.Exists(path) || Directory.Exists(path))
                {
                    attr = File.GetAttributes(path);
                }
                else
                {
                    Console.WriteLine("Can't find assembly or directory at {0}", path);
                    continue;
                }

                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    if (verbose)
                    {
                        Console.WriteLine("Processing directory: {0}", path);
                    }

                    // For the directory case create a stack and recursively find any
                    // assemblies for compilation.
                    List<AssemblyInfo> directoryAssemblyInfoList = IdentifyAssemblies(path,
                        config);

                    // Add info generated at this directory
                    assemblyInfoList.AddRange(directoryAssemblyInfoList);
                }
                else
                {
                    // This is the file case.

                    AssemblyInfo info = new AssemblyInfo
                    {
                        Name = Path.GetFileName(path),
                        Path = Path.GetDirectoryName(path),
                        OutputPath = ""
                    };

                    assemblyInfoList.Add(info);
                }
            }

            return assemblyInfoList;
        }

        // Recursivly search for assemblies from a root path.
        private static List<AssemblyInfo> IdentifyAssemblies(string rootPath, Config config)
        {
            List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();
            string fullRootPath = Path.GetFullPath(rootPath);
            SearchOption searchOption = (config.Recursive) ?
                SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Get files that could be assemblies, but discard currently
            // ngen'd assemblies.
            var subFiles = Directory.EnumerateFiles(rootPath, "*", searchOption)
                .Where(s => (s.EndsWith(".exe") || s.EndsWith(".dll")) && !s.Contains(".ni."));

            foreach (var filePath in subFiles)
            {
                if (config.DoVerboseOutput)
                {
                    Console.WriteLine("Scanning: {0}", filePath);
                }

                // skip if not an assembly
                if (!Utility.IsAssembly(filePath))
                {
                    continue;
                }

                string fileName = Path.GetFileName(filePath);
                string directoryName = Path.GetDirectoryName(filePath);
                string fullDirectoryName = Path.GetFullPath(directoryName);
                string outputPath = fullDirectoryName.Substring(fullRootPath.Length).TrimStart(Path.DirectorySeparatorChar);

                AssemblyInfo info = new AssemblyInfo
                {
                    Name = fileName,
                    Path = directoryName,
                    OutputPath = outputPath
                };

                assemblyInfoList.Add(info);
            }

            return assemblyInfoList;
        }

        private abstract class DisasmEngine
        {
            protected string _executablePath;
            private Config _config;
            private string _rootPath = null;
            protected IReadOnlyList<string> _platformPaths;
            protected string _jitPath = null;
            private string _altjit = null;
            private List<AssemblyInfo> _assemblyInfoList;
            public bool doGCDump = false;
            public bool doDebugDump = false;
            public bool verbose = false;
            private int _errorCount = 0;
            protected Dictionary<string, string> _environmentVariables;

            public int ErrorCount { get { return _errorCount; } }

            public DisasmEngine(string executable, Config config, string outputPath,
                List<AssemblyInfo> assemblyInfoList)
            {
                _config = config;
                _executablePath = executable;
                _rootPath = outputPath;
                _platformPaths = config.PlatformPaths;
                _jitPath = config.JitPath;
                _altjit = config.AltJit;
                _assemblyInfoList = assemblyInfoList;
                _environmentVariables = new Dictionary<string, string>();

                this.doGCDump = config.DumpGCInfo;
                this.doDebugDump = config.DumpDebugInfo;
                this.verbose = config.DoVerboseOutput;
            }

            public void GenerateAsm()
            {
                // Build a command per assembly to generate the asm output.
                foreach (var assembly in _assemblyInfoList)
                {
                    if (_config.DoVerboseOutput)
                    {
                        Console.WriteLine("assembly name: " + assembly.Name);
                    }

                    string fullPathAssembly = Path.Combine(assembly.Path, assembly.Name);

                    if (!File.Exists(fullPathAssembly))
                    {
                        // Assembly not found.  Produce a warning and skip this input.
                        Console.WriteLine("Skipping. Assembly not found: {0}", fullPathAssembly);
                        continue;
                    }

                    List<string> commandArgs = new List<string>();

                    // Tell crossgen not to output a success/failure message at the end; that message
                    // includes a full path to the generated .ni.dll file, which makes all base/diff
                    // asm files appear to have diffs.
                    AddSilentOption(commandArgs);
                    // Also pass /nologo to avoid spurious diffs that sometimes appear when errors
                    // occur (sometimes the logo lines and error lines are interleaved).
                    AddNoLogoOption(commandArgs);

                    // Set jit path if it's defined.
                    if (_jitPath != null)
                    {
                        AddJitPathOption(commandArgs);
                    }

                    // Set platform assembly path if it's defined.
                    if (_platformPaths.Count > 0)
                    {
                        AddAssembliesPathsOption(commandArgs);
                    }

                    string extension = Path.GetExtension(fullPathAssembly);
                    string nativeOutput = Path.ChangeExtension(fullPathAssembly, "ni" + extension);
                    string mapOutput = Path.ChangeExtension(fullPathAssembly, "ni.map");

                    if (_rootPath != null)
                    {
                        string assemblyNativeFileName = Path.ChangeExtension(assembly.Name, "ni" + extension);
                        string assemblyMapFileName = Path.ChangeExtension(assembly.Name, "ni.map");
                        nativeOutput = Path.Combine(_rootPath, assembly.OutputPath, assemblyNativeFileName);
                        mapOutput = Path.Combine(_rootPath, assembly.OutputPath, assemblyMapFileName);

                        Utility.EnsureParentDirectoryExists(nativeOutput);

                        AddOutputPathOption(commandArgs, nativeOutput);
                    }

                    AddOptimizationOption(commandArgs);

                    commandArgs.Add(fullPathAssembly);

                    // Pick up ambient COMPlus settings.
                    foreach (string envVar in Environment.GetEnvironmentVariables().Keys)
                    {
                        if (envVar.IndexOf("COMPlus_") == 0)
                        {
                            string value = Environment.GetEnvironmentVariable(envVar);
                            AddEnvironmentVariable(envVar, value);
                        }
                    }

                    // Set up environment do disasm.
                    AddEnvironmentVariable("COMPlus_NgenDisasm", "*");
                    AddEnvironmentVariable("COMPlus_NgenUnwindDump", "*");
                    AddEnvironmentVariable("COMPlus_NgenEHDump", "*");
                    if (!this._config.NoDiffable)
                    {
                        AddEnvironmentVariable("COMPlus_JitDiffableDasm", "1");
                    }
                    AddEnvironmentVariable("COMPlus_JitEnableNoWayAssert", "1");    // Force noway_assert to generate assert (not fall back to MinOpts).
                    AddEnvironmentVariable("COMPlus_JitNoForceFallback", "1");      // Don't stress noway fallback path.
                    AddEnvironmentVariable("COMPlus_JitRequired", "1");             // Force NO_WAY to generate assert. Also generates assert for BADCODE/BADCODE3.

                    if (this.doGCDump)
                    {
                        AddEnvironmentVariable("COMPlus_NgenGCDump", "*");
                    }

                    if (this.doDebugDump)
                    {
                        AddEnvironmentVariable("COMPlus_NgenDebugDump", "*");
                    }

                    if (this._altjit != null)
                    {
                        AddEnvironmentVariable("COMPlus_AltJit", "*");
                        AddEnvironmentVariable("COMPlus_AltJitNgen", "*");
                        AddEnvironmentVariable("COMPlus_AltJitName", _altjit);
                    }

                    string dasmPath = null;
                    if (_rootPath != null)
                    {
                        // Generate path to the output file
                        var assemblyFileName = Path.ChangeExtension(assembly.Name, ".dasm");
                        dasmPath = Path.Combine(_rootPath, assembly.OutputPath, assemblyFileName);

                        Utility.EnsureParentDirectoryExists(dasmPath);

                        AddEnvironmentVariable("COMPlus_JitStdOutFile", dasmPath);
                    }

                    if (this.verbose)
                    {
                        Console.WriteLine("Running: {0} {1}", _executablePath, String.Join(" ", commandArgs));
                    }

                    ProcessResult result;

                    if (_rootPath != null)
                    {
                        var logPath = Path.ChangeExtension(dasmPath, ".log");
                        result = ExecuteProcess(commandArgs, true);

                        // Write stdout/stderr to log file.
                        StringBuilder output = new StringBuilder();
                        if (!string.IsNullOrEmpty(result.StdOut))
                        {
                            output.Append(result.StdOut);
                        }
                        if (!string.IsNullOrEmpty(result.StdErr) && (result.StdOut != result.StdErr))
                        {
                            output.Append(result.StdErr);
                        }
                        if (output.Length > 0)
                        {
                            File.WriteAllText(logPath, output.ToString());
                        }

                        bool hasOutput = true;

                        if (result.ExitCode != 0)
                        {
                            _errorCount++;

                            if (result.ExitCode == -2146234344)
                            {
                                Console.Error.WriteLine("{0} is not a managed assembly", fullPathAssembly);

                                // Discard output if the assembly is not managed
                                File.Delete(dasmPath);
                                File.Delete(logPath);

                                hasOutput = false;
                            }
                            else
                            {
                                Console.Error.WriteLine("Error running {0} on {1}", _executablePath, fullPathAssembly);
                            }
                        }

                        if (hasOutput && !File.Exists(dasmPath))
                        {
                            // Looks like the JIT does not support COMPlus_JitStdOutFile so
                            // the assembly output must be in the log file.
                            File.Move(logPath, dasmPath);
                        }
                    }
                    else
                    {
                        // By default forward to output to stdout/stderr.
                        result = ExecuteProcess(commandArgs);

                        if (result.ExitCode != 0)
                        {
                            _errorCount++;
                        }
                    }

                    // Remove the generated .ni.exe/dll/map file; typical use case is generating dasm for
                    // assemblies in the test tree, and leaving the .ni.dll around would mean that
                    // subsequent test passes would re-use that code instead of jitting with the
                    // compiler that's supposed to be tested.
                    if (File.Exists(nativeOutput))
                    {
                        File.Delete(nativeOutput);
                    }
                    if (File.Exists(mapOutput))
                    {
                        File.Delete(mapOutput);
                    }
                }
            }

            // Add environment variables to the environment of the command we are going to execute, and
            // display them to the user in verbose mode.
            void AddEnvironmentVariable(string varName, string varValue)
            {
                _environmentVariables[varName] = varValue;
                if (this.verbose)
                {
                    Console.WriteLine("set {0}={1}", varName, varValue);
                }
            }

            abstract protected void AddSilentOption(List<string> commandArgs);

            abstract protected void AddNoLogoOption(List<string> commandArgs);

            abstract protected void AddJitPathOption(List<string> commandArgs);

            abstract protected void AddAssembliesPathsOption(List<string> commandArgs);

            abstract protected void AddOutputPathOption(List<string> commandArgs, string outputPath);

            abstract protected void AddOptimizationOption(List<string> commandArgs);

            abstract protected ProcessResult ExecuteProcess(List<string> commandArgs, bool capture = false);
        }

        sealed private class CrossgenDisasmEngine : DisasmEngine
        {
            public CrossgenDisasmEngine(string executable, Config config, string outputPath,
                List<AssemblyInfo> assemblyInfoList) : base(executable, config, outputPath, assemblyInfoList)
            {
            }

            override protected void AddSilentOption(List<string> commandArgs)
            {
                commandArgs.Add("/silent");
            }

            override protected void AddNoLogoOption(List<string> commandArgs)
            {
                commandArgs.Add("/nologo");
            }

            override protected void AddJitPathOption(List<string> commandArgs)
            {
                commandArgs.Add("/JitPath");
                commandArgs.Add(_jitPath);
            }

            override protected void AddAssembliesPathsOption(List<string> commandArgs)
            {
                commandArgs.Add("/Platform_Assemblies_Paths");
                commandArgs.Add(String.Join(" ", _platformPaths));
            }

            override protected void AddOutputPathOption(List<string> commandArgs, string outputPath)
            {
                commandArgs.Add("/out");
                commandArgs.Add(outputPath);
            }

            override protected void AddOptimizationOption(List<string> commandArgs)
            {
            }

            override protected ProcessResult ExecuteProcess(List<string> commandArgs, bool capture)
            {
                return Utility.ExecuteProcess(_executablePath, commandArgs, capture, environmentVariables: _environmentVariables);
            }
        }

        sealed private class Crossgen2DisasmEngine : DisasmEngine
        {
            public Crossgen2DisasmEngine(string executable, Config config, string outputPath,
                List<AssemblyInfo> assemblyInfoList) : base(executable, config, outputPath, assemblyInfoList)
            {
            }

            override protected void AddSilentOption(List<string> commandArgs)
            {
            }

            override protected void AddNoLogoOption(List<string> commandArgs)
            {
            }

            override protected void AddJitPathOption(List<string> commandArgs)
            {
                commandArgs.Add("--jitpath");
                commandArgs.Add(_jitPath);
            }

            override protected void AddAssembliesPathsOption(List<string> commandArgs)
            {
                commandArgs.Add("--reference");
                commandArgs.Add(String.Join(" ", _platformPaths.Select(str => Path.Combine(str, "*.dll"))));
            }

            override protected void AddOutputPathOption(List<string> commandArgs, string outputPath)
            {
                commandArgs.Add("--outputfilepath");
                commandArgs.Add(outputPath);
            }

            override protected void AddOptimizationOption(List<string> commandArgs)
            {
                commandArgs.Add("--optimize");
            }

            override protected ProcessResult ExecuteProcess(List<string> commandArgs, bool capture)
            {
                foreach (var envVar in _environmentVariables)
                {
                    commandArgs.Add("--codegenopt");
                    string complusPrefix = "COMPlus_";
                    commandArgs.Add(String.Format("{0}={1}", envVar.Key.Substring(complusPrefix.Length), envVar.Value));
                }
                return Utility.ExecuteProcess(_executablePath, commandArgs, capture);
            }
        }
    }
}
