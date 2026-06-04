// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Antigen.Compilation
{
    public struct CompileResult
    {
        public CompileResult(IEnumerable<Diagnostic> diagnostics)
        {
            CompileErrors = diagnostics.Where(diag => diag.Severity == DiagnosticSeverity.Error);
            CompileWarnings = diagnostics.Where(diag => diag.Severity == DiagnosticSeverity.Warning);
        }

        public CompileResult(string assemblyName, string assemblyFullPath, byte[]? debugMs, byte[]? releaseMs)
        {
            AssemblyName = assemblyName;
            AssemblyFullPath = assemblyFullPath;
            DebugAssembly = debugMs;
            ReleaseAssembly = releaseMs;
        }

        public CompileResult(Exception roslynException)
        {
            RoslynException = roslynException;
        }

        public string AssemblyName { get; }
        public Exception RoslynException { get; }
        public IEnumerable<Diagnostic> CompileErrors { get; }
        public IEnumerable<Diagnostic> CompileWarnings { get; }
        public string AssemblyFullPath { get; }
        public byte[]? DebugAssembly { get; }
        public byte[]? ReleaseAssembly { get; }
    }
}
