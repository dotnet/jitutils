// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
//using System.Diagnostics;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
//using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
//using Microsoft.DotNet.Tools.Common;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using System.Collections.Concurrent;
using System.Threading.Tasks;
//using System.Runtime.InteropServices;

namespace ManagedCodeGen
{
    using DasmWorkTask = Task<(DasmWorkKind kind, int errorCount)>;

    public enum DasmWorkKind
    {
        Base,
        Diff
    }

    public partial class jitdiff
    {
        public class DasmResult
        {
            public int BaseErrors = 0;
            public int DiffErrors = 0;
            public bool CaughtException = false;
            public bool Success => ((BaseErrors == 0) && (DiffErrors == 0) && !CaughtException);
        }

        public abstract class DiffTool
        {
            private class DasmWorkItem
            {
                public List<string> DasmArgs { get; set; }

                public DasmWorkItem(List<string> dasmArgs)
                {
                    DasmArgs = dasmArgs;
                }
            }

            protected Config m_config;
            protected List<DasmWorkTask> DasmWorkTasks = new List<DasmWorkTask>();
            protected string m_name;
            protected string m_commandName;
            public String Name => m_name;
            public string CommandName => m_commandName;

            protected DiffTool(Config config)
            {
                m_config = config;
            }

            protected static DiffTool NewDiffTool(Config config)
            {
                DiffTool result = null;

                switch (config.DoCommand)
                {
                    case Commands.Diff:
                        result = new CrossgenDiffTool(config);
                        break;
                    case Commands.PmiDiff:
                        result = new PmiDiffTool(config);
                        break;
                    default:
                        Console.WriteLine($"Unexpected command for diff: {config.DoCommand}");
                        break;

                }
                return result;
            }

            // Returns a count of the number of failures.
            private int RunDasmTool(DasmWorkItem item)
            {
                int dasmFailures = 0;
                string command = CommandName + " " + String.Join(" ", item.DasmArgs);
                if (m_config.Verbose)
                {
                    Console.WriteLine("Dasm command: {0}", command);
                }
                CommandResult result = Utility.TryCommand(CommandName, item.DasmArgs);
                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("Dasm command \"{0}\" returned with {1} failures", command, result.ExitCode);
                    dasmFailures += result.ExitCode;
                }

                return dasmFailures;
            }

