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
using System.CommandLine.Parsing;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ManagedCodeGen
{
    public class AssemblyInfo
    {
        public string Name { get; set; }
        // Contains path to assembly.
        public string Path { get; set; }
        // Contains relative path within output directory for given assembly.
        // This allows for different output directories per tool.
        public string OutputPath { get; set; }
    }


    internal sealed class Program
    {
        private readonly JitDasmRootCommand _command;
        private readonly string _crossgenPath;
        private readonly bool _verbose;
        private readonly Dictionary<string, string> _environmentVariables = new();

        public Program(JitDasmRootCommand command)
        {
            _command = command;
            _crossgenPath = Get(command.CrossgenPath);
            _verbose = Get(command.Verbose);
        }

        public int Run()
        {
            // Stop to attach a debugger if desired.
            if (Get(_command.WaitForDebugger))
            {
                Console.WriteLine("Wait for a debugger to attach. Press ENTER to continue");
                Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");
                Console.ReadLine();
            }

            // Builds assemblyInfoList on jitdasm
            List<AssemblyInfo> assemblyWorkList = GenerateAssemblyWorklist();

            // Produces a set of disasm outputs for a given code generator and assembly list/
            int errorCount = GenerateAsm(assemblyWorkList);
            if (errorCount > 0)
            {
                Console.Error.WriteLine("{0} errors compiling set.", errorCount);
            }

            return errorCount;
        }

        private T Get<T>(Option<T> option) => _command.Result.GetValue(option);
        private T Get<T>(Argument<T> arg) => _command.Result.GetValue(arg);

        private static int Main(string[] args) =>
            new CommandLineBuilder(new JitDasmRootCommand(args))
                .UseVersionOption("--version", "-v")
                .UseHelp()
                .UseParseErrorReporting()
                .Build()
                .Invoke(args);

        public List<AssemblyInfo> GenerateAssemblyWorklist()
        {
            List<string> assemblyList = new List<string>();
            List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();

            string filename = Get(_command.Filename);
            if (filename != null)
            {
                assemblyList = new List<string>();
                string inputFile = filename;

                // Open file, read assemblies one per line, and add them to the assembly list.
                using (var inputStream = File.Open(inputFile, FileMode.Open))
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

            List<string> userAssemblyList = Get(_command.AssemblyList);
            if (userAssemblyList.Count > 0)
            {
                // Append command line assemblies
                assemblyList.AddRange(userAssemblyList);
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
                    if (_verbose)
                    {
                        Console.WriteLine("Processing directory: {0}", path);
                    }

                    // For the directory case create a stack and recursively find any
                    // assemblies for compilation.
                    List<AssemblyInfo> directoryAssemblyInfoList = IdentifyAssemblies(path);

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
        private List<AssemblyInfo> IdentifyAssemblies(string rootPath)
        {
            List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();
            string fullRootPath = Path.GetFullPath(rootPath);
            SearchOption searchOption = (Get(_command.Recursive)) ?
                SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Get files that could be assemblies, but discard currently
            // ngen'd assemblies.
            var subFiles = Directory.EnumerateFiles(rootPath, "*", searchOption)
                .Where(s => (s.EndsWith(".exe") || s.EndsWith(".dll")) && !s.Contains(".ni."));

            foreach (var filePath in subFiles)
            {
                if (_verbose)
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

        public int GenerateAsm(List<AssemblyInfo> assemblyInfoList)
        {
            int errorCount = 0;

            // Build a command per assembly to generate the asm output.
            foreach (var assembly in assemblyInfoList)
            {
                if (_verbose)
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
                if (_command.CodeGeneratorV1)
                {
                    commandArgs.Add("/silent");
                }
                // Also pass /nologo to avoid spurious diffs that sometimes appear when errors
                // occur (sometimes the logo lines and error lines are interleaved).
                if (_command.CodeGeneratorV1)
                {
                    commandArgs.Add("/nologo");
                }

                string jitPath = Get(_command.JitPath);
                // Set jit path if it's defined.
                if (jitPath != null)
                {
                    commandArgs.Add(_command.CodeGeneratorV1 ? "/JitPath" : "--jitpath");
                    commandArgs.Add(jitPath);
                }

                // Set platform assembly path if it's defined.
                List<string> platformPaths = Get(_command.PlatformPaths);
                if (platformPaths.Count > 0)
                {
                    if (_command.CodeGeneratorV1)
                    {
                        commandArgs.Add("/Platform_Assemblies_Paths");
                        commandArgs.Add(string.Join(" ", platformPaths));
                    }
                    else
                    {
                        commandArgs.Add("--reference");
                        commandArgs.Add(string.Join(" ", platformPaths.Select(str => Path.Combine(str, "*.dll"))));
                    }
                }

                string extension = Path.GetExtension(fullPathAssembly);
                string nativeOutput = Path.ChangeExtension(fullPathAssembly, "ni" + extension);
                string mapOutput = Path.ChangeExtension(fullPathAssembly, "ni.map");

                string outputPath = Get(_command.OutputPath);
                if (outputPath != null)
                {
                    string assemblyNativeFileName = Path.ChangeExtension(assembly.Name, "ni" + extension);
                    string assemblyMapFileName = Path.ChangeExtension(assembly.Name, "ni.map");
                    nativeOutput = Path.Combine(outputPath, assembly.OutputPath, assemblyNativeFileName);
                    mapOutput = Path.Combine(outputPath, assembly.OutputPath, assemblyMapFileName);

                    Utility.EnsureParentDirectoryExists(nativeOutput);

                    commandArgs.Add(_command.CodeGeneratorV1 ? "/out" : "--out");
                    commandArgs.Add(nativeOutput);
                }

                if (!_command.CodeGeneratorV1)
                {
                    commandArgs.Add("--optimize");
                }

                commandArgs.Add(fullPathAssembly);

                // Pick up ambient DOTNET_ settings.
                foreach (string envVar in Environment.GetEnvironmentVariables().Keys)
                {
                    if (envVar.IndexOf("DOTNET_") == 0)
                    {
                        string value = Environment.GetEnvironmentVariable(envVar);
                        AddEnvironmentVariable(envVar, value);
                    }
                }

                // Set up environment do disasm.
                AddEnvironmentVariable("DOTNET_JitDisasm", "*");
                AddEnvironmentVariable("DOTNET_JitUnwindDump", "*");
                AddEnvironmentVariable("DOTNET_JitEHDump", "*");
                if (!Get(_command.NoDiffable))
                {
                    AddEnvironmentVariable("DOTNET_JitDiffableDasm", "1");
                }
                AddEnvironmentVariable("DOTNET_JitEnableNoWayAssert", "1");    // Force noway_assert to generate assert (not fall back to MinOpts).
                AddEnvironmentVariable("DOTNET_JitNoForceFallback", "1");      // Don't stress noway fallback path.
                AddEnvironmentVariable("DOTNET_JitRequired", "1");             // Force NO_WAY to generate assert. Also generates assert for BADCODE/BADCODE3.

                if (Get(_command.DumpGCInfo))
                {
                    AddEnvironmentVariable("DOTNET_JitGCDump", "*");
                }

                if (Get(_command.DumpDebugInfo))
                {
                    AddEnvironmentVariable("DOTNET_JitDebugDump", "*");
                }

                string altJit = Get(_command.AltJit);
                if (altJit != null)
                {
                    AddEnvironmentVariable("DOTNET_AltJit", "*");
                    AddEnvironmentVariable("DOTNET_AltJitName", altJit);
                }

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Disable fragile relocs on non-Windows platforms, see
                    // https://github.com/dotnet/runtime/issues/87842
                    AddEnvironmentVariable("DOTNET_JITAllowOptionalRelocs", "0");
                }

                string dasmPath = null;
                if (outputPath != null)
                {
                    // Generate path to the output file
                    var assemblyFileName = Path.ChangeExtension(assembly.Name, ".dasm");
                    dasmPath = Path.Combine(outputPath, assembly.OutputPath, assemblyFileName);

                    Utility.EnsureParentDirectoryExists(dasmPath);

                    AddEnvironmentVariable("DOTNET_JitStdOutFile", dasmPath);
                }

                if (_verbose)
                {
                    Console.WriteLine("Running: {0} {1}", _crossgenPath, string.Join(" ", commandArgs));
                }

                ProcessResult result;

                if (outputPath != null)
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
                        errorCount++;

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
                            Console.Error.WriteLine("Error running {0} on {1}", _crossgenPath, fullPathAssembly);
                        }
                    }

                    if (hasOutput && File.Exists(logPath) && !File.Exists(dasmPath))
                    {
                        // Looks like the JIT does not support DOTNET_JitStdOutFile so
                        // the assembly output must be in the log file.
                        File.Move(logPath, dasmPath);
                    }
                }
                else
                {
                    // By default forward to output to stdout/stderr.
                    result = ExecuteProcess(commandArgs, false);

                    if (result.ExitCode != 0)
                    {
                        errorCount++;
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

            return errorCount;
        }

        private ProcessResult ExecuteProcess(List<string> commandArgs, bool capture)
        {
            if (_command.CodeGeneratorV1)
            {
                return Utility.ExecuteProcess(_crossgenPath, commandArgs, capture, environmentVariables: _environmentVariables);
            }
            else
            {
                commandArgs.Add("--parallelism 1");
                foreach (var envVar in _environmentVariables)
                {
                    commandArgs.Add("--codegenopt");
                    string dotnetPrefix = "DOTNET_";
                    commandArgs.Add(string.Format("{0}={1}", envVar.Key.Substring(dotnetPrefix.Length), envVar.Value));
                }
                return Utility.ExecuteProcess(_crossgenPath, commandArgs, capture);
            }
        }

        // Add environment variables to the environment of the command we are going to execute, and
        // display them to the user in verbose mode.
        void AddEnvironmentVariable(string varName, string varValue)
        {
            _environmentVariables[varName] = varValue;
            if (_verbose)
            {
                Console.WriteLine("set {0}={1}", varName, varValue);
            }
        }
    }
}
