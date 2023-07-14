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
    internal sealed class CIJobsRootCommand : CliRootCommand
    {
        public CliOption<string> Server { get; } =
            new("--server", "-s") { Description = "Url of the server. Defaults to https://ci.dot.net/" };
        public CliOption<string> JobName { get; } =
            new("--job", "-j") { Description = "Name of the job." };
        public CliOption<string> BranchName { get; } =
            new("--branch", "-b") { DefaultValueFactory = _ => "master", Description = "Name of the branch." };
        public CliOption<string> RepoName { get; } =
            new("--repo", "-r") { DefaultValueFactory = _ => "dotnet_coreclr", Description = "Name of the repo (e.g. dotnet_corefx or dotnet_coreclr)." };
        public CliOption<string> MatchPattern { get; } =
            new("--match", "-m") { Description = "Regex pattern used to select jobs output." };
        public CliOption<int> JobNumber { get; } =
            new("--number", "-n") { Description = "Job number." };
        public CliOption<bool> ShowLastSuccessful { get; } =
            new("--last-successful", "-l") { Description = "Show last successful build." };
        public CliOption<string> Commit { get; } =
            new("--commit", "-c") { Description = "List build at this commit." };
        public CliOption<bool> ShowArtifacts { get; } =
            new("--artifacts", "-a") { Description = "Show job artifacts on server." };
        public CliOption<string> OutputPath { get; } =
            new("--output", "-o") { Description = "The path where output will be placed." };
        public CliOption<string> OutputRoot { get; } =
            new("--output-root") { Description = "The root directory where output will be placed. A subdirectory named by job and build number will be created within this to store the output." };
        public CliOption<bool> Unzip { get; } =
            new("--unzip", "-u") { Description = "Unzip copied artifacts" };
        public CliOption<string> ContentPath { get; } =
            new("--ContentPath", "-p") { Description = "Relative product zip path. Default is artifact/bin/Product/*zip*/Product.zip" };

        public ParseResult Result;

        public CIJobsRootCommand(string[] args) : base("Continuous integration build jobs tool")
        {
            List<string> errors = new();

            CliCommand listCommand = new("list", "List jobs on dotnet-ci.cloudapp.net for the repo.")
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

            listCommand.SetAction((result, cancellationToken) =>
            {
                int jobNumber = result.GetValue(JobNumber);
                bool showLastSuccessful = result.GetValue(ShowLastSuccessful);
                string commit = result.GetValue(Commit);

                if (result.GetResult(JobNumber) == null)
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

                return TryExecuteWithContextAsync(result, "list");
            });

            Subcommands.Add(listCommand);

            CliCommand copyCommand = new("copy", @"Copies job artifacts from dotnet-ci.cloudapp.net. This
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

            copyCommand.SetAction((result, cancellationToken) =>
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

                return TryExecuteWithContextAsync(result, "copy");
            });

            Subcommands.Add(copyCommand);

            async Task<int> TryExecuteWithContextAsync(ParseResult result, string name)
            {
                Result = result;
                try
                {
                    if (errors.Count > 0)
                    {
                        throw new Exception(string.Join(Environment.NewLine, errors));
                    }

                    return await new Program(this).RunAsync(name);
                }
                catch (Exception e)
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine("Error: " + e.Message);
                    Console.Error.WriteLine(e.ToString());

                    Console.ResetColor();

                    return 1;
                }
            }
        }
    }
}
