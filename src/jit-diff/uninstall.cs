// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

//
//  jit-diff
//

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ManagedCodeGen
{
    public partial class JitDiff
    {
        public static int UninstallCommand(Config config)
        {
            var configFilePath = Path.Combine(config.JitUtilsRoot, s_configFileName);
            string configJson = File.ReadAllText(configFilePath);
            var jObj = JsonObject.Parse(configJson);

            if ((jObj[s_configFileRootKey] == null) || (jObj[s_configFileRootKey]["tools"] == null))
            {
                Console.Error.WriteLine("Error: no \"" + s_configFileRootKey + "\":\"tools\" section in the config file");
                return -1;
            }

            var tools = (JsonArray)jObj[s_configFileRootKey]["tools"];
            var elem = tools.Where(x => (string)x["tag"] == config.Tag);
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
            tools.Remove(jobj);

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

