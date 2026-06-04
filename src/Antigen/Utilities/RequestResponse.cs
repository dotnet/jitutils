// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExecutionEngine
{
    public class Request
    {
        public byte[] Debug { get; set; }
        public byte[] Release { get; set; }
    }

    public class Response
    {
        public int DebugOutput { get; set; }
        public string? DebugError { get; set; }
        public int ReleaseOutput { get; set; }
        public string? ReleaseError { get; set; }
        public bool IsTimeout { get; set; }
        public bool IsJitAssert { get; set; }
        public bool HasCrashed { get; set; }
        public IReadOnlyList<Tuple<string, string>> EnvironmentVariables { get; set; }
    }
}
