// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExecutionEngine
{
    internal class Program
    {
        private static DynamicAssemblyLoader s_loader = new();
        private static int s_parentProcessId;
        private const int ALCRecycleCount = 100;

        static async Task Main(string[] args)
        {
            if (args.Length != 1)
            {
                throw new ArgumentException("Expected exactly one argument");
            }

            s_parentProcessId = int.Parse(args[0]);

            // Start a background thread to monitor the parent process
            Task monitorTask = Task.Run(() => MonitorParentProcess());
            int currentAlcCount = 0;
            while (true)
            {
                var lines = Console.ReadLine();
                if (lines == null)
                {
                    continue;
                }
                Request? request = JsonConvert.DeserializeObject<Request>(lines);
                if (request == null)
                {
                    continue;
                }

                RunResult debugResult = Run(request.Debug);
                RunResult releaseResult = Run(request.Release);


                Response response = new Response()
                {
                    DebugOutput = debugResult.HashCode,
                    DebugError = debugResult.Error,
                    ReleaseOutput = releaseResult.HashCode,
                    ReleaseError = releaseResult.Error
                };

                if (debugResult.IsJitAssert || releaseResult.IsJitAssert)
                {
                    response.IsJitAssert = true;
                }

                var json = JsonConvert.SerializeObject(response);
                Console.WriteLine(json);
                Console.Out.Flush();
                Console.WriteLine("Done");
                Console.Out.Flush();

                currentAlcCount++;
                if (currentAlcCount > ALCRecycleCount)
                {
                    s_loader = new DynamicAssemblyLoader();
                    currentAlcCount = 0;
                }
            }
        }

        /// <summary>
        /// Run the assmebly and return the hash code
        /// </summary>
        /// <param name="assemblyBytes"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private static RunResult Run(byte[] assemblyBytes)
        {
            int hashCode;
            var assembly = s_loader.LoadFromBytes(assemblyBytes);
            var methodInfo = assembly.GetType("TestClass").GetMethod("Main");
            var methodExec = methodInfo.CreateDelegate<Func<string[], int>>();

            // Adopted from Jakob's Fuzzlyn
            int threadID = Environment.CurrentManagedThreadId;
            List<Exception> exceptions = null;
            void FirstChanceExceptionHandler(object sender, FirstChanceExceptionEventArgs args)
            {
                if (Environment.CurrentManagedThreadId == threadID)
                {
                    (exceptions ??= new List<Exception>()).Add(args.Exception);
                }
            }

            AppDomain.CurrentDomain.FirstChanceException += FirstChanceExceptionHandler;

            try
            {
                hashCode = methodExec(null);
            }
            catch
            {
                // We consider the innermost exception the root cause and only report it.
                // Otherwise we may be confusing the viewer about what the problem is.
                // Consider for example (adapted from a real example):
                // try
                // {
                //   value = -1;
                //   FunctionThatJitAssertsInDebug();
                //   value = 1;
                // }
                // finally
                // {
                //   int.MinValue / value;
                // }
                // We are interested in the JIT assert that was hit, and not the OverflowException
                // thrown because value = 1 did not get to run.
                Exception ex = exceptions[0];

                if (ex is TypeInitializationException && ex.InnerException != null)
                {
                    ex = ex.InnerException;
                }

                if (ex is InvalidProgramException && ex.Message.Contains("JIT assert failed"))
                {
                    return RunResult.JitAssertionResult(ex.Message);
                }

                return RunResult.ErrorResult(ex.Message);
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= FirstChanceExceptionHandler;
            }

            return RunResult.SuccessResult(hashCode);
        }

        /// <summary>
        /// Monitor parent process every 10 seconds and exit if it terminates
        /// </summary>
        private static void MonitorParentProcess()
        {
            try
            {
                while (true)
                {
                    // Check if the parent process is still running
                    Process.GetProcessById(s_parentProcessId);
                    Thread.Sleep(10000); // Check every 10 seconds
                }
            }
            catch (ArgumentException)
            {
                // Parent process is no longer running
                Console.WriteLine("Parent process terminated. Exiting...");
                Environment.Exit(0);
            }
        }
    }
}
