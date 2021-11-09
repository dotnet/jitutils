// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ManagedCodeGen
{

    public class ProcessResult
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;
    }

    public class ProcessManager : IDisposable
    {
        private List<Process> ProcessesList = new List<Process>();
        private static ProcessManager processManager;

        private ProcessManager()
        {
            Console.CancelKeyPress += new ConsoleCancelEventHandler(CancelKeyPress);
        }

        // Ctrl+Cancel and Ctrl+Break event
        static void CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Cancelling");
            if (e.SpecialKey == ConsoleSpecialKey.ControlC || e.SpecialKey == ConsoleSpecialKey.ControlBreak)
            {
                ProcessManager.Instance.KillAllProcesses(true);
            }
        }

        public static ProcessManager Instance  
        {
            get
            {
                if (processManager == null)
                {
                    processManager = new ProcessManager();
                }
                return processManager;
            }
        }

        // Creates a Process object and set the appropriate exitHandler
        public Process Start(ProcessStartInfo startInfo)
        {
            Process process = new Process
            {
                EnableRaisingEvents = true,
                StartInfo = startInfo
            };
            RegisterProcess(process);
            process.Exited += (sender, e) => UnregisterProcess(process);
            return process;
        }

        public void Dispose()
        {
            KillAllProcesses(false);
            GC.SuppressFinalize(this);
        }

        private void RegisterProcess(Process p)
        {
            lock (ProcessesList)
            {
                ProcessesList.Add(p);
            }
        }

        private void UnregisterProcess(Process p)
        {
            lock (ProcessesList)
            {
                ProcessesList.Remove(p);
            }
        }

        // Kills all the associated processes
        private void KillAllProcesses(bool printPid)
        {
            lock (ProcessesList)
            {
                foreach (var process in ProcessesList)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            if (printPid)
                            {
                                Console.WriteLine($"Killing {process.Id}");
                            }
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch { }
                }
            }
        }
    }

    public class Utility
    {
        public static string CombinePath(string basePath, string[] pathComponents)
        {
            string resultPath = basePath;
            foreach (var dir in pathComponents)
            {
                resultPath = Path.Combine(resultPath, dir);
            }
            return resultPath;
        }

        // GetRepoRoot: Determine if the current directory is within the directory tree of a dotnet/runtime
        // repo clone. Depends on "git".
        public static string GetRepoRoot(bool verbose = false)
        {
            // git rev-parse --show-toplevel
            List<string> commandArgs = new List<string> { "rev-parse", "--show-toplevel" };
            ProcessResult result = ExecuteProcess("git", commandArgs, true);
            if (result.ExitCode != 0)
            {
                if (verbose)
                {
                    Console.Error.WriteLine("'git rev-parse --show-toplevel' returned non-zero ({0})", result.ExitCode);
                }
                return null;
            }

            var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var git_root = lines[0];
            var repo_root = git_root.Replace('/', Path.DirectorySeparatorChar);

            // Is it actually the dotnet/runtime repo?
            commandArgs = new List<string> { "remote", "-v" };
            result = ExecuteProcess("git", commandArgs, true);
            if (result.ExitCode != 0)
            {
                if (verbose)
                {
                    Console.Error.WriteLine("'git remote -v' returned non-zero ({0})", result.ExitCode);
                }
                return null;
            }

            bool isRuntime = result.StdOut.Contains("/runtime");
            if (!isRuntime)
            {
                if (verbose)
                {
                    Console.Error.WriteLine("Doesn't appear to be the dotnet/runtime repo:");
                    Console.Error.WriteLine(result.StdOut);
                }
                return null;
            }

            if (verbose)
            {
                Console.WriteLine("Repo root: " + repo_root);
            }
            return repo_root;
        }

        public static ProcessResult ExecuteProcess(string name, IEnumerable<string> commandArgs, bool capture = false, string workingDirectory = "", Dictionary<string, string> environmentVariables = null)
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = workingDirectory,
                FileName = name,
                Arguments = string.Join(" ", commandArgs)
            };

            if (environmentVariables != null)
            {
                foreach (var envVar in environmentVariables)
                {
                    startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                }
            }

            // set up the pipe for the stdout and builder for stderr
            StringBuilder _errorDataStringBuilder = new StringBuilder();
            StringBuilder _outputDataStringBuilder = new StringBuilder();

            Process process = ProcessManager.Instance.Start(startInfo);

            if (capture)
            {
                // Handle output from the process
                process.OutputDataReceived += (object sender, DataReceivedEventArgs args) => {
                    if (args.Data == null)
                    {
                        return;
                    }
                    lock (_outputDataStringBuilder)
                    {
                        _outputDataStringBuilder.AppendLine(args.Data);
                    }
                };

                // Handle errors from the process
                process.ErrorDataReceived += (object sender, DataReceivedEventArgs args) => {
                    if (args.Data == null)
                    {
                        return;
                    }
                    lock (_errorDataStringBuilder)
                    {
                        _errorDataStringBuilder.AppendLine(args.Data);
                    }
                };
            }

            // Finally, start the process.
            try
            {
                process.Start();
            }
            catch (System.Exception e)
            {
                // Maybe the program we're spawning wasn't found (ERROR_FILE_NOT_FOUND == 2).
                Console.Error.WriteLine($"Error: failed to start '{name} {startInfo.Arguments}': {e.Message}");

                return new ProcessResult()
                {
                    ExitCode = -1,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                };
            }

            if (capture)
            {
                // Set up async reads for stderr and stdout
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            else
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                if (!string.IsNullOrEmpty(stdout))
                {
                    Console.Write(stdout);
                }
                if (!string.IsNullOrEmpty(stderr) && (stdout != stderr))
                {
                    Console.Write(stderr);
                }
            }

            process.WaitForExit();

            return new ProcessResult()
            {
                ExitCode = process.ExitCode,
                StdOut = capture ? _outputDataStringBuilder.ToString() : string.Empty,
                StdErr = capture ? _errorDataStringBuilder.ToString() : string.Empty
            };
        }

        // Check to see if the passed filePath is to an assembly.
        public static bool IsAssembly(string filePath)
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

        // Ensures parent directory of filePath exists. If not, creates one.
        public static void EnsureParentDirectoryExists(string filePath)
        {
            string directoryPath = Path.GetDirectoryName(filePath);

            EnsureDirectoryExists(directoryPath);
        }

        public static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }
}
