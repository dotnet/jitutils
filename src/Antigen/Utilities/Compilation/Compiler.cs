// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Antigen.Execution;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis;

namespace Antigen.Compilation
{
    public class Compiler
    {
        private static readonly CSharpCompilationOptions ReleaseCompileOptions = new(
            OutputKind.ConsoleApplication,
            concurrentBuild: true,
            optimizationLevel: OptimizationLevel.Release,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                { "SYSLIB5003", ReportDiagnostic.Suppress }
            });

        private static readonly CSharpCompilationOptions DebugCompileOptions = new(
            OutputKind.ConsoleApplication,
            concurrentBuild: true,
            optimizationLevel: OptimizationLevel.Debug,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                { "SYSLIB5003", ReportDiagnostic.Suppress }
            });

        private static readonly string s_corelibPath = typeof(object).Assembly.Location;
        private static readonly MetadataReference[] s_references =
{
             MetadataReference.CreateFromFile(s_corelibPath),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(s_corelibPath), "System.Console.dll")),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(s_corelibPath), "System.Runtime.dll")),
             MetadataReference.CreateFromFile(typeof(SyntaxTree).Assembly.Location),
             MetadataReference.CreateFromFile(typeof(CSharpSyntaxTree).Assembly.Location),
        };

        private readonly string m_outputDirectory;

        public Compiler(string outputDirectory)
        {
            m_outputDirectory = outputDirectory;
        }

        public CompileResult Compile(SyntaxTree programTree, string assemblyName)
        {
            byte[]? debugBytes = null, releaseBytes = null;
            debugBytes = CompileAndGetBytes(programTree, assemblyName, DebugCompileOptions);
            if (debugBytes != null)
            {
                releaseBytes = CompileAndGetBytes(programTree, assemblyName, ReleaseCompileOptions);
            }
            return new CompileResult(assemblyName, null, debugBytes, releaseBytes);
        }

        private byte[] CompileAndGetBytes(SyntaxTree programTree, string assemblyName, CSharpCompilationOptions options)
        {
            string tag = options.OptimizationLevel == OptimizationLevel.Debug ? "Debug" : "Release";
            var cc = CSharpCompilation.Create($"{assemblyName}-{tag}.exe", new SyntaxTree[] { programTree }, s_references, options);

            using (var ms = new MemoryStream())
            {
                EmitResult result;
                try
                {
                    result = cc.Emit(ms);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return null;
                }

                if (!result.Success)
                {
#if UNREACHABLE
                    SaveCompilationError(programTree, result.Diagnostics);
#endif
                    return null;
                }

                ms.Seek(0, SeekOrigin.Begin);

                return ms.ToArray();
            }
        }

        private void SaveCompilationError(SyntaxTree tree, IEnumerable<Diagnostic> diagnostics)
        {
            StringBuilder fileContents = new StringBuilder();

            fileContents.AppendLine(tree.GetRoot().NormalizeWhitespace().ToFullString());
            fileContents.AppendLine("/*");
            var errorLines = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(diag => $"{diag.Location.GetLineSpan().StartLinePosition.Line}: {diag.GetMessage()}");
            fileContents.AppendLine($"Got {errorLines.Count()} compiler error(s):");
            foreach (var error in errorLines)
            {
                fileContents.AppendLine(error);
            }
            var errorFile = Path.Combine(m_outputDirectory, $"{tree.FilePath}.error");
            File.WriteAllText(errorFile, fileContents.ToString());
        }
    }
}
