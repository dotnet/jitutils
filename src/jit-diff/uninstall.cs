// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
//using System.Diagnostics;
using System.CommandLine;
using System.IO;
//using System.Collections.Generic;
//using System.Text.RegularExpressions;
using System.Linq;
//using Microsoft.DotNet.Cli.Utils;
//using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
//using System.Collections.Concurrent;
//using System.Threading.Tasks;
//using System.Runtime.InteropServices;

namespace ManagedCodeGen
{
    public partial class jitdiff
    {
        public static int UninstallCommand(Config config)
        {
            var configFilePath = Path.Combine(config.JitUtilsRoot, s_configFileName);
            string configJson = File.ReadAllText(configFilePath);
            var jObj = JObject.Parse(configJson);

            if ((jObj[s_configFileRootKey] == null) || (jObj[s_configFileRootKey]["tools"] == null))
            {
                Console.Error.WriteLine("Error: no \"" + s_configFileRootKey + "\":\"tools\" section in the config file");
                return -1;
            }

            var tools = (JArray)jObj[s_configFileRootKey]["tools"];
            var elem = tools.Children()
                            .Where(x => (string)x["tag"] == config.Tag);
            if (!elem.Any())
            {
                Console.WriteLine("{0} is not installed in {1}.", config.Tag, s_configFileName);
                return -1;
            }

            var jobj = elem.First();
            string path = (string)jobj["path"];
            if (path != null)
            {
                Console.WriteLine("Warning: you should remove install directory {0}.", path);

                // We could do this:
                //      Directory.Delete(path, true);
                // However, the "install" command copies down a lot more than just this directory,
                // so removing this directory still leaves around a lot of stuff.
            }

            Console.WriteLine("Removing tag {0} from config file.", config.Tag);
            jobj.Remove();

            // Overwrite current config.json with new data.
            using (var file = File.CreateText(configFilePath))
            {
                using (JsonTextWriter writer = new JsonTextWriter(file))
                {
                    writer.Formatting = Formatting.Indented;
                    jObj.WriteTo(writer);
                }
            }

            return 0;
        }
    }
}

