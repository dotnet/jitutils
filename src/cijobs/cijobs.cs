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
using Newtonsoft.Json;
using System.CommandLine.Invocation;

namespace ManagedCodeGen
{
    using CommandResult = Microsoft.DotNet.Cli.Utils.CommandResult;
    internal class cijobs
    {
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
            public void ValidateCopy()
            {
                if (!Uri.IsWellFormedUriString(Server, UriKind.Absolute))
                {
                    ReportError($"Invalid uri: {Server}.");
                }

                if (Job == null) 
                {
                    ReportError("Must have --job <name> for copy.");
                }
                
                if (Number == 0 && !LastSuccessful && Commit == null)
                {
                    ReportError("Must have --number <num>, --last_successful, or --commit <commit> for copy.");
                }

                if (Convert.ToInt32(Number != 0) + Convert.ToInt32(LastSuccessful) + Convert.ToInt32(Commit != null) > 1)
                {
                    ReportError("Must have only one of --number <num>, --last_successful, and --commit <commit> for copy.");
                }

                if ((OutputPath == null) && (OutputRoot == null))
                {
                    ReportError("Must specify either --output <path> or --output_root <path> for copy.");
                }

                if ((OutputPath != null) && (OutputRoot != null))
                {
                    ReportError("Must specify only one of --output <path> or --output_root <path>.");
                }

                if (ContentPath == null)
                {
                    ContentPath = "artifact/bin/Product/*zip*/Product.zip";
                }
            }

            public void ValidateList()
            {
                if (!Uri.IsWellFormedUriString(Server, UriKind.Absolute))
                {
                    ReportError($"Invalid uri: {Server}.");
                }

                if (Job != null)
                {
                    DoListOption = ListOption.Builds;

                    if (Convert.ToInt32(Number != 0) + Convert.ToInt32(LastSuccessful) + Convert.ToInt32(Commit != null) > 1)
                    {
                        ReportError("Must have at most one of --number <num>, --last_successful, and --commit <commit> for list.");
                    }

                    if (Match != String.Empty)
                    {
                        ReportError("Match pattern not valid with --job");
                    }
                }
                else
                {
                    DoListOption = ListOption.Jobs;

                    if (Number != 0)
                    {
                        ReportError("Must select --job <name> to specify --number <num>.");
                    }

                    if (LastSuccessful)
                    {
                        ReportError("Must select --job <name> to specify --last_successful.");
                    }

                    if (Commit != null)
                    {
                        ReportError("Must select --job <name> to specify --commit <commit>.");
                    }

                    if (Artifacts)
                    {
                        ReportError("Must select --job <name> to specify --artifacts.");
                    }
                }
            }

            void ReportError(string message)
            {
                Console.WriteLine(message);
                Error = true;
            }

            public Command DoCommand { get; set; }
            public ListOption DoListOption { get; set; }
            public string Job { get; set; }
            public string Server { get; set; }
            public string ContentPath { get; set; }
            public int Number { get; set; }
            public string Match { get; set; }
            public string Branch { get; set; }
            public string Repo { get; set; }
            public bool LastSuccessful { get; set; }
            public string Commit { get; set; }
            public bool DoUnzip { get; set; }
            public string OutputPath { get; set; }
            public string OutputRoot { get; set; }
            public bool Artifacts { get; set; }
            public bool Error { get; set; }
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
            Command listCommand = new Command("list");
            {
                listCommand.Description = "List jobs on dotnet-ci.cloudapp.net for the repo.";

                Option serverOption = new Option("--server", "Url of the server. Defaults to http://ci.dot.net/", new Argument<string>("http://ci.dot.net/"));
                serverOption.AddAlias("-s");
                Option jobOption = new Option("--job", "Name of the job.", new Argument<string>());
                jobOption.AddAlias("-j");
                Option branchOption = new Option("--branch", "Name of the branch (default is master).", new Argument<string>("master"));
                branchOption.AddAlias("-b");
                Option repoOption = new Option("--repo", "Name of the repo(e.g.dotnet_corefx or dotnet_coreclr). Default is dotnet_coreclr.", new Argument<string>("dotnet_coreclr"));
                repoOption.AddAlias("-r");
                Option matchOption = new Option("--match", "Regex pattern used to select jobs output.", new Argument<string>(String.Empty));
                matchOption.AddAlias("-m");
                Option numberOption = new Option("--number", "Job number.", new Argument<int>());
                numberOption.AddAlias("-n");
                Option lastOption = new Option("--last-successful", "List last successful build.", new Argument<bool>());
                lastOption.AddAlias("-l");
                Option commitOption = new Option("--commit", "List build at this commit.", new Argument<string>());
                commitOption.AddAlias("-c");
                Option artifactsOption = new Option("--artifacts", "List job artifacts on server.", new Argument<bool>());
                artifactsOption.AddAlias("-a");

                listCommand.AddOption(serverOption);
                listCommand.AddOption(jobOption);
                listCommand.AddOption(branchOption);
                listCommand.AddOption(repoOption);
                listCommand.AddOption(matchOption);
                listCommand.AddOption(numberOption);
                listCommand.AddOption(lastOption);
                listCommand.AddOption(commitOption);
                listCommand.AddOption(artifactsOption);

                listCommand.Handler = CommandHandler.Create<Config>((config) =>
                {
                    config.ValidateList();

                    if (config.Error)
                    {
                        return -1;
                    }

                    CIClient cic = new CIClient(config);
                    ListCommand.List(cic, config).Wait();

                    return 0;
                });
            }

