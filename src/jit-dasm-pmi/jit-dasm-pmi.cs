// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

///////////////////////////////////////////////////////////////////////////////
//
//  jit-dasm-pmi - The managed code gen diff tool scripts the generation of
//  diffable assembly code output from the the runtime. This enables quickly
//  generating A/B comparisons of .Net codegen tools to validate ongoing
//  development.
//
//  The related jit-dasm tool is complementary, and does something similar for
//  prejitted code.
//

using System;
using System.Diagnostics;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;

namespace ManagedCodeGen
{
    // Define options to be parsed 
    public class Config
    {
        private ArgumentSyntax _syntaxResult;
        private string _altjit = null;
        private string _corerunExe = null;
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
        private bool _noCopyJit = false;
        private bool _verbose = false;
        private bool _tiering = false;
        private bool _cctors = false;

        public Config(string[] args)
        {
            _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
            {
                syntax.DefineOption("altjit", ref _altjit, "If set, the name of the altjit to use (e.g., protononjit.dll).");
                syntax.DefineOption("c|corerun", ref _corerunExe, "The corerun compiler exe.");
                syntax.DefineOption("j|jit", ref _jitPath, "The full path to the jit library.");
                syntax.DefineOption("o|output", ref _rootPath, "The output path.");
                syntax.DefineOption("f|file", ref _fileName, "Name of file to take list of assemblies from. Both a file and assembly list can be used.");
                syntax.DefineOption("gcinfo", ref _dumpGCInfo, "Add GC info to the disasm output.");
                syntax.DefineOption("debuginfo", ref _dumpDebugInfo, "Add Debug info to the disasm output.");
                syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");
                syntax.DefineOption("t|tiering", ref _tiering, "Enable tiered jitting");
                syntax.DefineOption("cctors", ref _cctors, "Jit and run cctors before jitting other methods");
                syntax.DefineOption("r|recursive", ref _recursive, "Scan directories recursively.");
                syntax.DefineOptionList("p|platform", ref _platformPaths, "Path to platform assemblies");

                var waitArg = syntax.DefineOption("w|wait", ref _wait, "Wait for debugger to attach.");
                waitArg.IsHidden = true;

                var methodsArg = syntax.DefineOptionList("m|methods", ref _methods, "List of methods to disasm.");
                methodsArg.IsHidden = true;

                var noCopyArg = syntax.DefineOption("nocopy", ref _noCopyJit, "Correct jit has already been copied into the corerun directory");
                noCopyArg.IsHidden = true;

                // Warning!! - Parameters must occur after options to preserve parsing semantics.

                syntax.DefineParameterList("assembly", ref _assemblyList, "The list of assemblies or directories to scan for assemblies.");
            });

            // Run validation code on parsed input to ensure we have a sensible scenario.

            Validate();
        }

