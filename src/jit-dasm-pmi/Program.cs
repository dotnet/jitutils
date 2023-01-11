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
using System.CommandLine.Parsing;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;

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
        private readonly JitDasmPmiRootCommand _command;
        private readonly string _corerunPath;
        private readonly bool _verbose;

        public Program(JitDasmPmiRootCommand command)
        {
            _command = command;
            _corerunPath = Get(command.CorerunPath);
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
            new CommandLineBuilder(new JitDasmPmiRootCommand(args))
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
            string testOverlayDir = Path.GetDirectoryName(_corerunPath);
            string jitPath = Get(_command.JitPath);
            string jitDir = Path.GetDirectoryName(jitPath);
            string realJitPath = Path.Combine(testOverlayDir, GetPmiJitLibraryName(""));
            string tempJitPath = Path.Combine(testOverlayDir, GetPmiJitLibraryName("-backup"));

            int errorCount = 0;
            bool copyjit = !Get(_command.NoCopyJit);

            try
            {
                if (copyjit)
                {
                    if (_verbose)
                    {
                        Console.WriteLine($"Copying default jit: {realJitPath} ==> {tempJitPath}");
                    }
                    File.Copy(realJitPath, tempJitPath, true);
                    if (_verbose)
                    {
                        Console.WriteLine($"Copying in the test jit: {jitPath} ==> {realJitPath}");
                    }
                    // May need chmod +x for non-windows ??
                    File.Copy(jitPath, realJitPath, true);
                }

                GenerateAsmInternal(assemblyInfoList, ref errorCount);
            }
            catch (Exception e)
            {
                Console.WriteLine($"JIT DASM PMI failed: {e.Message}");
                errorCount++;
            }
            finally
            {
                if (copyjit)
                {
                    if (_verbose)
                    {
                        Console.WriteLine($"Restoring default jit: {tempJitPath} ==> {realJitPath}");
                    }
                    File.Copy(tempJitPath, realJitPath, true);
                }
            }

            return errorCount;

            string GetPmiJitLibraryName(string suffix)
            {
                string jitName = Path.GetFileNameWithoutExtension(jitPath);
                string pmiJitName = jitName + suffix + Path.GetExtension(jitPath);
                return pmiJitName;
            }
        }
        void GenerateAsmInternal(List<AssemblyInfo> assemblyInfoList, ref int errorCount)
        {
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

                string binDir = Path.GetDirectoryName(System.AppContext.BaseDirectory);
                string command = "DRIVEALL-QUIET";
                if (Get(_command.Cctors))
                {
                    command += "-CCTORS";
                }
                List<string> commandArgs = new List<string>() { Path.Combine(binDir, "pmi.dll"), command, fullPathAssembly };

                Dictionary<string, string> _environmentVariables = new Dictionary<string, string>();
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

                StringBuilder pmiEnv = new StringBuilder();
                // Append environment variable to the string that will be used as a value of PMIENV environment
                // variable.
                void AppendEnvironmentVariableToPmiEnv(string varName, string varValue)
                {
                    if (pmiEnv.Length > 0)
                    {
                        pmiEnv.Append(";");
                    }
                    pmiEnv.Append(varName + "=" + varValue);
                    if (_verbose)
                    {
                        Console.WriteLine("Appending: {0}={1} to PMIENV", varName, varValue);
                    }
                }

                // Pick up ambient DOTNET settings.
                foreach (string envVar in Environment.GetEnvironmentVariables().Keys)
                {
                    if (envVar.IndexOf("DOTNET_") == 0)
                    {
                        string value = Environment.GetEnvironmentVariable(envVar);
                        AppendEnvironmentVariableToPmiEnv(envVar, value);
                    }
                }

                // Set up environment do PMI based disasm.
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitDisasm", "*");
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitDisasmAssemblies", Path.GetFileNameWithoutExtension(assembly.Name));
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitUnwindDump", "*");
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitEHDump", "*");
                if (!Get(_command.NoDiffable))
                {
                    AppendEnvironmentVariableToPmiEnv("DOTNET_JitDiffableDasm", "1");
                }
                AppendEnvironmentVariableToPmiEnv("DOTNET_ReadyToRun", "0");
                AppendEnvironmentVariableToPmiEnv("DOTNET_ZapDisable", "1");
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitEnableNoWayAssert", "1");    // Force noway_assert to generate assert (not fall back to MinOpts).
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitNoForceFallback", "1");      // Don't stress noway fallback path.
                AppendEnvironmentVariableToPmiEnv("DOTNET_JitRequired", "1");             // Force NO_WAY to generate assert. Also generates assert for BADCODE/BADCODE3.

                bool tier0 = Get(_command.Tier0);
                // We likely don't want tiering enabled, but allow it, if user wants tier0 codegen
                AppendEnvironmentVariableToPmiEnv("DOTNET_TieredCompilation", tier0 ? "1" : "0");

                if (tier0)
                {
                    // jit all methods at tier0
                    AppendEnvironmentVariableToPmiEnv("DOTNET_TC_QuickJitForLoops", "1");
                    // don't promote any method to tier1
                    AppendEnvironmentVariableToPmiEnv("DOTNET_TC_CallCounting", "0");
                }

                if (Get(_command.DumpGCInfo ))
                {
                    AppendEnvironmentVariableToPmiEnv("DOTNET_JitGCDump", "*");
                }

                if (Get(_command.DumpDebugInfo))
                {
                    AppendEnvironmentVariableToPmiEnv("DOTNET_JitDebugDump", "*");
                }

                string altJit = Get(_command.AltJit);
                if (altJit != null)
                {
                    AppendEnvironmentVariableToPmiEnv("DOTNET_AltJit", "*");
                    AppendEnvironmentVariableToPmiEnv("DOTNET_AltJitName", altJit);

                    const string arm64AsTarget = "_arm64_";
                    int targetArm64 = altJit.IndexOf(arm64AsTarget);
                    if (targetArm64 > 0)
                    {
                        bool isHostArm64 = (altJit.IndexOf("arm64", targetArm64 + arm64AsTarget.Length) > 0);
                        if (!isHostArm64)
                        {
                            // If this looks like a cross-targeting altjit with a arm64 target and a different host
                            // then fix the SIMD size.
                            AppendEnvironmentVariableToPmiEnv("DOTNET_SIMD16ByteOnly", "1");
                        }
                    }
                }

                // Set up PMI path...
                AddEnvironmentVariable("PMIPATH", assembly.Path);

                if (_verbose)
                {
                    Console.WriteLine("Running: {0} {1}", _corerunPath, string.Join(" ", commandArgs));
                }

                ProcessResult result;

                string outputPath = Get(_command.OutputPath);
                if (outputPath != null)
                {
                    // Generate path to the output file
                    var assemblyFileName = Path.ChangeExtension(assembly.Name, ".dasm");
                    var dasmPath = Path.Combine(outputPath, assembly.OutputPath, assemblyFileName);
                    var logPath = Path.ChangeExtension(dasmPath, ".log");

                    Utility.EnsureParentDirectoryExists(dasmPath);

                    AppendEnvironmentVariableToPmiEnv("DOTNET_JitStdOutFile", dasmPath);

                    AddEnvironmentVariable("PMIENV", pmiEnv.ToString());

                    result = Utility.ExecuteProcess(_corerunPath, commandArgs, true, environmentVariables: _environmentVariables);

                    // Redirect stdout/stderr to log file and run command.
                    StringBuilder output = new StringBuilder();
                    if (!string.IsNullOrEmpty(result.StdOut))
                    {
                        output.AppendLine(result.StdOut);
                    }
                    if (!string.IsNullOrEmpty(result.StdErr) && (result.StdOut != result.StdErr))
                    {
                        output.AppendLine(result.StdErr);
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
                            Console.Error.WriteLine("Error running {0} on {1}", _corerunPath, fullPathAssembly);
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
                    AddEnvironmentVariable("PMIENV", pmiEnv.ToString());

                    // By default forward to output to stdout/stderr.
                    result = Utility.ExecuteProcess(_corerunPath, commandArgs, environmentVariables: _environmentVariables);

                    if (result.ExitCode != 0)
                    {
                        errorCount  ++;
                    }
                }
            }
        }
    }
}
