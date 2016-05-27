// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

///////////////////////////////////////////////////////////////////////////////
//
//  cijobs - Continuious integration build jobs tool enables the listing of
//  jobs built in the CI system as well as downloading their artifacts. This
//  functionality allows for the speed up of some common dev tasks but taking
//  advantage of work being done in the cloud.
//
//  Scenario 1: Start new work. When beginning a new set of changes, listing 
//  job status can help you find a commit to start your work from. The tool
//  answers questions like "are the CentOS build jobs passing?" and "what was
//  the commit hash for the last successful tizen arm32 build?"
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
using System.Threading.Tasks;
using System.CommandLine;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO.Compression;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            Builds,
            Number
        }

        // Define options to be parsed 
        public class Config
        {
            private ArgumentSyntax _syntaxResult;
            private Command _command = Command.List;
            private ListOption _listOption = ListOption.Invalid;
            private string _jobName;
            private string _forkUrl;
            private int _number = 0;
            private string _matchPattern = String.Empty;
            private string _coreclrBranchName = "master";
            private string _privateBranchName;
            private bool _lastSuccessful = false;
            private bool _unzip = false;
            private string _outputPath;
            private bool _artifacts = false;

            public Config(string[] args)
            {
                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    // NOTE!!! - Commands and their options are ordered.  Moving an option out of line
                    // could move it to another command.  Take a careful look at how they're organized
                    // before changing.
                    syntax.DefineCommand("list", ref _command, Command.List, 
                        "List jobs on dotnet-ci.cloudapp.net for dotnet_coreclr.");
                    syntax.DefineOption("j|job", ref _jobName, "Name of the job.");
                    syntax.DefineOption("b|branch", ref _coreclrBranchName, 
                        "Name of the branch (dotnet/coreclr, def. is master).");
                    syntax.DefineOption("m|match", ref _matchPattern, 
                        "Regex pattern used to select jobs output.");
                    syntax.DefineOption("n|number", ref _number, "Job number.");
                    syntax.DefineOption("l|last_successful", ref _lastSuccessful, 
                        "List last successful build.");
                    syntax.DefineOption("a|artifacts", ref _artifacts, "List job artifacts on server.");

                    syntax.DefineCommand("copy", ref _command, Command.Copy, 
                        "Copies job artifacts from dotnet-ci.cloudapp.net. " 
                        + "Currently hardcoded to dotnet_coreclr, this command " 
                        + "copies a zip of the artifacts under the Product sub-directory "
                        + "that is the result of a build.");
                    syntax.DefineOption("j|job", ref _jobName, "Name of the job.");
                    syntax.DefineOption("n|number", ref _number, "Job number.");
                    syntax.DefineOption("l|last_successful", ref _lastSuccessful, 
                        "Copy last successful build.");
                    syntax.DefineOption("b|branch", ref _coreclrBranchName, 
                        "Name of branch  (dotnet_coreclr, def. is master)..");
                    syntax.DefineOption("o|output", ref _outputPath, "Output path.");
                    syntax.DefineOption("u|unzip", ref _unzip, "Unzip copied artifacts");
                });

                // Run validation code on parsed input to ensure we have a sensible scenario.
                validate();
            }

            private void validate()
            {
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
                
                if (_number == 0 && !_lastSuccessful)
                {
                    _syntaxResult.ReportError("Must have --number <num> or --last_successful for copy.");
                }
                
                if (_outputPath == null)
                {
                    _syntaxResult.ReportError("Must have --output <path> for copy.");
                }
            }

            private void validateList()
            {
                if (_jobName != null)
                {
                    _listOption = ListOption.Builds;

                    if (_matchPattern != String.Empty)
                    {
                        _syntaxResult.ReportError("Match pattern not valid with --job");
                    }
                }
                else
                {
                    _listOption = ListOption.Jobs;
                }
            }

            public Command DoCommand { get { return _command; } }
            public ListOption DoListOption { get { return _listOption; } }
            public string JobName { get { return _jobName; } }
            public int Number { get { return _number; } set { this._number = value; } }
            public string MatchPattern { get { return _matchPattern; } }
            public string CoreclrBranchName { get { return _coreclrBranchName; } }
            public bool LastSuccessful { get { return _lastSuccessful; } }
            public bool DoUnzip { get { return _unzip; } }
            public string OutputPath { get { return _outputPath; } }
            public bool Artifacts { get { return _artifacts; } }
        }

        // The following block of simple structs maps to the data extracted from the CI system as json.
        // This allows to map it directly into C# and access it.

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
                        Console.WriteLine("super bad!  why no command!");
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
                _client.BaseAddress = new Uri("http://dotnet-ci.cloudapp.net/");
                _client.DefaultRequestHeaders.Accept.Clear();
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            public async Task<bool> DownloadProduct(Config config, string outputPath)
            {
                string messageString
                        = String.Format("job/dotnet_coreclr/job/{0}/job/{1}/{2}/artifact/bin/Product/*zip*/Product.zip",
                            config.CoreclrBranchName, config.JobName, config.Number);

                 Console.WriteLine("Downloading: {0}", messageString);

                 HttpResponseMessage response = await _client.GetAsync(messageString);

                 bool downloaded = false;

                 if (response.IsSuccessStatusCode)
                 {
                    var zipPath = Path.Combine(outputPath, "Product.zip");
                    using (var outputStream = System.IO.File.Create(zipPath))
                    {
                        Stream inputStream = await response.Content.ReadAsStreamAsync();
                        inputStream.CopyTo(outputStream);
                    }
                    downloaded = true;
                 }
                 else
                 {
                     Console.WriteLine("Zip not found!");
                 }
                 
                 return downloaded;
            }

            public async Task<IEnumerable<Job>> GetProductJobs(string productName, string branchName)
            {
                string productString
                    = String.Format("job/{0}/job/{1}/api/json?&tree=jobs[name,url]",
                        productName, branchName);
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

            public async Task<IEnumerable<Build>> GetJobBuilds(string productName, string branchName, 
                string jobName, bool lastSuccessfulBuild = false)
            {
                var jobString
                    = String.Format(@"job/dotnet_coreclr/job/master/job/{0}", jobName);
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
                            = GetJobBuildInfo(jobName, lastSuccessfulNumber).Result;
                        return Enumerable.Repeat(jobBuilds.lastSuccessfulBuild, 1);
                    }
                    else
                    {
                        var builds = jobBuilds.builds;

                        var count = builds.Count();
                        for (int i = 0; i < count; i++)
                        {
                            var build = builds[i];
                            // fill in build info
                            build.info = GetJobBuildInfo(jobName, build.number).Result;
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

            public async Task<BuildInfo> GetJobBuildInfo(string jobName, int number)
            {
                string buildString = String.Format("{0}/{1}/{2}", "job/dotnet_coreclr/job/master/job",
                   jobName, number);
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
            // List jobs and their details from the dotnet_coreclr project on .NETCI Jenkins instance.
            // List functionality:
            //    if --job is not specified, ListOption.Jobs, list jobs under branch.
            //        (default is "master" set in Config).
            //    if --job is specified, ListOption.Builds, list job builds by id with details.
            //    if --job and --id is specified, ListOption.Number, list particular job instance, 
            //        status, and artifacts.
            // 
            public static async Task List(CIClient cic, Config config)
            {
                switch (config.DoListOption)
                {
                    case ListOption.Jobs:
                        {
                            var jobs = cic.GetProductJobs("dotnet_coreclr", "master").Result;

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
                            var builds = cic.GetJobBuilds("dotnet_coreclr",
                                "master", config.JobName, config.LastSuccessful);

                            if (config.LastSuccessful && builds.Result.Any())
                            {
                                Console.WriteLine("Last successful build:");    
                            }
                            
                            PrettyBuilds(builds.Result, config.Artifacts);
                        }
                        break;
                    case ListOption.Number:
                        {
                            var info = cic.GetJobBuildInfo(config.JobName, config.Number);
                            // Pretty build info
                            PrettyBuildInfo(info.Result, config.Artifacts);
                        }
                        break;
                    default:
                        {
                            Console.WriteLine("Unknown list option!");
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
                    // Querry last successful build and extract the number.
                    var builds = cic.GetJobBuilds("dotnet_coreclr",
                                "master", config.JobName, true);
                    
                    if (!builds.Result.Any())
                    {
                        Console.WriteLine("Last successful not found on server.");
                        return;
                    }
                    
                    Build lastSuccess = builds.Result.First();
                    config.Number = lastSuccess.number;
                }
                
                string tag = String.Format("{0}-{1}", config.JobName, config.Number);
                string outputPath = config.OutputPath;

                // Create directory if it doesn't exist.
                Directory.CreateDirectory(outputPath);

                // Pull down the zip file.
                DownloadZip(cic, config, outputPath).Wait();
            }

            // Download zip file.  It's arguable that this should be in the 
            private static async Task DownloadZip(CIClient cic, Config config, string outputPath)
            {
                // Copy product tools to output location. 
                bool success = cic.DownloadProduct(config, outputPath).Result;

                if (config.DoUnzip)
                {
                    // unzip archive in place.
                    var zipPath = Path.Combine(outputPath, "Product.zip");
                    ZipFile.ExtractToDirectory(zipPath, outputPath);
                }
            }
        }
    }
}
