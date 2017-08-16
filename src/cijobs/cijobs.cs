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
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO.Compression;
//using Microsoft.DotNet.Cli.Utils;
//using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;

namespace ManagedCodeGen
{
    internal class cijobs
    {
        // Supported commands.  List to view information from the CI system, and Copy to download artifacts.
        public enum Command
        {
            List,
            Copy
        }
        
        // List options control what level and level of detail to put out.
        // Jobs lists jobs under a product.
        // Builds lists build instances under a job.
        // Number lists a particular builds info.
        public enum ListOption
        {
            Invalid,
            Jobs,
            Builds
        }

        // Define options to be parsed 
        public class Config
        {
            private ArgumentSyntax _syntaxResult;
            private Command _command = Command.List;
            private ListOption _listOption = ListOption.Invalid;
            private string _server = "http://ci.dot.net/";
            private string _jobName;
            private string _contentPath;
            private string _repoName = "dotnet_coreclr";
            private int _number = 0;
            private string _matchPattern = String.Empty;
            private string _branchName = "master";
            private bool _lastSuccessful = false;
            private string _commit;
            private bool _unzip = false;
            private string _outputPath;
            private string _outputRoot;
            private bool _artifacts = false;

            public Config(string[] args)
            {
                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    // NOTE!!! - Commands and their options are ordered.  Moving an option out of line
                    // could move it to another command.  Take a careful look at how they're organized
                    // before changing.
                    syntax.DefineCommand("list", ref _command, Command.List, 
                        "List jobs on dotnet-ci.cloudapp.net for the repo.");
                    syntax.DefineOption("s|server", ref _server, "Url of the server. Defaults to http://ci.dot.net/");
                    syntax.DefineOption("j|job", ref _jobName, "Name of the job.");
                    syntax.DefineOption("b|branch", ref _branchName, 
                        "Name of the branch (default is master).");
                    syntax.DefineOption("r|repo", ref _repoName, 
                        "Name of the repo (e.g. dotnet_corefx or dotnet_coreclr). Default is dotnet_coreclr.");
                    syntax.DefineOption("m|match", ref _matchPattern, 
                        "Regex pattern used to select jobs output.");
                    syntax.DefineOption("n|number", ref _number, "Job number.");
                    syntax.DefineOption("l|last_successful", ref _lastSuccessful, 
                        "List last successful build.");
                    syntax.DefineOption("c|commit", ref _commit, "List build at this commit.");
                    syntax.DefineOption("a|artifacts", ref _artifacts, "List job artifacts on server.");

                    syntax.DefineCommand("copy", ref _command, Command.Copy, 
                        "Copies job artifacts from dotnet-ci.cloudapp.net. " 
                        + "This command copies a zip of artifacts from a repo (defaulted to dotnet_coreclr)." 
                        + " The default location of the zips is the Product sub-directory, though "
                        + "that can be changed using the ContentPath(p) parameter");
                    syntax.DefineOption("s|server", ref _server, "Url of the server. Defaults to http://ci.dot.net/");
                    syntax.DefineOption("j|job", ref _jobName, "Name of the job.");
                    syntax.DefineOption("n|number", ref _number, "Job number.");
                    syntax.DefineOption("l|last_successful", ref _lastSuccessful, 
                        "Copy last successful build.");
                    syntax.DefineOption("c|commit", ref _commit, "Copy this commit.");
                    syntax.DefineOption("b|branch", ref _branchName, 
                        "Name of the branch (default is master).");
                    syntax.DefineOption("r|repo", ref _repoName, 
                        "Name of the repo (e.g. dotnet_corefx or dotnet_coreclr). Default is dotnet_coreclr.");
                    syntax.DefineOption("o|output", ref _outputPath, "The path where output will be placed.");
                    syntax.DefineOption("or|output_root", ref _outputRoot,
                        "The root directory where output will be placed. A subdirectory named by job and build number will be created within this to store the output.");
                    syntax.DefineOption("u|unzip", ref _unzip, "Unzip copied artifacts");
                    syntax.DefineOption("p|ContentPath", ref _contentPath,
                        "Relative product zip path. Default is artifact/bin/Product/*zip*/Product.zip");
                });

                // Run validation code on parsed input to ensure we have a sensible scenario.
                validate();
            }

            private void validate()
            {
                if (!Uri.IsWellFormedUriString(_server, UriKind.Absolute))
                {
                    _syntaxResult.ReportError($"Invalid uri: {_server}.");
                }
                switch (_command)
                {
                    case Command.List:
                        {
                            validateList();
                        }
                        break;
                    case Command.Copy:
                        {
                            validateCopy();
                        }
                        break;
                }
            }

