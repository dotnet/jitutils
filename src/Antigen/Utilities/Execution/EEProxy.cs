// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using Antigen.Config;
using ExecutionEngine;
using Newtonsoft.Json;
using Utils;

namespace Antigen.Execution
{
    // Each instance of class represents a corresponding instance of ExecutionEngine
    public class EEProxy
    {
        public readonly Process _process;
        public Stopwatch LastUsedTime { get; } = new Stopwatch();
        private ReadOnlyDictionary<string, string> _envVars;
        private IReadOnlyList<Tuple<string, string>> _envVarsList;
        private int _testCaseExecutionCount = 0;

        private static readonly TimeSpan s_inactivityPeriod = TimeSpan.FromMinutes(3);
        private const int RecycleCount = 100;
        private const int TimeoutInSeconds = 10;

        private EEProxy(string host, string executionEngine, Dictionary<string, string> envVars)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = host,
                Arguments = $"{executionEngine} {Environment.ProcessId}", // Optional: arguments for the process
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            _process = new Process
            {
                StartInfo = startInfo
            };

            SetEnvironmentVariables(startInfo, envVars);
            _process.Start();
            _testCaseExecutionCount = 0;
        }

        private void SetEnvironmentVariables(ProcessStartInfo startInfo, Dictionary<string, string> envVars)
        {
            envVars["DOTNET_TieredCompilation"] = "0";
            envVars["DOTNET_JitThrowOnAssertionFailure"] = "1";
            envVars["DOTNET_LegacyExceptionHandling"] = "1";

            foreach (var envVar in envVars)
            {
                startInfo.Environment[envVar.Key] = envVar.Value;
            }

            _envVars = new ReadOnlyDictionary<string, string>(envVars);
            _envVarsList = envVars.Select(kv => new Tuple<string, string>(kv.Key, kv.Value)).ToList();
        }

        public override string ToString()
        {
            string result = "";
            if (_process.HasExited)
            {
                result += "~";
            }
            else
            {
                result += _process.Id;
            }
            result += $"  [{LastUsedTime.Elapsed}]";
            return result;
        }

        public static EEProxy GetInstance(string host, string executionEngine, Dictionary<string, string> envVars)
        {
            if (!File.Exists(host) || !File.Exists(executionEngine))
            {
                throw new FileNotFoundException($"'{host}' or '{executionEngine}' not found.");
            }

            return new EEProxy(host, executionEngine, envVars);
        }

        public Response Execute(Request request)
        {
            _process.StandardInput.WriteLine(JsonConvert.SerializeObject(request));

            bool killed = false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutInSeconds));
            using var reg = cts.Token.Register(() => { killed = true; Kill(); });
            StringBuilder responseReader = new StringBuilder();
            while (true)
            {
                string line = _process.StandardOutput.ReadLine();
                if ((line == null) || (line == "Done"))
                {
                    break;
                }
                responseReader.AppendLine(line);
            }
            _testCaseExecutionCount++;
            LastUsedTime.Restart();

            if (killed)
            {
                return new Response
                {
                    IsTimeout = true
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<Response>(responseReader.ToString());
            }
            catch (JsonException)
            {
                return new()
                {
                    HasCrashed = true
                };
            }
        }

        /// <summary>
        ///     Returns environment variables used by this process.
        /// </summary>
        /// <returns></returns>
        public IReadOnlyList<Tuple<string, string>> GetEnvironmentVariables()
        {
            return _envVarsList;
        }

        public void Kill()
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.Dispose();
            }
        }

        /// <summary>
        ///     Recycle if it ran around {RecycleCount} test cases or has been inactive for 3 minutes. 
        /// </summary>
        /// <returns></returns>
        public bool ShouldRecycle()
        {
            if (_testCaseExecutionCount >= RecycleCount)
            {
                return true;
            }
            if (LastUsedTime.Elapsed > s_inactivityPeriod)
            {
                return true;
            }
            return false;
        }

        public bool IsRunning => (_process != null) && !_process.HasExited;
    }
}
