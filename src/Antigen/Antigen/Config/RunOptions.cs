using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Intrinsics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antigen.Config
{
    public class RunOptions
    {
        // random seed
        public int Seed = -1;

        // sets the output directory for tests
        public string OutputDirectory = "." + Path.DirectorySeparatorChar;

        // Total number of test cases (overrides number specified in each XML config file)
        public long NumTestCases;

        // Duration to execute tests for (overrides number specified in each XML config file)
        public int RunDuration;

        // Percent of time to execute baseline
        public double ExecuteBaseline;

        [JsonIgnore]
        public string CoreRun = null;

        public List<ConfigOptions> Configs;

        public List<DotnetEnvVarGroup> BaselineEnvVars;

        public List<DotnetEnvVarGroup> TestEnvVars;

        internal static RunOptions Initialize()
        {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string antiGenConfig = Path.Combine(currentDirectory, "Config", "antigen.json");
            Debug.Assert(File.Exists(antiGenConfig));

            JsonSerializerOptions options = new()
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            };

            var runOption = JsonSerializer.Deserialize<RunOptions>(File.ReadAllText(antiGenConfig), options);
            EnvVarOptions.Initialize(runOption.BaselineEnvVars, runOption.TestEnvVars);

            return runOption;
        }
    }
}