            private void validateCopy()
            {
                if (_jobName == null) 
                {
                    _syntaxResult.ReportError("Must have --job <name> for copy.");
                }
                
                if (_number == 0 && !_lastSuccessful && _commit == null)
                {
                    _syntaxResult.ReportError("Must have --number <num>, --last_successful, or --commit <commit> for copy.");
                }

                if (Convert.ToInt32(_number != 0) + Convert.ToInt32(_lastSuccessful) + Convert.ToInt32(_commit != null) > 1)
                {
                    _syntaxResult.ReportError("Must have only one of --number <num>, --last_successful, and --commit <commit> for copy.");
                }

                if ((_outputPath == null) && (_outputRoot == null))
                {
                    _syntaxResult.ReportError("Must specify either --output <path> or --output_root <path> for copy.");
                }

                if ((_outputPath != null) && (_outputRoot != null))
                {
                    _syntaxResult.ReportError("Must specify only one of --output <path> or --output_root <path>.");
                }

                if (_contentPath == null)
                {
                    _contentPath = "artifact/bin/Product/*zip*/Product.zip";
                }
            }

            private void validateList()
            {
                if (_jobName != null)
                {
                    _listOption = ListOption.Builds;

                    if (Convert.ToInt32(_number != 0) + Convert.ToInt32(_lastSuccessful) + Convert.ToInt32(_commit != null) > 1)
                    {
                        _syntaxResult.ReportError("Must have at most one of --number <num>, --last_successful, and --commit <commit> for list.");
                    }

                    if (_matchPattern != String.Empty)
                    {
                        _syntaxResult.ReportError("Match pattern not valid with --job");
                    }
                }
                else
                {
                    _listOption = ListOption.Jobs;

                    if (_number != 0)
                    {
                        _syntaxResult.ReportError("Must select --job <name> to specify --number <num>.");
                    }

                    if (_lastSuccessful)
                    {
                        _syntaxResult.ReportError("Must select --job <name> to specify --last_successful.");
                    }

                    if (_commit != null)
                    {
                        _syntaxResult.ReportError("Must select --job <name> to specify --commit <commit>.");
                    }

                    if (_artifacts)
                    {
                        _syntaxResult.ReportError("Must select --job <name> to specify --artifacts.");
                    }
                }
            }