            private void StartDasmWork(DasmWorkKind kind, List<string> args)
            {
                var task = DasmWorkTask.Factory.StartNew(() => (kind, RunDasmTool(new DasmWorkItem(args))));
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

            protected void StartDasmWorkOne(DasmWorkKind kind, List<string> commandArgs, string tagBaseDiff, string clrPath, AssemblyInfo assemblyInfo)
            {
                List<string> args = ConstructArgs(commandArgs, clrPath);

                string outputPath = Path.Combine(m_config.OutputPath, tagBaseDiff, assemblyInfo.OutputPath);
                args.Add("--output");
                args.Add(outputPath);

                args.Add(assemblyInfo.Path);

                StartDasmWork(kind, args);
            }

            protected virtual void StartDasmWorkBaseDiff(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                foreach (var assemblyInfo in assemblyWorkList)
                {
                    if (m_config.DoBaseCompiles)
                    {
                        StartDasmWorkOne(DasmWorkKind.Base, commandArgs, "base", m_config.BasePath, assemblyInfo);
                    }
                    if (m_config.DoDiffCompiles)
                    {
                        StartDasmWorkOne(DasmWorkKind.Diff, commandArgs, "diff", m_config.DiffPath, assemblyInfo);
                    }
                }
            }

            protected abstract List<string> ConstructArgs(List<string> commandArgs, string clrPath);
            private DasmResult RunDasmTool(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                StartDasmWorkBaseDiff(commandArgs, assemblyWorkList);

                DasmResult result = new DasmResult();

                try
                {
                    var taskArray = DasmWorkTasks.ToArray();
                    Task.WaitAll(taskArray);

                    foreach (var t in taskArray)
                    {
                        if (t.Result.kind == DasmWorkKind.Base)
                        {
                            result.BaseErrors += t.Result.errorCount;
                        }
                        else
                        {
                            result.DiffErrors += t.Result.errorCount;
                        }
                    }
                }
                catch (AggregateException ex)
                {
                    Console.Error.WriteLine("Dasm task failed with {0}", ex.Message);
                    result.CaughtException = true;
                }

                if (!result.Success)
                {
                    Console.Error.WriteLine("Dasm commands returned {0} base failures, {1} diff failures{2}.",
                        result.BaseErrors, result.DiffErrors, (result.CaughtException ? ", exception occurred" : ""));
                }

                return result;
            }

            // Returns 0 on success, 1 on failure.
            public static int DiffCommand(Config config)
            {
                DiffTool diffTool = NewDiffTool(config);
                string diffString = $"{diffTool.Name} Diffs for ";
                bool needPrefix = false;

                if (config.CoreLib)
                {
                    diffString += "System.Private.CoreLib.dll";
                    needPrefix = true;
                }
                else if (config.DoFrameworks)
                {
                    diffString += "System.Private.CoreLib.dll, framework assemblies";
                    needPrefix = true;
                }

                if (config.Benchmarks)
                {
                    if (needPrefix) diffString += ", ";
                    diffString += "benchstones and benchmarks game in " + config.TestRoot;
                    needPrefix = true;
                }
                else if (config.DoTestTree)
                {
                    if (needPrefix) diffString += ", ";
                    diffString += "assemblies in " + config.TestRoot;
                    needPrefix = true;
                }

                if (config.AssemblyName != null)
                {
                    if (needPrefix) diffString += ", ";
                    diffString += Path.GetFileName(config.AssemblyName);
                }

                Console.WriteLine($"Beginning {diffString}");

                // Create subjob that runs jit-dasm or jit-dasm-pmi (which should be in path)
                // with the relevent coreclr assemblies/paths.

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

                if (config.AltJit != null)
                {
                    commandArgs.Add("--altjit");
                    commandArgs.Add(config.AltJit);
                }

                DateTime startTime = DateTime.Now;
                List<AssemblyInfo> assemblyWorkList = GenerateAssemblyWorklist(config);
                DasmResult dasmResult = diffTool.RunDasmTool(commandArgs, assemblyWorkList);
                Console.WriteLine($"Completed {diffString} in {(DateTime.Now - startTime).TotalSeconds:F2}s");

                // Analyze completed run.

                if (config.DoAnalyze && config.DoDiffCompiles && config.DoBaseCompiles)
                {
                    List<string> analysisArgs = new List<string>();

                    analysisArgs.Add("--base");
                    analysisArgs.Add(Path.Combine(config.OutputPath, "base"));
                    analysisArgs.Add("--diff");
                    analysisArgs.Add(Path.Combine(config.OutputPath, "diff"));
                    analysisArgs.Add("--recursive");

                    if (config.Verbose)
                    {
                        Console.WriteLine("Analyze command: {0} {1}",
                            s_analysisTool, String.Join(" ", analysisArgs));
                    }

                    Console.WriteLine("Analyzing diffs...");
                    startTime = DateTime.Now;
                    CommandResult analyzeResult = Utility.TryCommand(s_analysisTool, analysisArgs);
                    Console.WriteLine($"Completed analysis in {(DateTime.Now - startTime).TotalSeconds:F2}s");
                }

                // Report any failures to generate asm at the very end (again). This is so
                // this information doesn't get buried in previous output.

                if (!dasmResult.Success)
                {
                    Console.Error.WriteLine("");
                    Console.Error.WriteLine("Warning: Failures detected generating asm: {0} base, {1} diff{2}",
                        dasmResult.BaseErrors, dasmResult.DiffErrors, (dasmResult.CaughtException ? ", exception occurred" : ""));

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

                if (config.AssemblyName != null)
                {
                    if (!File.Exists(config.AssemblyName))
                    {
                        Console.Error.WriteLine($"Warning: can't find specified assembly {config.AssemblyName}");
                    }
                    else
                    {
                        AssemblyInfo info = new AssemblyInfo
                        {
                            Path = config.AssemblyName,
                            OutputPath = ""
                        };

                        assemblyInfoList.Add(info);
                    }
                }

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
                    if (!Utility.IsAssembly(filePath))
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

        public class CrossgenDiffTool : DiffTool
        {
            public CrossgenDiffTool(Config config) : base(config)
            {
                m_name = "Crossgen";
                m_commandName = s_asmToolPrejit;
            }
            protected override List<string> ConstructArgs(List<string> commandArgs, string clrPath)
            {
                List<string> dasmArgs = commandArgs.ToList();
                dasmArgs.Add("--crossgen");
                if (m_config.HasCrossgenExe)
                {
                    dasmArgs.Add(m_config.CrossgenExe);
                }
                else
                {
                    var crossgenPath = Path.Combine(m_config.CoreRoot, GetCrossgenExecutableName(m_config.PlatformMoniker));
                    if (!File.Exists(crossgenPath))
                    {
                        Console.Error.WriteLine("crossgen not found at {0}", crossgenPath);
                        return null;
                    }

                    dasmArgs.Add(crossgenPath);
                }

                string jitName;
                if (m_config.AltJit != null)
                {
                    jitName = m_config.AltJit;
                }
                else
                {
                    jitName = GetJitLibraryName(m_config.PlatformMoniker);
                }

                var jitPath = Path.Combine(clrPath, jitName);
                if (!File.Exists(jitPath))
                {
                    Console.Error.WriteLine("JIT not found at {0}", jitPath);
                    return null;
                }

                dasmArgs.Add("--jit");
                dasmArgs.Add(jitPath);

                return dasmArgs;
            }
        }

        public class PmiDiffTool : DiffTool
        {
            string m_corerunPath;
            string m_defaultJitName;
            string m_testJitName;

            public PmiDiffTool(Config config) : base(config)
            {
                m_name = "PMI";
                m_commandName = s_asmToolJit;
                m_corerunPath = Path.Combine(m_config.CoreRoot, GetCorerunExecutableName(m_config.PlatformMoniker));
                m_defaultJitName = GetJitLibraryName(m_config.PlatformMoniker);
                if (m_config.AltJit != null)
                {
                    m_testJitName = m_config.AltJit;
                }
                else
                {
                    m_testJitName = m_defaultJitName;
                }
            }

            protected override List<string> ConstructArgs(List<string> commandArgs, string clrPath)
            {
                List<string> dasmArgs = commandArgs.ToList();
                dasmArgs.Add("--corerun");

                if (!File.Exists(m_corerunPath))
                {
                    Console.Error.WriteLine($"corerun not found at {m_corerunPath}");
                    return null;
                }

                dasmArgs.Add(m_corerunPath);

                var jitPath = Path.Combine(clrPath, m_testJitName);
                if (!File.Exists(jitPath))
                {
                    Console.Error.WriteLine($"JIT not found at {jitPath}");
                    return null;
                }

                dasmArgs.Add("--jit");
                dasmArgs.Add(jitPath);

                // jit-diffs will handle installing the right jit
                dasmArgs.Add("--nocopy");

                return dasmArgs;
            }

            void InstallBaseJit()
            {
                string realJitPath = Path.Combine(m_config.CoreRoot, m_defaultJitName);
                string tempJitPath = Path.Combine(m_config.CoreRoot, "backup-" + m_defaultJitName);
                string testJitPath = Path.Combine(m_config.BasePath, m_testJitName);
                if (m_config.Verbose)
                {
                    Console.WriteLine($"Copying default jit: {realJitPath} ==> {tempJitPath}");
                }
                File.Copy(realJitPath, tempJitPath, true);
                if (m_config.Verbose)
                {
                    Console.WriteLine($"Copying in the test jit: {testJitPath} ==> {realJitPath}");
                }
                File.Copy(testJitPath, realJitPath, true);
            }

            void InstallDiffJit()
            {
                string realJitPath = Path.Combine(m_config.CoreRoot, m_defaultJitName);
                string tempJitPath = Path.Combine(m_config.CoreRoot, "backup-" + m_defaultJitName);
                string testJitPath = Path.Combine(m_config.DiffPath, m_testJitName);
                if (m_config.Verbose)
                {
                    Console.WriteLine($"Copying default jit: {realJitPath} ==> {tempJitPath}");
                }
                File.Copy(realJitPath, tempJitPath, true);
                if (m_config.Verbose)
                {
                    Console.WriteLine($"Copying in the test jit: {testJitPath} ==> {realJitPath}");
                }
                File.Copy(testJitPath, realJitPath, true);
            }

            void RestoreDefaultJit()
            {
                string realJitPath = Path.Combine(m_config.CoreRoot, m_defaultJitName);
                string tempJitPath = Path.Combine(m_config.CoreRoot, "backup-" + m_defaultJitName);
                if (m_config.Verbose)
                {
                    Console.WriteLine($"Restoring default jit: {tempJitPath} ==> {realJitPath}");
                }
                File.Copy(tempJitPath, realJitPath, true);
            }

            // Because pmi modifies the test overlay, we can't run base and diff tasks in an interleaved manner.

            protected override void StartDasmWorkBaseDiff(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                if (m_config.DoBaseCompiles)
                {
                    try
                    {
                        InstallBaseJit();
                        foreach (var assemblyInfo in assemblyWorkList)
                        {
                            StartDasmWorkOne(DasmWorkKind.Base, commandArgs, "base", m_config.BasePath, assemblyInfo);
                        }

                        var taskArray = DasmWorkTasks.ToArray();
                        Task.WaitAll(taskArray);
                    }
                    finally
                    {
                        RestoreDefaultJit();
                    }
                }

                if (m_config.DoDiffCompiles)
                {
                    try
                    {
                        InstallDiffJit();
                        foreach (var assemblyInfo in assemblyWorkList)
                        {
                            StartDasmWorkOne(DasmWorkKind.Diff, commandArgs, "diff", m_config.DiffPath, assemblyInfo);
                        }

                        var taskArray = DasmWorkTasks.ToArray();
                        Task.WaitAll(taskArray);
                    }
                    finally
                    {
                        RestoreDefaultJit();
                    }
                }
            }
        }
    }
}
