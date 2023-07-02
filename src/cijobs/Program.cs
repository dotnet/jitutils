// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

///////////////////////////////////////////////////////////////////////////////
//
//  cijobs - Continuous integration build jobs tool enables the listing of
//  jobs built in the CI system as well as downloading their artifacts. This
//  functionality allows for the speed up of some common dev tasks by taking
//  advantage of work being done in the cloud.
//
//  Scenario 1: Start new work. When beginning a new set of changes, listing
//  job status can help you find a commit to start your work from. The tool
//  answers questions like "are the CentOS build jobs passing?" and "what was
//  the commit hash for the last successful Tizen arm32 build?"
//
//  Scenario 2: Copy artifacts to speed up development flow. The tool enables
//  developers to download builds from the cloud so that developers can avoid
//  rebuilding baseline tools on a local machine.  Need the crossgen tool for
//  the baseline commit for OSX diffs?  Cijobs makes this easy to copy to your
//  system.
//

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ManagedCodeGen
{
    internal sealed class Program
    {
        private readonly CIJobsRootCommand _command;
        private string _repoName;
        private string _branchName;
        private string _commit;
        private string _jobName;
        private int _jobNumber;
        private string _contentPath;
        private bool _showLastSuccessful;

        public Program(CIJobsRootCommand command)
        {
            _command = command;

            _repoName = Get(command.RepoName);
            _branchName = Get(command.BranchName);
            _commit = Get(_command.Commit);
            _jobName = Get(command.JobName);
            _jobNumber = Get(command.JobNumber);
            _showLastSuccessful = Get(command.ShowLastSuccessful);
            _contentPath = Get(command.ContentPath) ?? "artifact/bin/Product/*zip*/Product.zip";
        }

        // Main entry point.  Simply set up a httpClient to access the CI
        // and switch on the command to invoke underlying logic.
        public async Task<int> RunAsync(string name)
        {
            CIClient cic = new(Get(_command.Server));

            if (name == "list")
                return await ListAsync(cic);

            return await CopyAsync(cic);
        }

        private T Get<T>(CliOption<T> option) => _command.Result.GetValue(option);

        private static int Main(string[] args) =>
            new CliConfiguration(new CIJobsRootCommand(args).UseVersion())
            {
                EnableParseErrorReporting = true
            }.Invoke(args);

        // List jobs and their details from the given project on .NETCI Jenkins instance.
        // List functionality:
        //    if --job is not specified, ListOption.Jobs, list jobs under branch.
        //        (default is "master" set in root command).
        //    if --job is specified, ListOption.Builds, list job builds with details.
        //        --number, --last_successful, or -commit can be used to specify specific job
        //
        private async Task<int> ListAsync(CIClient cic)
        {
            if (_jobName == null)
            {
                var jobs = await cic.GetProductJobs(_repoName, _branchName);
                string matchPattern = Get(_command.MatchPattern);

                if (matchPattern != null)
                {
                    var pattern = new Regex(matchPattern);
                    PrettyJobs(jobs.Where(x => pattern.IsMatch(x.name)));
                }
                else
                {
                    PrettyJobs(jobs);
                }
            }
            else
            {
                var builds = await cic.GetJobBuilds(_repoName, _branchName,
                                                    _jobName, _showLastSuccessful,
                                                    _jobNumber, _commit);

                if (_showLastSuccessful && builds.Any())
                {
                    Console.WriteLine("Last successful build:");
                }

                PrettyBuilds(builds, Get(_command.ShowArtifacts));
            }

            return 0;
        }

        private static void PrettyJobs(IEnumerable<Job> jobs)
        {
            foreach (var job in jobs)
            {
                Console.WriteLine("job {0}", job.name);
            }
        }

        private static void PrettyBuilds(IEnumerable<Build> buildList, bool artifacts = false)
        {
            foreach (var build in buildList)
            {
                var result = build.info.result;
                if (result != null)
                {
                    Console.Write("build {0} - {1} : ", build.number, result);
                    PrettyBuildInfo(build.info, artifacts);
                }
            }
        }

        private static void PrettyBuildInfo(BuildInfo info, bool artifacts = false)
        {
            var actions = info.actions.Where(x => x.lastBuiltRevision.SHA1 != null);

            if (actions.Any())
            {
                var action = actions.First();
                Console.WriteLine("commit {0}", action.lastBuiltRevision.SHA1);
            }
            else
            {
                Console.WriteLine("");
            }

            if (artifacts)
            {
                Console.WriteLine("    artifacts:");
                foreach (var artifact in info.artifacts)
                {
                    Console.WriteLine("       {0}", artifact.relativePath);
                }
            }
        }

        // Based on the config, copy down the artifacts for the referenced job.
        // Today this is just the product bits.  This code also knows how to install
        // the bits into the asmdiff.json config file.
        public async Task<int> CopyAsync(CIClient cic)
        {
            if (_showLastSuccessful)
            {
                // Query last successful build and extract the number.
                var builds = await cic.GetJobBuilds(_repoName, _branchName, _jobName, true, 0, null);

                if (!builds.Any())
                {
                    Console.WriteLine("Last successful not found on server.");
                    return -1;
                }

                Build lastSuccess = builds.First();
                _jobNumber = lastSuccess.number;
            }
            else if (_commit != null)
            {
                var builds = await cic.GetJobBuilds(_repoName, _branchName, _jobName, false, 0, _commit);

                if (!builds.Any())
                {
                    Console.WriteLine("Commit not found on server.");
                    return -1;
                }

                Build commitBuild = builds.First();
                _jobNumber = commitBuild.number;
            }

            string outputPath;
            string outputRoot = Get(_command.OutputRoot);

            if (outputRoot != null)
            {
                outputPath = Get(_command.OutputPath);
            }
            else
            {
                string tag = $"{_jobName}-{_jobNumber}";
                outputPath = Path.Combine(outputRoot, tag);
            }

            if (Directory.Exists(outputPath))
            {
                Console.WriteLine("Warning: directory {0} already exists.", outputPath);
            }

            // Create directory if it doesn't exist.
            Directory.CreateDirectory(outputPath);

            // Pull down the zip file.
            await DownloadZip(cic, outputPath);

            return 0;
        }

        // Download zip file.  It's arguable that this should be in the
        private async Task DownloadZip(CIClient cic, string outputPath)
        {
            string messageString = $"job/{_repoName}/job/{_branchName}/job/{_jobName}/{_jobNumber}/{_contentPath}";

            // Copy product tools to output location.
            bool success = await cic.DownloadProduct(messageString, outputPath, _contentPath);

            if (success && Get(_command.Unzip))
            {
                // unzip archive in place.
                var zipPath = Path.Combine(outputPath, Path.GetFileName(_contentPath));
                Console.WriteLine("Unzipping: {0}", zipPath);
                ZipFile.ExtractToDirectory(zipPath, outputPath);
            }
        }
    }
}