            public Command DoCommand { get { return _command; } }
            public ListOption DoListOption { get { return _listOption; } }
            public string JobName { get { return _jobName; } }
            public string Server { get { return _server; } }
            public string ContentPath { get { return _contentPath; } }
            public int Number { get { return _number; } set { this._number = value; } }
            public string MatchPattern { get { return _matchPattern; } }
            public string BranchName { get { return _branchName; } }
            public string RepoName { get { return _repoName; } }
            public bool LastSuccessful { get { return _lastSuccessful; } }
            public string Commit { get { return _commit; } }
            public bool DoUnzip { get { return _unzip; } }
            public string OutputPath { get { return _outputPath; } }
            public string OutputRoot { get { return _outputRoot; } }
            public bool Artifacts { get { return _artifacts; } }
        }

        // The following block of simple structs maps to the data extracted from the CI system as json.
        // This allows to map it directly into C# and access it.

        // fields are assigned to by json deserializer
        #pragma warning disable 0649
        
        private struct Artifact
        {
            public string fileName;
            public string relativePath;
        }

        private struct Revision
        {
            public string SHA1;
        }

        private class Action
        {
            public Revision lastBuiltRevision;
        }

        private class BuildInfo
        {
            public List<Action> actions;
            public List<Artifact> artifacts;
            public string result;
        }

        private struct Job
        {
            public string name;
            public string url;
        }

        private struct ProductJobs
        {
            public List<Job> jobs;
        }

        private struct Build
        {
            public int number;
            public string url;
            public BuildInfo info;
        }

        private struct JobBuilds
        {
            public List<Build> builds;
            public Build lastSuccessfulBuild;
        }

        #pragma warning restore 0649

        // Main entry point.  Simply set up a httpClient to access the CI
        // and switch on the command to invoke underlying logic.
        private static int Main(string[] args)
        {
            Config config = new Config(args);
            int error = 0;

            CIClient cic = new CIClient(config);

            Command currentCommand = config.DoCommand;
            switch (currentCommand)
            {
                case Command.List:
                    {
                        ListCommand.List(cic, config).Wait();
                        break;
                    }
                case Command.Copy:
                    {
                        CopyCommand.Copy(cic, config).Wait();
                        break;
                    }
                default:
                    {
                        Console.Error.WriteLine("super bad!  why no command!");
                        error = 1;
                        break;
                    }
            }

            return error;
        }

        // Wrap CI httpClient with focused APIs for product, job, and build.
        // This logic is seperate from listing/copying and just extracts data.
        private class CIClient
        {
            private HttpClient _client;

            public CIClient(Config config)
            {
                _client = new HttpClient();
                _client.BaseAddress = new Uri(config.Server);
                _client.DefaultRequestHeaders.Accept.Clear();
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _client.Timeout = Timeout.InfiniteTimeSpan;
            }

            public async Task<bool> DownloadProduct(Config config, string outputPath, string contentPath)
            {
                string messageString
                        = String.Format("job/{0}/job/{1}/job/{2}/{3}/{4}",
                            config.RepoName, config.BranchName, config.JobName, config.Number, contentPath);

                 Console.WriteLine("Downloading: {0}", messageString);

                 HttpResponseMessage response = await _client.GetAsync(messageString);

                 bool downloaded = false;

                 if (response.IsSuccessStatusCode)
                 {
                    var zipPath = Path.Combine(outputPath, Path.GetFileName(contentPath));
                    using (var outputStream = System.IO.File.Create(zipPath))
                    {
                        Stream inputStream = await response.Content.ReadAsStreamAsync();
                        inputStream.CopyTo(outputStream);
                    }
                    downloaded = true;
                 }
                 else
                 {
                     Console.Error.WriteLine("Zip not found!");
                 }
                 
                 return downloaded;
            }

            public async Task<IEnumerable<Job>> GetProductJobs(string productName, string branchName)
            {
                string productString
                    = String.Format("job/{0}/job/{1}/api/json?&tree=jobs[name,url]",
                        productName, branchName);

                try
                {
                    HttpResponseMessage response = await _client.GetAsync(productString);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var productJobs = JsonConvert.DeserializeObject<ProductJobs>(json);
                        return productJobs.jobs;
                    }
                    else
                    {
                        return Enumerable.Empty<Job>();
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error enumerating jobs: {0} {1}", ex.Message, ex.InnerException.Message);
                    return Enumerable.Empty<Job>();
                }
            }

            public async Task<IEnumerable<Build>> GetJobBuilds(string productName, string branchName, 
                string jobName, bool lastSuccessfulBuild, int number, string commit)
            {
                var jobString
                    = String.Format(@"job/{0}/job/{1}/job/{2}", productName, branchName, jobName);
                var messageString
                    = String.Format("{0}/api/json?&tree=builds[number,url],lastSuccessfulBuild[number,url]",
                        jobString);
                HttpResponseMessage response = await _client.GetAsync(messageString);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var jobBuilds = JsonConvert.DeserializeObject<JobBuilds>(json);

                    if (lastSuccessfulBuild)
                    {
                        var lastSuccessfulNumber = jobBuilds.lastSuccessfulBuild.number;
                        jobBuilds.lastSuccessfulBuild.info 
                            = await GetJobBuildInfo(productName, branchName, jobName, lastSuccessfulNumber);
                        return Enumerable.Repeat(jobBuilds.lastSuccessfulBuild, 1);
                    }
                    else if (number != 0)
                    {
                        var builds = jobBuilds.builds;

                        var count = builds.Count();
                        for (int i = 0; i < count; i++)
                        {
                            var build = builds[i];
                            if (build.number == number)
                            {
                                build.info = await GetJobBuildInfo(productName, branchName, jobName, build.number);
                                return Enumerable.Repeat(build, 1);
                            }
                        }
                        return Enumerable.Empty<Build>();
                    }
                    else if (commit != null)
                    {
                        var builds = jobBuilds.builds;

                        var count = builds.Count();
                        for (int i = 0; i < count; i++)
                        {
                            var build = builds[i];
                            build.info = await GetJobBuildInfo(productName, branchName, jobName, build.number);
                            var actions = build.info.actions.Where(x => x.lastBuiltRevision.SHA1 != null);
                            foreach (var action in actions)
                            {
                                if (action.lastBuiltRevision.SHA1.Equals(commit, StringComparison.OrdinalIgnoreCase))
                                {
                                    return Enumerable.Repeat(build, 1);
                                }
                            }
                        }
                        return Enumerable.Empty<Build>();
                    }
                    else
                    {
                        var builds = jobBuilds.builds;

                        var count = builds.Count();
                        for (int i = 0; i < count; i++)
                        {
                            var build = builds[i];
                            // fill in build info
                            build.info = await GetJobBuildInfo(productName, branchName, jobName, build.number);
                            builds[i] = build;
                        }

                        return jobBuilds.builds;
                    }
                }
                else
                {
                    return Enumerable.Empty<Build>();
                }
            }

            public async Task<BuildInfo> GetJobBuildInfo(string repoName, string branchName, string jobName, int number)
            {
                string buildString = String.Format("job/{0}/job/{1}/job/{2}/{3}",
                   repoName, branchName, jobName, number);
                string buildMessage = String.Format("{0}/{1}", buildString,
                   "api/json?&tree=actions[lastBuiltRevision[SHA1]],artifacts[fileName,relativePath],result");
                HttpResponseMessage response = await _client.GetAsync(buildMessage);

                if (response.IsSuccessStatusCode)
                {
                    var buildInfoJson = await response.Content.ReadAsStringAsync();
                    var info = JsonConvert.DeserializeObject<BuildInfo>(buildInfoJson);
                    return info;
                }
                else
                {
                    return null;
                }
            }
        }

        // Implementation of the list command.
        private class ListCommand
        {
            // List jobs and their details from the given project on .NETCI Jenkins instance.
            // List functionality:
            //    if --job is not specified, ListOption.Jobs, list jobs under branch.
            //        (default is "master" set in Config).
            //    if --job is specified, ListOption.Builds, list job builds with details.
            //        --number, --last_successful, or -commit can be used to specify specific job
            // 
            public static async Task List(CIClient cic, Config config)
            {
                switch (config.DoListOption)
                {
                    case ListOption.Jobs:
                        {
                            var jobs = await cic.GetProductJobs(config.RepoName, config.BranchName);

                            if (config.MatchPattern != null)
                            {
                                var pattern = new Regex(config.MatchPattern);
                                PrettyJobs(jobs.Where(x => pattern.IsMatch(x.name)));
                            }
                            else
                            {
                                PrettyJobs(jobs);
                            }
                        }
                        break;
                    case ListOption.Builds:
                        {
                            var builds = await cic.GetJobBuilds(config.RepoName, config.BranchName,
                                                                config.JobName, config.LastSuccessful,
                                                                config.Number, config.Commit);

                            if (config.LastSuccessful && builds.Any())
                            {
                                Console.WriteLine("Last successful build:");    
                            }
                            
                            PrettyBuilds(builds, config.Artifacts);
                        }
                        break;
                    default:
                        {
                            Console.Error.WriteLine("Unknown list option!");
                        }
                        break;
                }
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
        }

        // Implementation of the copy command.
        private class CopyCommand
        {
            // Based on the config, copy down the artifacts for the referenced job.
            // Today this is just the product bits.  This code also knows how to install
            // the bits into the asmdiff.json config file.
            public static async Task Copy(CIClient cic, Config config)
            {
                if (config.LastSuccessful)
                {
                    // Query last successful build and extract the number.
                    var builds = await cic.GetJobBuilds(config.RepoName, config.BranchName, config.JobName, true, 0, null);
                    
                    if (!builds.Any())
                    {
                        Console.WriteLine("Last successful not found on server.");
                        return;
                    }
                    
                    Build lastSuccess = builds.First();
                    config.Number = lastSuccess.number;
                }
                else if (config.Commit != null)
                {
                    var builds = await cic.GetJobBuilds(config.RepoName, config.BranchName, config.JobName, false, 0, config.Commit);

                    if (!builds.Any())
                    {
                        Console.WriteLine("Commit not found on server.");
                        return;
                    }

                    Build commitBuild = builds.First();
                    config.Number = commitBuild.number;
                }

                string outputPath;

                if (config.OutputRoot == null)
                {
                    outputPath = config.OutputPath;
                }
                else
                {
                    string tag = String.Format("{0}-{1}", config.JobName, config.Number);
                    outputPath = Path.Combine(config.OutputRoot, tag);
                }

                if (Directory.Exists(outputPath))
                {
                    Console.WriteLine("Warning: directory {0} already exists.", outputPath);
                }

                // Create directory if it doesn't exist.
                Directory.CreateDirectory(outputPath);

                // Pull down the zip file.
                await DownloadZip(cic, config, outputPath, config.ContentPath);
            }

            // Download zip file.  It's arguable that this should be in the 
            private static async Task DownloadZip(CIClient cic, Config config, string outputPath, string contentPath)
            {
                // Copy product tools to output location. 
                bool success = await cic.DownloadProduct(config, outputPath, contentPath);

                if (success && config.DoUnzip)
                {
                    // unzip archive in place.
                    var zipPath = Path.Combine(outputPath, Path.GetFileName(contentPath));
                    Console.WriteLine("Unzipping: {0}", zipPath);
                    ZipFile.ExtractToDirectory(zipPath, outputPath);
                }
            }
        }
    }
}
