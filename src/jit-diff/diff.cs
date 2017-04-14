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
    public partial class jitdiff
    {
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
                CommandResult result = Utility.TryCommand(s_asmTool, item.DasmArgs);
                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("Dasm command \"{0}\" returned with {1} failures", command, result.ExitCode);
                    dasmFailures += result.ExitCode;
                }

                return dasmFailures;
            }

            private void StartDasmWork(List<string> args)
            {
                Task<int> task = Task<int>.Factory.StartNew(() => RunDasmTool(new DasmWorkItem(args)));
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

            private void StartDasmWorkOne(List<string> commandArgs, string tagBaseDiff, string clrPath, AssemblyInfo assemblyInfo)
            {
                List<string> args = ConstructArgs(commandArgs, clrPath);

                string outputPath = Path.Combine(m_config.OutputPath, tagBaseDiff, assemblyInfo.OutputPath);
                args.Add("--output");
                args.Add(outputPath);

                args.Add(assemblyInfo.Path);

                StartDasmWork(args);
            }

            private void StartDasmWorkBaseDiff(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                foreach (var assemblyInfo in assemblyWorkList)
                {
                    if (m_config.DoBaseCompiles)
                    {
                        StartDasmWorkOne(commandArgs, "base", m_config.BasePath, assemblyInfo);
                    }
                    if (m_config.DoDiffCompiles)
                    {
                        StartDasmWorkOne(commandArgs, "diff", m_config.DiffPath, assemblyInfo);
                    }
                }
            }

            private List<string> ConstructArgs(List<string> commandArgs, string clrPath)
            {
                List<string> dasmArgs = commandArgs.ToList();
                dasmArgs.Add("--crossgen");
                if (m_config.HasCrossgenExe)
                {
                    dasmArgs.Add(m_config.CrossgenExe);

                    var jitPath = Path.Combine(clrPath, GetJitLibraryName(m_config.PlatformMoniker));
                    if (!File.Exists(jitPath))
                    {
                        Console.Error.WriteLine("clrjit not found at {0}", jitPath);
                        return null;
                    }

                    dasmArgs.Add("--jit");
                    dasmArgs.Add(jitPath);
                }
                else
                {
                    var crossgenPath = Path.Combine(clrPath, GetCrossgenExecutableName(m_config.PlatformMoniker));
                    if (!File.Exists(crossgenPath))
                    {
                        Console.Error.WriteLine("crossgen not found at {0}", crossgenPath);
                        return null;
                    }

                    dasmArgs.Add(crossgenPath);
                }

                return dasmArgs;
            }

            // Returns:
            // 0 on success,
            // Otherwise, a count of the number of failures generating asm, as reported by the asm tool.
            private int RunDasmTool(List<string> commandArgs, List<AssemblyInfo> assemblyWorkList)
            {
                StartDasmWorkBaseDiff(commandArgs, assemblyWorkList);

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
                string diffString = "";

                if (config.CoreLib)
                {
                    diffString += "System.Private.CoreLib.dll";
                }
                else if (config.DoFrameworks)
                {
                    diffString += "System.Private.CoreLib.dll, framework assemblies";
                }

                if (config.Benchmarks)
                {
                    if (!String.IsNullOrEmpty(diffString)) diffString += ", ";
                    diffString += "benchstones and benchmarks game in " + config.TestRoot;
                }
                else if (config.DoTestTree)
                {
                    if (!String.IsNullOrEmpty(diffString)) diffString += ", ";
                    diffString += "assemblies in " + config.TestRoot;
                }

                Console.WriteLine("Beginning diff of {0}", diffString);

                // Create subjob that runs jit-dasm, which should be in path, with the 
                // relevent coreclr assemblies/paths.

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

                List<AssemblyInfo> assemblyWorkList = GenerateAssemblyWorklist(config);

                DiffTool diffTool = new DiffTool(config);
                int dasmFailures = diffTool.RunDasmTool(commandArgs, assemblyWorkList);

                // Analyze completed run.

                if (config.DoAnalyze && config.DoDiffCompiles && config.DoBaseCompiles)
                {
                    List<string> analysisArgs = new List<string>();

                    analysisArgs.Add("--base");
                    analysisArgs.Add(Path.Combine(config.OutputPath, "base"));
                    analysisArgs.Add("--diff");
                    analysisArgs.Add(Path.Combine(config.OutputPath, "diff"));
                    analysisArgs.Add("--recursive");

                    Console.WriteLine("Analyze command: {0} {1}",
                        s_analysisTool, String.Join(" ", analysisArgs));

                    CommandResult analyzeResult = Utility.TryCommand(s_analysisTool, analysisArgs);
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

            public static List<AssemblyInfo> GenerateAssemblyWorklist(Config config)
            {
                List<AssemblyInfo> assemblyInfoList = new List<AssemblyInfo>();

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
    }
}
