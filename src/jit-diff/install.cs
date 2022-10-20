﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace ManagedCodeGen
{
    public partial class jitdiff
    {
        public static int InstallCommand(Config config)
        {
            var configFilePath = Path.Combine(config.JitUtilsRoot, s_configFileName);
            string configJson = File.ReadAllText(configFilePath);
            var jObj = JsonObject.Parse(configJson);

            if ((jObj[s_configFileRootKey] == null) || (jObj[s_configFileRootKey]["tools"] == null))
            {
                Console.Error.WriteLine("\"install\" doesn't know how to add the \"" + s_configFileRootKey + "\":\"tools\" section to the config file");
                return -1;
            }

            if ((config.PlatformMoniker == null) || (GetOSArtifactDirComponent(config.PlatformMoniker) == null))
            {
                return -1;
            }

            var tools = (JsonArray)jObj[s_configFileRootKey]["tools"];

            // Early out if the tool is already installed. We can only do this if we're not doing
            // "--last_successful", in which case we don't know what the build number (and hence
            // tag) is.
            string tag = null;
            if (!config.DoLastSucessful)
            {
                tag = String.Format("{0}-{1}", config.JobName, config.Number);
                if (tools.Where(x => (string)x["tag"] == tag).Any())
                {
                    Console.Error.WriteLine("{0} is already installed in the " + s_configFileName + ". Remove before re-install.", tag);
                    return -1;
                }
            }

            string toolPath = Path.Combine(config.JitUtilsRoot, "tools");

            // Issue cijobs command to download bits            
            List<string> cijobsArgs = new List<string>();

            cijobsArgs.Add("copy");

            cijobsArgs.Add("--job");
            cijobsArgs.Add(config.JobName);

            if (config.BranchName != null)
            {
                cijobsArgs.Add("--branch");
                cijobsArgs.Add(config.BranchName);
            }

            if (config.DoLastSucessful)
            {
                cijobsArgs.Add("--last_successful");
            }
            else
            {
                cijobsArgs.Add("--number");
                cijobsArgs.Add(config.Number);
            }

            cijobsArgs.Add("--unzip");

            cijobsArgs.Add("--output_root");
            cijobsArgs.Add(toolPath);

            if (config.Verbose)
            {
                Console.WriteLine("Command: {0} {1}", "cijobs", String.Join(" ", cijobsArgs));
            }

            ProcessResult result = Utility.ExecuteProcess("cijobs", cijobsArgs);

            if (result.ExitCode != 0)
            {
                Console.Error.WriteLine("cijobs command returned with {0} failures", result.ExitCode);
                return result.ExitCode;
            }

            // There is a convention that cijobs creates a directory to store the job within
            // the toolPath named:
            //      <job-name>-<version-number>
            // for example:
            //      checked_windows_nt-1234
            //
            // However, if we passed "--last_successful", we don't know that number! So, figure it out.

            if (config.DoLastSucessful)
            {
                // Find the largest numbered build with this job name.
                int maxBuildNum = -1;
                foreach (var dir in Directory.EnumerateDirectories(toolPath))
                {
                    var lastComponent = Path.GetFileName(dir);
                    Regex dirPattern = new Regex(@"(.*)-(.*)");
                    Match dirMatch = dirPattern.Match(lastComponent);
                    if (dirMatch.Success)
                    {
                        var value = dirMatch.Groups[2].Value;
                        if (int.TryParse(value, out int thisBuildNum))
                        {
                            if (thisBuildNum > maxBuildNum)
                            {
                                maxBuildNum = thisBuildNum;
                            }
                        }
                    }
                }

                if (maxBuildNum == -1)
                {
                    Console.Error.WriteLine("Error: couldn't determine last successful build directory in {0}", toolPath);
                    return -1;
                }

                string buildNum = maxBuildNum.ToString();
                tag = String.Format("{0}-{1}", config.JobName, buildNum);
            }

            toolPath = Path.Combine(toolPath, tag);

            string platformPath = Path.Combine(toolPath, "Product");
            if (!Directory.Exists(platformPath))
            {
                Console.Error.WriteLine("cijobs didn't create or populate directory {0}", platformPath);
                return 1;
            }

            string buildOS = GetOSArtifactDirComponent(config.PlatformMoniker).ToUpper();
            foreach (var dir in Directory.EnumerateDirectories(platformPath))
            {
                if (Path.GetFileName(dir).ToUpper().Contains(buildOS))
                {
                    tools.Add(new JsonObject
                    {
                        ["tag"] = tag,
                        ["path"] = Path.GetFullPath(dir)
                    });
                    break;
                }
            }

            // Overwrite current config.json with new data.
            using (var sw = File.CreateText(configFilePath))
            {
                var json = JsonSerializer.Serialize (jObj, new JsonSerializerOptions { WriteIndented = true });
                sw.Write(json);
            }

            return 0;
        }
    }
}

