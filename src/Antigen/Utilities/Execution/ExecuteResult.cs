// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace Antigen.Execution
{
    public enum RunOutcome
    {
        Success,
        Timeout,
        AssertionFailure,
        OutputMismatch,
        OtherError,
        CompilationError,
    }

    public struct ExecuteResult
    {
        public string OtherErrorMessage { get; private set; }
        public string AssertionMessage { get; private set; }
        public string ShortAssertionText { get; private set; }
        public RunOutcome Result { get; private set; }
        public IReadOnlyList<Tuple<string, string>> EnvVars { get; private set; }

        public static ExecuteResult GetSuccessResult()
        {
            return new ExecuteResult(RunOutcome.Success, null);
        }

        public static ExecuteResult GetOtherErrorResult(string errorMessage, IReadOnlyList<Tuple<string, string>> envVars)
        {
            return new ExecuteResult(RunOutcome.OtherError, null, errorMessage, envVars);
        }

        public static ExecuteResult GetTimeoutResult()
        {
            return new ExecuteResult(RunOutcome.Timeout, null);
        }

        public static ExecuteResult GetAssertionFailureResult(string assertionMessage, IReadOnlyList<Tuple<string, string>> envVars)
        {
            var result = new ExecuteResult(RunOutcome.AssertionFailure, assertionMessage, null, envVars);
            result.ShortAssertionText = RslnUtilities.ParseAssertionError(assertionMessage);
            return result;
        }

        public static ExecuteResult GetOutputMismatchResult(string outputDiff, IReadOnlyList<Tuple<string, string>> envVars)
        {
            return new ExecuteResult(RunOutcome.OutputMismatch, null, outputDiff, envVars);
        }

        public static ExecuteResult GetCompilationError()
        {
            return new ExecuteResult(RunOutcome.CompilationError, null);
        }

        private ExecuteResult(RunOutcome result, string assertionMessage, string errorMessage = null, IReadOnlyList<Tuple<string, string>> envVars = null)
        {
            Result = result;
            AssertionMessage = assertionMessage;
            OtherErrorMessage = errorMessage;
            EnvVars = envVars;
        }

    }
}
