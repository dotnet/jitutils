// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Antigen.Config
{
    public static class EnvVarOptions
    {
        private static List<DotnetEnvVarGroup> s_baselineGroups;
        private static readonly List<Weights<DotnetEnvVarGroup>> s_baselineGroupWeight = new();

        private static List<DotnetEnvVarGroup> s_testGroups;
        private static readonly List<Weights<DotnetEnvVarGroup>> s_testGroupWeight = new();

        private static bool s_IsArm = (RuntimeInformation.OSArchitecture == Architecture.Arm) || (RuntimeInformation.OSArchitecture == Architecture.Arm64);

        internal static void Initialize(List<DotnetEnvVarGroup> baselineEnvVars, List<DotnetEnvVarGroup> testEnvVars)
        {
            s_baselineGroups = baselineEnvVars;
            foreach (var base_group in s_baselineGroups)
            {
                s_baselineGroupWeight.Add(new Weights<DotnetEnvVarGroup>(base_group, base_group.Weight));
                base_group.PopulateWeights();
            }

            s_testGroups = testEnvVars;
            foreach (var test_group in s_testGroups)
            {
                s_testGroupWeight.Add(new Weights<DotnetEnvVarGroup>(test_group, test_group.Weight));
                test_group.PopulateWeights();
            }
        }

        /// <summary>
        ///     Returns a random EnvVarGroup depending on the weight.
        /// </summary>
        /// <returns></returns>
        private static DotnetEnvVarGroup GetRandomOsrTestGroup()
        {
            return PRNG.WeightedChoice(s_testGroupWeight.Where(tg => tg.Data.IsOsrSwitchGroup()));
        }

        /// <summary>
        ///     Returns a random EnvVarGroup depending on the weight.
        /// </summary>
        /// <returns></returns>
        private static DotnetEnvVarGroup GetRandomNonOsrTestGroup()
        {
            return PRNG.WeightedChoice(s_testGroupWeight.Where(tg => !tg.Data.IsOsrSwitchGroup()));
        }

        /// <summary>
        ///     Returns random set of baseline environment variables.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, string> BaseLineVars(bool useSve)
        {
            var envVars = new Dictionary<string, string>();
            foreach (var group in s_baselineGroups)
            {
                foreach (var variable in group.Variables)
                {
                    envVars[$"DOTNET_{variable.Name}"] = variable.Values[PRNG.Next(variable.Values.Length)];
                }
            }

            if (s_IsArm)
            {
                envVars["DOTNET_MaxVectorTBitWidth"] = "128";
            }
            if (useSve)
            {
                AddSveSwitches(ref envVars);
            }
            return envVars;
        }

        /// <summary>
        ///     Returns random set of test environment variables.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, string> TestVars(bool includeOsrSwitches, bool useSve)
        {
            var envVars = new Dictionary<string, string>();

            var defaultGroup = s_testGroups.First(tg => tg.Name == "Default");

            // default variables
            var usedEnvVars = new HashSet<string>();
            var defaultVariablesCount = PRNG.Next(1, 8);
            for (var i = 0; i < defaultVariablesCount; i++)
            {
                DotnetEnvVar envVar;

                // Avoid duplicate variables
                do
                {
                    envVar = defaultGroup.GetRandomVariable();
                } while (!usedEnvVars.Add(envVar.Name));

                envVars[$"DOTNET_{envVar.Name}"] = envVar.Values[PRNG.Next(envVar.Values.Length)];
            }

            // OSR switches
            if (includeOsrSwitches)
            {
                var osrstressGroup = GetRandomOsrTestGroup();
                // Unique OSR group found. Add all switches and move on.
                foreach (var osrSwitch in osrstressGroup.Variables)
                {
                    Debug.Assert(osrSwitch.Values.Length == 1);
                    envVars[$"DOTNET_{osrSwitch.Name}"] = osrSwitch.Values[0];
                }
            }
            else
            {
                envVars["DOTNET_TieredCompilation"] = "0";
                if (s_IsArm)
                {
                    envVars["DOTNET_MaxVectorTBitWidth"] = "128";
                    envVars["DOTNET_PreferredVectorBitWidth"] = "0";

                }
                else if (useSve)
                {
                    AddSveSwitches(ref envVars);
                }
                else
                {
                    envVars["DOTNET_PreferredVectorBitWidth"] = "512";
                }
            }

            // stress switches
            var stressVariablesCount = PRNG.Next(1, 4);
            for (var i = 0; i < stressVariablesCount; i++)
            {
                DotnetEnvVar envVar;

                // Avoid duplicate variables
                do
                {
                    var stressGroup = GetRandomNonOsrTestGroup();
                    envVar = stressGroup.GetRandomVariable();
                } while (!usedEnvVars.Add(envVar.Name));

                envVars[$"DOTNET_{envVar.Name}"] = envVar.Values[PRNG.Next(envVar.Values.Length)];
            }

            return envVars;
        }

        private static void AddSveSwitches(ref Dictionary<string, string> envVars)
        {
            string altjitName = "clrjit_universal_arm64_x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                altjitName += ".dylib";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                altjitName += ".so";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                altjitName += ".dll";
            }

            envVars["DOTNET_AltJitName"] = altjitName;
            envVars["DOTNET_AltJit"] = "*";
            envVars["DOTNET_MaxVectorTBitWidth"] = "128";
            envVars["DOTNET_PreferredVectorBitWidth"] = "0";
        }
    }

    public class DotnetEnvVar
    {
        public string Name;
        public string[] Values;
        public double Weight;

        public override string ToString()
        {
            return $"DOTNET_{Name}=[{string.Join(",", Values)}]";
        }

        public override bool Equals(object obj)
        {
            if (obj is not DotnetEnvVar otherObj)
            {
                return false;
            }

            return otherObj.Name == Name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    public class DotnetEnvVarGroup
    {
        public string Name;
        public double Weight;
        public List<DotnetEnvVar> Variables;

        /// <summary>
        ///     Weights of individual EnvVars present in this group.
        /// </summary>
        private readonly List<Weights<DotnetEnvVar>> _variableWeights = new List<Weights<DotnetEnvVar>>();

        /// <summary>
        ///     Populate list of weighted choices.
        /// </summary>
        internal void PopulateWeights()
        {
            Variables.ForEach(v => AddVariable(v));
        }

        private void AddVariable(DotnetEnvVar variable)
        {
            _variableWeights.Add(new Weights<DotnetEnvVar>(variable, variable.Weight));
        }

        public DotnetEnvVar GetRandomVariable()
        {
            return PRNG.WeightedChoice(_variableWeights);
        }

        public override string ToString()
        {
            return $"{Name}: {Variables.Count}";
        }

        public bool IsOsrSwitchGroup()
        {
            return Name.Contains("OSR") || Name.Contains("PartialCompile");
        }
    }
}
