// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExecutionEngine
{
    internal struct RunResult
    {
        public int HashCode { get; private set; }
        public string? Error { get; private set; }
        public bool IsTimeout { get; private set; }
        public bool IsJitAssert { get; private set; }

        private RunResult(int hashCode, string? error, bool isTimeout, bool isJitAssert)
        {
            HashCode = hashCode;
            Error = error;
            IsTimeout = isTimeout;
            IsJitAssert = isJitAssert;
        }

        internal static RunResult SuccessResult(int hashCode) => new RunResult(hashCode, null, false, false);
        internal static RunResult ErrorResult(string error) => new RunResult(0, error, false, false);
        internal static RunResult TimeoutResult() => new RunResult(0, null, true, false);
        internal static RunResult JitAssertionResult(string error) => new RunResult(0, error, true, true);
    }
}