            Command copyCommand = new Command("copy");
            {
                copyCommand.Description = "Copies job artifacts from dotnet-ci.cloudapp.net. "
                    + "This command copies a zip of artifacts from a repo (defaulted to dotnet_coreclr). "
                    + "The default location of the zips is the Product sub-directory, though "
                    + "that can be changed using the ContentPath(p) parameter";

                Option serverOption = new Option("--server", "Url of the server. Defaults to http://ci.dot.net/", new Argument<string>("http://ci.dot.net/"));
                serverOption.AddAlias("-s");
                Option jobOption = new Option("--job", "Name of the job.", new Argument<string>());
                jobOption.AddAlias("-j");
                Option branchOption = new Option("--branch", "Name of the branch (default is master).", new Argument<string>("master"));
                branchOption.AddAlias("-b");
                Option repoOption = new Option("--repo", "Name of the repo(e.g.dotnet_corefx or dotnet_coreclr). Default is dotnet_coreclr.", new Argument<string>("dotnet_coreclr"));
                repoOption.AddAlias("-r");
                Option numberOption = new Option("--number", "Job number.", new Argument<int>());
                numberOption.AddAlias("-n");
                Option lastOption = new Option("--last-successful", "List last successful build.", new Argument<bool>());
                lastOption.AddAlias("-l");
                Option commitOption = new Option("--commit", "List build at this commit.", new Argument<string>());
                commitOption.AddAlias("-c");
                Option outputOption = new Option("--output", "The path where output will be placed.", new Argument<string>());
                outputOption.AddAlias("-o");
                Option outputRootOption = new Option("--output-root", "The root directory where output will be placed. A subdirectory named by job and build number will be created within this to store the output.", new Argument<string>());
                outputRootOption.AddAlias("-or");
                Option unzipOption = new Option("--unzip", "Unzip copied artifacts", new Argument<bool>());
                unzipOption.AddAlias("-u");
                Option contentOption = new Option("--content-path", "Relative product zip path. Default is artifact/bin/Product/*zip*/Product.zip", new Argument<string>());
                contentOption.AddAlias("-p");

                copyCommand.AddOption(serverOption);
                copyCommand.AddOption(jobOption);
                copyCommand.AddOption(branchOption);
                copyCommand.AddOption(repoOption);
                copyCommand.AddOption(numberOption);
                copyCommand.AddOption(lastOption);
                copyCommand.AddOption(commitOption);
                copyCommand.AddOption(outputOption);
                copyCommand.AddOption(outputRootOption);
                copyCommand.AddOption(unzipOption);
                copyCommand.AddOption(contentOption);

                copyCommand.Handler = CommandHandler.Create<Config>((config) =>
                {
                    config.ValidateCopy();

                    if (config.Error)
                    {
                        return -1;
                    }

                    CIClient cic = new CIClient(config);
                    CopyCommand.Copy(cic, config).Wait();

                    return 0;
                });
            }

            RootCommand rootCommand = new RootCommand();
            rootCommand.AddCommand(copyCommand);
            rootCommand.AddCommand(listCommand);

            return rootCommand.InvokeAsync(args).Result;
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
                            config.Repo, config.Branch, config.Job, config.Number, contentPath);

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
                            var jobs = await cic.GetProductJobs(config.Repo, config.Branch);

                            if (config.Match != null)
                            {
                                var pattern = new Regex(config.Match);
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
                            var builds = await cic.GetJobBuilds(config.Repo, config.Branch,
                                                                config.Job, config.LastSuccessful,
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
                    var builds = await cic.GetJobBuilds(config.Repo, config.Branch, config.Job, true, 0, null);
                    
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
                    var builds = await cic.GetJobBuilds(config.Repo, config.Branch, config.Job, false, 0, config.Commit);

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
                    string tag = String.Format("{0}-{1}", config.Job, config.Number);
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
