// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.DotNet.Cli.Utils;

namespace ManagedCodeGen
{
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

        // GetRepoRoot: Determine if the current directory is within the directory tree of a dotnet/coreclr
        // repo clone. Depends on "git".
        public static string GetRepoRoot(bool verbose = false)
        {
            // git rev-parse --show-toplevel
            List<string> commandArgs = new List<string> { "rev-parse", "--show-toplevel" };
            CommandResult result = TryCommand("git", commandArgs, true);
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

            // Is it actually the dotnet/coreclr repo?
            commandArgs = new List<string> { "remote", "-v" };
            result = TryCommand("git", commandArgs, true);
            if (result.ExitCode != 0)
            {
                if (verbose)
                {
                    Console.Error.WriteLine("'git remote -v' returned non-zero ({0})", result.ExitCode);
                }
                return null;
            }

            bool isCoreClr = result.StdOut.Contains("/coreclr");
            if (!isCoreClr)
            {
                if (verbose)
                {
                    Console.Error.WriteLine("Doesn't appear to be the dotnet/coreclr repo:");
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

        class ScriptResolverPolicyWrapper : ICommandResolverPolicy
        {
            public CompositeCommandResolver CreateCommandResolver() => ScriptCommandResolverPolicy.Create();
        }

        public static CommandResult TryCommand(string name, IEnumerable<string> commandArgs, bool capture = false, string workingDirectory = null)
        {
            try
            {
                Command command = Command.Create(new ScriptResolverPolicyWrapper(), name, commandArgs);

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    command.WorkingDirectory(workingDirectory);
                }

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
                Console.Error.WriteLine("\nerror: {0} command not found!  Add {0} to the path.", name, e);
                Environment.Exit(-1);
                return CommandResult.Empty;
            }
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
    }
}