        // Validate arguments
        //
        // Pass a single tool as --corerun. Optionally specify a jit for corerun to use.
        //
        private void Validate()
        {
            if (_corerunExe == null)
            {
                _syntaxResult.ReportError("Specify --corerun.");
            }

            if ((_fileName == null) && (_assemblyList.Count == 0))
            {
                _syntaxResult.ReportError("No input: Specify --file <arg> or list input assemblies.");
            }

            // Check that we can find the corerunExe
            if (_corerunExe != null)
            {
                if (!File.Exists(_corerunExe))
                {
                    _syntaxResult.ReportError("Can't find --corerun tool.");
                }
                else
                {
                    // Set to full path for command resolution logic.
                    string fullCorerunPath = Path.GetFullPath(_corerunExe);
                    _corerunExe = fullCorerunPath;
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
        public bool CopyJit { get { return !_noCopyJit; } }
        public string CorerunExecutable { get { return _corerunExe; } }
        public string JitPath { get { return _jitPath; } }
        public string AltJit { get { return _altjit; } }
        public string RootPath { get { return _rootPath; } }
        public IReadOnlyList<string> PlatformPaths { get { return _platformPaths; } }
        public string FileName { get { return _fileName; } }
        public IReadOnlyList<string> AssemblyList { get { return _assemblyList; } }
        public bool Tiering => _tiering;
        public bool Cctors => _cctors;
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

    public class jitdasmpmi
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

            DisasmEnginePmi corerunDisasm = new DisasmEnginePmi(config.CorerunExecutable, config, config.RootPath, assemblyWorkList);
            corerunDisasm.GenerateAsm();

            if (corerunDisasm.ErrorCount > 0)
            {
                Console.Error.WriteLine("{0} errors compiling set.", corerunDisasm.ErrorCount);
                errorCount += corerunDisasm.ErrorCount;
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

        private class DisasmEnginePmi
        {
            private string _executablePath;
            private Config _config;
            private string _rootPath = null;
            private IReadOnlyList<string> _platformPaths;
            private string _jitPath = null;
            private string _altjit = null;
            private List<AssemblyInfo> _assemblyInfoList;
            public bool doGCDump = false;
            public bool doDebugDump = false;
            public bool verbose = false;
            private int _errorCount = 0;

            public int ErrorCount { get { return _errorCount; } }

            private string GetPmiJitLibraryName(string suffix = "")
            {
                string jitName = Path.GetFileNameWithoutExtension(_jitPath);
                string pmiJitName = jitName + suffix + Path.GetExtension(_jitPath);
                return pmiJitName;
            }

            public DisasmEnginePmi(string executable, Config config, string outputPath,
                List<AssemblyInfo> assemblyInfoList)
            {
                _config = config;
                _executablePath = executable;
                _rootPath = outputPath;
                _platformPaths = config.PlatformPaths;
                _jitPath = config.JitPath;
                _altjit = config.AltJit;
                _assemblyInfoList = assemblyInfoList;

                this.doGCDump = config.DumpGCInfo;
                this.doDebugDump = config.DumpDebugInfo;
                this.verbose = config.DoVerboseOutput;
            }

            class ScriptResolverPolicyWrapper : ICommandResolverPolicy
            {
                public CompositeCommandResolver CreateCommandResolver() => ScriptCommandResolverPolicy.Create();
            }

            public void GenerateAsm()
            {
                string testOverlayDir = Path.GetDirectoryName(_config.CorerunExecutable);
                string jitDir = Path.GetDirectoryName(_jitPath);
                string realJitPath = Path.Combine(testOverlayDir, GetPmiJitLibraryName());
                string tempJitPath = Path.Combine(testOverlayDir, GetPmiJitLibraryName("-backup"));

                try
                {
                    if (_config.CopyJit)
                    {
                        if (this.verbose)
                        {
                            Console.WriteLine($"Copying default jit: {realJitPath} ==> {tempJitPath}");
                        }
                        File.Copy(realJitPath, tempJitPath, true);
                        if (this.verbose)
                        {
                            Console.WriteLine($"Copying in the test jit: {_jitPath} ==> {realJitPath}");
                        }
                        // May need chmod +x for non-windows ??
                        File.Copy(_jitPath, realJitPath, true);
                    }

                    GenerateAsmInternal();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"JIT DASM PMI failed: {e.Message}");
                    _errorCount++;
                }
                finally
                {
                    if (_config.CopyJit)
                    {
                        if (this.verbose)
                        {
                            Console.WriteLine($"Restoring default jit: {tempJitPath} ==> {realJitPath}");
                        }
                        File.Copy(tempJitPath, realJitPath, true);
                    }
                }
            }
            void GenerateAsmInternal()
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

                    Assembly thisAssembly = typeof(DisasmEnginePmi).Assembly;
                    string binDir = Path.GetDirectoryName(thisAssembly.Location);
                    string command = "DRIVEALL-QUIET";
                    if (_config.Cctors)
                    {
                        command += "-CCTORS";
                    }
                    List<string> commandArgs = new List<string>() { Path.Combine(binDir, "pmi.dll"), command, fullPathAssembly };
                    Command generateCmd = null;

                    // Add environment variables to the environment of the command we are going to execute, and
                    // display them to the user in verbose mode.
                    void AddEnvironmentVariable(string varName, string varValue)
                    {
                        generateCmd.EnvironmentVariable(varName, varValue);
                        if (this.verbose)
                        {
                            Console.WriteLine("Setting: {0}={1}", varName, varValue);
                        }
                    }
                    
                    try 
                    {
                        generateCmd = Command.Create(new ScriptResolverPolicyWrapper(), _executablePath, commandArgs);
                    }
                    catch (CommandUnknownException e)
                    {
                        Console.Error.WriteLine("\nError: {0} command not found!\n", e);
                        _errorCount++;
                        return;
                    }

                    // Pick up ambient COMPlus settings.
                    foreach (string envVar in Environment.GetEnvironmentVariables().Keys)
                    {
                        if (envVar.IndexOf("COMPlus_") == 0)
                        {
                            string value = Environment.GetEnvironmentVariable(envVar);
                            AddEnvironmentVariable(envVar, value);
                        }
                    }

                    // Set up environment do PMI based disasm.
                    AddEnvironmentVariable("COMPlus_JitDisasm", "*");
                    AddEnvironmentVariable("COMPlus_JitDisasmAssemblies", Path.GetFileNameWithoutExtension(assembly.Name));
                    AddEnvironmentVariable("COMPlus_JitUnwindDump", "*");
                    AddEnvironmentVariable("COMPlus_JitEHDump", "*");
                    AddEnvironmentVariable("COMPlus_JitDiffableDasm", "1");
                    AddEnvironmentVariable("COMPlus_ReadyToRun", "0");
                    AddEnvironmentVariable("COMPlus_ZapDisable", "1");
                    AddEnvironmentVariable("COMPlus_JitEnableNoWayAssert", "1");    // Force noway_assert to generate assert (not fall back to MinOpts).
                    AddEnvironmentVariable("COMPlus_JitNoForceFallback", "1");      // Don't stress noway fallback path.
                    AddEnvironmentVariable("COMPlus_JitRequired", "1");             // Force NO_WAY to generate assert. Also generates assert for BADCODE/BADCODE3.
                    
                    // We likely don't want tiering enabled, but allow it, if user asks for it.
                    AddEnvironmentVariable("COMPlus_TieredCompilation", _config.Tiering ? "1" : "0");

                    if (this.doGCDump)
                    {
                        AddEnvironmentVariable("COMPlus_JitGCDump", "*");
                    }

                    if (this.doDebugDump)
                    {
                        AddEnvironmentVariable("COMPlus_JitDebugDump", "*");
                    }

                    if (this._altjit != null)
                    {
                        AddEnvironmentVariable("COMPlus_AltJit", "*");
                        AddEnvironmentVariable("COMPlus_AltJitName", _altjit);

                        // If this looks like a cross-targeting altjit, fix the SIMD size.
                        // Here's one place where rationalized jit naming would be nice.
                        if (_altjit.IndexOf("nonjit") > 0)
                        {
                            AddEnvironmentVariable("COMPlus_SIMD16ByteOnly", "1");
                        }
                    }

                    // Set up PMI path...
                    AddEnvironmentVariable("PMIPATH", Path.GetDirectoryName(assembly.Name));

                    if (this.verbose)
                    {
                        Console.WriteLine("Running: {0} {1}", _executablePath, String.Join(" ", commandArgs));
                    }

                    CommandResult result;

                    if (_rootPath != null)
                    {
                        // Generate path to the output file
                        var assemblyFileName = Path.ChangeExtension(assembly.Name, ".dasm");
                        var dasmPath = Path.Combine(_rootPath, assembly.OutputPath, assemblyFileName);
                        var logPath = Path.ChangeExtension(dasmPath, ".log");

                        PathUtility.EnsureParentDirectoryExists(dasmPath);

                        generateCmd.EnvironmentVariable("COMPlus_JitStdOutFile", dasmPath);

                        // Redirect stdout/stderr to log file and run command.
                        using (var outputStreamWriter = File.CreateText(logPath))
                        {
                            // Forward output and error to file.
                            generateCmd.ForwardStdOut(outputStreamWriter);
                            generateCmd.ForwardStdErr(outputStreamWriter);
                            result = generateCmd.Execute();
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
                        generateCmd.ForwardStdOut();
                        generateCmd.ForwardStdErr();
                        result = generateCmd.Execute();

                        if (result.ExitCode != 0)
                        {
                            _errorCount++;
                        }
                    }
                }
            }
        }
    }
}
