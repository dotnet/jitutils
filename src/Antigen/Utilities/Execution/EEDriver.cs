// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Collections.Generic;
using ExecutionEngine;

namespace Antigen.Execution
{
    // Driver that creates EEProxy instances and responsible for its lifetime.
    public class EEDriver
    {
        private readonly string _hostName;
        private readonly string _executionEngine;
        private Func<Dictionary<string, string>> _envVarsGen;

        public static EEDriver GetInstance(string host, string executionEngine, Func<Dictionary<string, string>> envVarGen)
        {
            return new EEDriver(host, executionEngine, envVarGen);
        }

        private EEDriver(string host, string executionEngine, Func<Dictionary<string, string>> envVarGen)
        {
            _hostName = host;
            _executionEngine = executionEngine;
            _envVarsGen = envVarGen;

            AppDomain.CurrentDomain.ProcessExit += (sender, args) => TerminateAllProxys();
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => TerminateAllProxys();
        }

        private void TerminateAllProxys()
        {
            for (int i = 0; i < Proxys.Count; i++)
            {
                Proxys[i].Dispose();
            }
        }

        public List<EEProxy> Proxys { get; } = [];

        private EEProxy Get()
        {
            lock(Proxys)
            {
                if (Proxys.Count > 0)
                {
                    int leastUsedIndex = 0;
                    for (int i = 1; i < Proxys.Count; i++)
                    {
                        if (Proxys[leastUsedIndex].LastUsedTime.Elapsed > Proxys[i].LastUsedTime.Elapsed)
                        {
                            leastUsedIndex = i;
                        }
                    }
                    var proxy = Proxys[leastUsedIndex];
                    Proxys[leastUsedIndex] = Proxys[Proxys.Count - 1];
                    Proxys.RemoveAt(Proxys.Count - 1);
                    if (proxy.IsRunning)
                    {
                        return proxy;
                    }
                }
            }

            while (true)
            {
                var envVars = _envVarsGen();
                var proxy = EEProxy.GetInstance(_hostName, _executionEngine, envVars);
                if (proxy.IsRunning)
                {
                    return proxy;
                }
            }
        }

        private void Return(EEProxy proxy)
        {
            List<EEProxy> toRemove;
            lock(Proxys)
            {
                toRemove = Proxys.Where(p => p.ShouldRecycle()).ToList();
                Proxys.RemoveAll(toRemove.Contains);
                Proxys.Add(proxy);
            }

            foreach (var p in toRemove)
            {
                p.Dispose();
            }
        }

        /// <summary>
        ///     Execute the compiled assembly.
        /// </summary>
        /// <param name="compileResult"></param>
        /// <returns></returns>
        internal Response Execute(Request request)
        {
            EEProxy proxy = null;
            try
            {
                proxy = Get();
                var response = proxy.Execute(request);
                if (response == null || response.HasCrashed || response.IsTimeout)
                {
                    proxy = null;
                }
                else if (response.IsJitAssert || (response.DebugOutput != response.ReleaseOutput))
                {
                    response.EnvironmentVariables = proxy.GetEnvironmentVariables();
                }
                return response;
            }
            finally
            {
                if (proxy != null)
                {
                    Return(proxy);
                }
            }
        }

    }
}
