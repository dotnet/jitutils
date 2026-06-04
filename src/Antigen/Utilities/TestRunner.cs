// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Antigen.Compilation;
using Antigen.Execution;
using ExecutionEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Utils
{
    public enum TestResult
    {
        RoslynException,
        CompileError,
        Overflow,
        DivideByZero,
        OutputMismatch,
        Assertion,
        OtherError,
        Timeout,
        Pass,
        OOM
    }


    public class TestRunner
    {
        private static TestRunner _testRunner;
        private readonly string _coreRun;
        private readonly EEDriver _driver;

        private static readonly string s_corelibPath = typeof(object).Assembly.Location;
        private static readonly MetadataReference[] s_references =
        {
             MetadataReference.CreateFromFile(s_corelibPath),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(s_corelibPath), "System.Console.dll")),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(s_corelibPath), "System.Runtime.dll")),
             MetadataReference.CreateFromFile(typeof(SyntaxTree).Assembly.Location),
             MetadataReference.CreateFromFile(typeof(CSharpSyntaxTree).Assembly.Location),
        };

        private TestRunner(EEDriver driver, string coreRun)
        {
            _coreRun = coreRun;
            _driver = driver;
        }

        public static TestRunner GetInstance(EEDriver driver, string coreRun)
        {
            if (_testRunner == null)
            {
                _testRunner = new TestRunner(driver, coreRun);
            }
            return _testRunner;
        }

        public ExecuteResult Execute(CompileResult compileResult)
        {
            if (compileResult.DebugAssembly == null || compileResult.ReleaseAssembly == null)
            {
                return ExecuteResult.GetCompilationError();
            }
            var result = _driver.Execute(new()
            {
                Debug = compileResult.DebugAssembly,
                Release = compileResult.ReleaseAssembly
            });
            if (result == null)
            {
                // can't do much
                return ExecuteResult.GetSuccessResult();
            }
            else if (result.IsJitAssert)
            {
                return ExecuteResult.GetAssertionFailureResult(GetFailureOutput(result), result.EnvironmentVariables);
            }
            else if (result.IsTimeout)
            {
                return ExecuteResult.GetTimeoutResult();
            }
            else if (!string.IsNullOrEmpty(result.DebugError) || !string.IsNullOrEmpty(result.ReleaseError))
            {
                if (result.DebugError != result.ReleaseError)
                {
                    return ExecuteResult.GetOutputMismatchResult(GetFailureOutput(result), result.EnvironmentVariables);
                }
                else
                {
                    return ExecuteResult.GetOtherErrorResult(result.DebugError, result.EnvironmentVariables);
                }
            }
            else if (result.DebugOutput != result.ReleaseOutput)
            {
                return ExecuteResult.GetOutputMismatchResult(GetFailureOutput(result), result.EnvironmentVariables);
            }
            else
            {
                return ExecuteResult.GetSuccessResult();
            }
        }

        internal string GetFailureOutput(Response response)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Debug: {response.DebugOutput}");
            sb.AppendLine(response.DebugError);
            sb.AppendLine($"Release: {response.ReleaseOutput}");
            sb.AppendLine(response.ReleaseError);
            return sb.ToString();
        }
    }
}
