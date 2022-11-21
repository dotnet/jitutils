// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace ManagedCodeGen
{
    internal sealed class CIJobsRootCommand : RootCommand
    {
        public Option<string> Server { get; } =
            new(new[] { "--server", "-s" }, "Url of the server. Defaults to http://ci.dot.net/");
        public Option<string> JobName { get; } =
            new(new[] { "--job", "-j" }, "Name of the job.");
        public Option<string> BranchName { get; } =
            new(new[] { "--branch", "-b" }, () => "master", "Name of the branch.");
        public Option<string> RepoName { get; } =
            new(new[] { "--repo", "-r" }, () => "dotnet_coreclr", "Name of the repo (e.g. dotnet_corefx or dotnet_coreclr).");
        public Option<string> MatchPattern { get; } =
            new(new[] { "--match", "-m" }, "Regex pattern used to select jobs output.");
        public Option<int> JobNumber { get; } =
            new(new[] { "--number", "-n" }, "Job number.");
        public Option<bool> ShowLastSuccessful { get; } =
            new(new[] { "--last-successful", "-l", }, "Show last successful build.");
        public Option<string> Commit { get; } =
            new(new[] { "--commit", "-c", }, "List build at this commit.");
        public Option<bool> ShowArtifacts { get; } =
            new(new[] { "--artifacts", "-a" }, "Show job artifacts on server.");
        public Option<string> OutputPath { get; } =
            new(new[] { "--output", "-o" }, "The path where output will be placed.");
        public Option<string> OutputRoot { get; } =
            new("--output-root", "The root directory where output will be placed. A subdirectory named by job and build number will be created within this to store the output.");
        public Option<bool> Unzip { get; } =
            new(new[] { "--unzip", "-u" }, "Unzip copied artifacts");
        public Option<string> ContentPath { get; } =
            new(new[] { "--ContentPath", "-p" }, "Relative product zip path. Default is artifact/bin/Product/*zip*/Product.zip");

        public ParseResult Result;

        public CIJobsRootCommand(string[] args) : base("Continuous integration build jobs tool")
        {
            List<string> errors = new();

            Command listCommand = new("list", "List jobs on dotnet-ci.cloudapp.net for the repo.")
            {
                Server,
                JobName,
                BranchName,
                RepoName,
                MatchPattern,
                JobNumber,
                ShowLastSuccessful,
                Commit,
                ShowArtifacts
            };

            listCommand.SetHandler(context => TryExecuteWithContextAsync(context, "list", result =>
            {
                int jobNumber = result.GetValue(JobNumber);
                bool showLastSuccessful = result.GetValue(ShowLastSuccessful);
                string commit = result.GetValue(Commit);

                if (result.FindResultFor(JobNumber) == null)
                {
                    if (jobNumber != 0)
                    {
                        errors.Add("Must select --job <name> to specify --number <num>.");
                    }

                    if (showLastSuccessful)
                    {
                        errors.Add("Must select --job <name> to specify --last_successful.");
                    }

                    if (commit != null)
                    {
                        errors.Add("Must select --job <name> to specify --commit <commit>.");
                    }

                    if (result.GetValue(ShowArtifacts))
                    {
                        errors.Add("Must select --job <name> to specify --artifacts.");
                    }
                }
                else
                {
                    if (Convert.ToInt32(jobNumber != 0) + Convert.ToInt32(showLastSuccessful) + Convert.ToInt32(commit != null) > 1)
                    {
                        errors.Add("Must have at most one of --number <num>, --last_successful, and --commit <commit> for list.");
                    }

                    if (!string.IsNullOrEmpty(result.GetValue(MatchPattern)))
                    {
                        errors.Add("Match pattern not valid with --job");
                    }
                }
            }));

            AddCommand(listCommand);

            Command copyCommand = new("copy", @"Copies job artifacts from dotnet-ci.cloudapp.net. This
command copies a zip of artifacts from a repo (defaulted to
dotnet_coreclr). The default location of the zips is the
Product sub-directory, though that can be changed using the
ContentPath(p) parameter")
            {
                Server,
                JobName,
                BranchName,
                RepoName,
                JobNumber,
                ShowLastSuccessful,
                Commit,
                ShowArtifacts,
                OutputPath,
                OutputRoot,
                Unzip,
                ContentPath
            };

            copyCommand.SetHandler(context => TryExecuteWithContextAsync(context, "copy", result =>
            {
                if (result.GetValue(JobName) == null)
                {
                    errors.Add("Must have --job <name> for copy.");
                }

                int jobNumber = result.GetValue(JobNumber);
                bool shwoLastSuccessful = result.GetValue(ShowLastSuccessful);
                string commit = result.GetValue(Commit);
                if (jobNumber == 0 && !shwoLastSuccessful && commit == null)
                {
                    errors.Add("Must have --number <num>, --last_successful, or --commit <commit> for copy.");
                }

                if (Convert.ToInt32(jobNumber != 0) + Convert.ToInt32(shwoLastSuccessful) + Convert.ToInt32(commit != null) > 1)
                {
                    errors.Add("Must have only one of --number <num>, --last_successful, and --commit <commit> for copy.");
                }

                string outputPath = result.GetValue(OutputPath);
                string outputRoot = result.GetValue(OutputRoot);
                if (outputPath == null && outputRoot == null)
                {
                    errors.Add("Must specify either --output <path> or --output_root <path> for copy.");
                }

                if (outputPath != null && outputRoot != null)
                {
                    errors.Add("Must specify only one of --output <path> or --output_root <path>.");
                }
            }));

            AddCommand(copyCommand);

            async Task TryExecuteWithContextAsync(InvocationContext context, string name, Action<ParseResult> validate)
            {
                Result = context.ParseResult;
                try
                {
                    validate(Result);
                    if (errors.Count > 0)
                    {
                        throw new Exception(string.Join(Environment.NewLine, errors));
                    }

                    context.ExitCode = await new Program(this).RunAsync(name);
                }
                catch (Exception e)
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine("Error: " + e.Message);
                    Console.Error.WriteLine(e.ToString());

                    Console.ResetColor();

                    context.ExitCode = 1;
                }
            }
        }
    }
}
