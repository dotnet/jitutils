// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

///////////////////////////////////////////////////////////////////////////////
//
//  mcgdiff - The managed code gen diff tool scripts the generation of
//  diffable assembly code output from the crossgen ahead of time compilation
//  tool.  This enables quickly generating A/B comparisons of .Net codegen
//  tools to validate ongoing development.
//
//  Scenario 1: Pass A and B compilers to mcgdiff.  Using the --base and --diff
//  arguments pass two seperate compilers and a passed set of assemblies.  This 
//  is the most common scenario.
//
//  Scenario 2: Iterativly call mcgdiff with a series of compilers tagging
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
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;

namespace ManagedCodeGen
{
    // Define options to be parsed 
    public class Config
    {
        private ArgumentSyntax _syntaxResult;
        private string _baseExe = null;
        private string _diffExe = null;
        private string _rootPath = null;
        private string _tag = null;
        private string _fileName = null;
        private IReadOnlyList<string> _assemblyList = Array.Empty<string>();
        private bool _wait = false;
        private bool _recursive = false;
        private IReadOnlyList<string> _methods = Array.Empty<string>();
        private IReadOnlyList<string> _platformPaths = Array.Empty<string>();
        private bool _dumpGCInfo = false;
        private bool _verbose = false;

        public Config(string[] args)
        {
            _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
            {
                syntax.DefineOption("b|base", ref _baseExe, "The base compiler exe.");
                syntax.DefineOption("d|diff", ref _diffExe, "The diff compiler exe.");
                syntax.DefineOption("o|output", ref _rootPath, "The output path.");
                syntax.DefineOption("t|tag", ref _tag, "Name of root in output directory.  Allows for many sets of output.");
                syntax.DefineOption("f|file", ref _fileName, "Name of file to take list of assemblies from. Both a file and assembly list can be used.");
                syntax.DefineOption("gcinfo", ref _dumpGCInfo, "Add GC info to the disasm output.");
                syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");
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

            validate();
        }

        // Validate supported scenarios
        // 
        //    Scenario 1:  --base and --diff
        //       Pass two tools in and generate a set of disassembly with each.  Result directories
        //       will be tagged with "base" and "diff" in the output dir.
        //
        //    Scenario 2:  --base or --diff with --tag
        //       Pass single tool as either --base or --diff and tag the result directory with a user
        //       supplied tag.
        //
        private void validate()
        {
            if ((_baseExe == null) && (_diffExe == null))
            {
                _syntaxResult.ReportError("Specify --base and/or --diff.");
            }

            if ((_tag != null) && (_diffExe != null) && (_baseExe != null))
            {
                _syntaxResult.ReportError("Multiple compilers with the same tag: Specify --diff OR --base seperatly with --tag (one compiler for one tag).");
            }

            if ((_fileName == null) && (_assemblyList.Count == 0))
            {
                _syntaxResult.ReportError("No input: Specify --file <arg> or list input assemblies.");
            }

            // Check that we can find the baseExe.
            if (_baseExe != null)
            {
                if (!File.Exists(_baseExe))
                {
                    _syntaxResult.ReportError("Can't find --base tool.");
                }
                else
                {
                    // Set to full path for the command resolution logic.
                    string fullBasePath = Path.GetFullPath(_baseExe);
                    _baseExe = fullBasePath;
                }
            }

            // Check that we can find the diffExe.
            if (_diffExe != null)
            {
                if (!File.Exists(_diffExe))
                {
                    _syntaxResult.ReportError("Can't find --diff tool.");
                }
                else
                {
                    // Set to full path for command resolution logic.
                    string fullDiffPath = Path.GetFullPath(_diffExe);
                    _diffExe = fullDiffPath;
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
        public bool DoFileOutput { get { return (this.RootPath != null); } }
        public bool WaitForDebugger { get { return _wait; } }
        public bool GenerateBaseline { get { return (_baseExe != null); } }
        public bool GenerateDiff { get { return (_diffExe != null); } }
        public bool HasTag { get { return (_tag != null); } }
        public bool Recursive { get { return _recursive; } }
        public bool UseFileName { get { return (_fileName != null); } }
        public bool DumpGCInfo { get { return _dumpGCInfo; } }
        public bool DoVerboseOutput { get { return _verbose; } }
        public string BaseExecutable { get { return _baseExe; } }
        public string DiffExecutable { get { return _diffExe; } }
        public string RootPath { get { return _rootPath; } }
        public IReadOnlyList<string> PlatformPaths { get { return _platformPaths; } }
        public string Tag { get { return _tag; } }
        public string FileName { get { return _fileName; } }
        public IReadOnlyList<string> AssemblyList { get { return _assemblyList; } }
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

    public class mcgdiff
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

            // Builds assemblyInfoList on mcgdiff
            List<AssemblyInfo> assemblyWorkList = GenerateAssemblyWorklist(config);

            // The disasm engine encapsulates a particular set of diffs.  An engine is
            // produced with a given code generator and assembly list, which then produces
            // a set of disasm outputs

            if (config.GenerateBaseline)
            {
                string taggedPath = null;
                if (config.DoFileOutput)
                {
                    string tag = "base";
                    if (config.HasTag)
                    {
                        tag = config.Tag;
                    }

                    taggedPath = Path.Combine(config.RootPath, tag);
                }

                DisasmEngine baseDisasm = new DisasmEngine(config.BaseExecutable, config, taggedPath, assemblyWorkList);
                baseDisasm.GenerateAsm();

                if (baseDisasm.ErrorCount > 0)
                {
                    Console.WriteLine("{0} errors compiling base set.", baseDisasm.ErrorCount);
                    errorCount += baseDisasm.ErrorCount;
                }
            }

            if (config.GenerateDiff)
            {
                string taggedPath = null;
                if (config.DoFileOutput)
                {
                    string tag = "diff";
                    if (config.HasTag)
                    {
                        tag = config.Tag;
                    }

                    taggedPath = Path.Combine(config.RootPath, tag);
                }

                DisasmEngine diffDisasm = new DisasmEngine(config.DiffExecutable, config, taggedPath, assemblyWorkList);
                diffDisasm.GenerateAsm();

                if (diffDisasm.ErrorCount > 0)
                {
                    Console.WriteLine("{0} errors compiling diff set.", diffDisasm.ErrorCount);
                    errorCount += diffDisasm.ErrorCount;
                }
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
                .Where(s => (s.EndsWith(".exe") || s.EndsWith(".dll"))
                    && !s.Contains(".ni."));

            foreach (var filePath in subFiles)
            {
                if (config.DoVerboseOutput)
                {
                    Console.WriteLine("Scaning: {0}", filePath);
                }

                // skip if not an assembly
                if (!IsAssembly(filePath))
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

        private class DisasmEngine
        {
            private string _executablePath;
            private Config _config;
            private string _rootPath = null;
            private IReadOnlyList<string> _platformPaths;
            private List<AssemblyInfo> _assemblyInfoList;
            public bool doGCDump = false;
            public bool verbose = false;
            private int _errorCount = 0;

            public int ErrorCount { get { return _errorCount; } }

            public DisasmEngine(string executable, Config config, string outputPath,
                List<AssemblyInfo> assemblyInfoList)
            {
                _config = config;
                _executablePath = executable;
                _rootPath = outputPath;
                _platformPaths = config.PlatformPaths;
                _assemblyInfoList = assemblyInfoList;

                this.doGCDump = config.DumpGCInfo;
                this.verbose = config.DoVerboseOutput;
            }

            public void GenerateAsm()
            {
                // Build a command per assembly to generate the asm output.
                foreach (var assembly in _assemblyInfoList)
                {
                    string fullPathAssembly = Path.Combine(assembly.Path, assembly.Name);

                    if (!File.Exists(fullPathAssembly))
                    {
                        // Assembly not found.  Produce a warning and skip this input.
                        Console.WriteLine("Skipping. Assembly not found: {0}", fullPathAssembly);
                        continue;
                    }

                    List<string> commandArgs = new List<string>() { fullPathAssembly };

                    // Set platform assermbly path if it's defined.
                    if (_platformPaths.Count > 0)
                    {
                        commandArgs.Insert(0, "/Platform_Assemblies_Paths");
                        commandArgs.Insert(1, String.Join(" ", _platformPaths));
                    }

                    Command generateCmd = Command.Create(
                        _executablePath,
                        commandArgs);

                    // Pick up ambient COMPlus settings.
                    foreach (string envVar in Environment.GetEnvironmentVariables().Keys)
                    {
                        if (envVar.IndexOf("COMPlus_") == 0)
                        {
                            string value = Environment.GetEnvironmentVariable(envVar);
                            if (this.verbose)
                            {
                                Console.WriteLine("Incorporating ambient setting: {0}={1}", envVar, value);
                            }
                            generateCmd.EnvironmentVariable(envVar, value);
                        }
                    }

                    // Set up environment do disasm.
                    generateCmd.EnvironmentVariable("COMPlus_NgenDisasm", "*");
                    generateCmd.EnvironmentVariable("COMPlus_NgenUnwindDump", "*");
                    generateCmd.EnvironmentVariable("COMPlus_NgenEHDump", "*");
                    generateCmd.EnvironmentVariable("COMPlus_JitDiffableDasm", "1");

                    if (this.doGCDump)
                    {
                        generateCmd.EnvironmentVariable("COMPlus_NgenGCDump", "*");
                    }

                    if (this.verbose)
                    {
                        Console.WriteLine("Running: {0} {1}", _executablePath, String.Join(" ", commandArgs));
                    }

                    CommandResult result;

                    if (_rootPath != null)
                    {
                        // Generate path to the output file
                        var assemblyFileName = Path.ChangeExtension(assembly.Name, ".dasm");
                        var path = Path.Combine(_rootPath, assembly.OutputPath, assemblyFileName);

                        PathUtility.EnsureParentDirectory(path);

                        // Redirect stdout/stderr to disasm file and run command.
                        using (var outputStream = System.IO.File.Create(path))
                        {
                            using (var outputStreamWriter = new StreamWriter(outputStream))
                            {
                                // Forward output and error to file.
                                generateCmd.ForwardStdOut(outputStreamWriter);
                                generateCmd.ForwardStdErr(outputStreamWriter);
                                result = generateCmd.Execute();
                            }
                        }
                        
                        if (result.ExitCode != 0)
                        {
                            Console.WriteLine("Error running {0} on {1}", _executablePath, fullPathAssembly);
                            _errorCount++;

                            // If the tool still produced a output file rename it to indicate
                            // the error in the file system.
                            if (File.Exists(path))
                            {
                                // Change file to *.err.
                                string errorPath = Path.ChangeExtension(path, ".err");

                                // If this is a rerun to the same output, overwrite with current
                                // error output.
                                if (File.Exists(errorPath))
                                {
                                    File.Delete(errorPath);
                                }

                                File.Move(path, errorPath);
                            }
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
