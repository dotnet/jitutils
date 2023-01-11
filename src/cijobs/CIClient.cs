// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedCodeGen
{
    // Wrap CI httpClient with focused APIs for product, job, and build.
    // This logic is seperate from listing/copying and just extracts data.
    internal sealed class CIClient
    {
        private HttpClient _client;

        public CIClient(string server)
        {
            _client = new HttpClient();
            _client.BaseAddress = new Uri(server);
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.Timeout = Timeout.InfiniteTimeSpan;
        }

        public async Task<bool> DownloadProduct(string messageString, string outputPath, string contentPath)
        {
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
            string productString = $"job/{productName}/job/{branchName}/api/json?&tree=jobs[name,url]";

            try
            {
                using HttpResponseMessage response = await _client.GetAsync(productString);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var productJobs = JsonSerializer.Deserialize<ProductJobs>(json);
                    return productJobs.jobs;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error enumerating jobs: {0} {1}", ex.Message, ex.InnerException.Message);
            }

            return Enumerable.Empty<Job>();
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
                var jobBuilds = JsonSerializer.Deserialize<JobBuilds>(json);

                if (lastSuccessfulBuild)
                {
                    var lastSuccessfulNumber = jobBuilds.lastSuccessfulBuild.number;
                    jobBuilds.lastSuccessfulBuild.info = await GetJobBuildInfo(productName, branchName, jobName, lastSuccessfulNumber);
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
                var info = JsonSerializer.Deserialize<BuildInfo>(buildInfoJson);
                return info;
            }
            else
            {
                return null;
            }
        }
    }
}
