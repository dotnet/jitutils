// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;

namespace ManagedCodeGen
{
    internal sealed class JitDasmRootCommand : RootCommand
    {
        public Option<string> AltJit { get; } =
            new("--altjit", "If set, the name of the altjit to use (e.g., clrjit_win_arm64_x64.dll)");
        public Option<string> CrossgenPath { get; } =
            new(new[] { "--crossgen", "-c" }, result => result.Tokens.Count > 0 ? Path.GetFullPath(result.Tokens[0].Value) : null, true, "The crossgen or crossgen2 compiler exe.");
        public Option<string> JitPath { get; } =
            new(new[] { "--jit", "-j" }, result => result.Tokens.Count > 0 ? Path.GetFullPath(result.Tokens[0].Value) : null, true, "The full path to the jit library");
        public Option<string> OutputPath { get; } =
            new(new[] { "--output", "-o" }, "The output path");
        public Option<string> Filename { get; } =
            new(new[] { "--file", "-f" }, "Name of file to take list of assemblies from. Both a file and assembly list can be used");
        public Option<bool> DumpGCInfo { get; } =
            new("--gcinfo", "Add GC info to the disasm output");
        public Option<bool> DumpDebugInfo { get; } =
            new("--debuginfo", "Add Debug info to the disasm output");
        public Option<bool> Verbose { get; } =
            new("--verbose", "Enable verbose output");
        public Option<bool> NoDiffable { get; } =
            new("--nodiffable", "Generate non-diffable asm (pointer values will be left in output)");
        public Option<bool> Recursive { get; } =
            new(new[] { "--recursive", "-r" }, "Search directories recursively");
        public Option<List<string>> PlatformPaths { get; } =
            new(new[] { "--platform", "-p" }, "Path to platform assemblies");
        public Option<List<string>> Methods { get; } =
            new(new[] { "--methods", "-m" }, "List of methods to disasm");
        public Option<List<string>> AssemblyList { get; } =
            new("--assembly", "The list of assemblies or directories to scan for assemblies");
        public Option<bool> WaitForDebugger { get; } =
            new(new[] { "--wait", "-w" }, "Wait for debugger to attach");

        public ParseResult Result;
        public bool CodeGeneratorV1 { get; private set; }

        public JitDasmRootCommand(string[] args) : base("Managed codegen diff tool (crossgen/AOT)")
        {
            AddOption(AltJit);
            AddOption(CrossgenPath);
            AddOption(JitPath);
            AddOption(OutputPath);
            AddOption(Filename);
            AddOption(DumpGCInfo);
            AddOption(DumpDebugInfo);
            AddOption(Verbose);
            AddOption(NoDiffable);
            AddOption(Recursive);
            AddOption(PlatformPaths);
            AddOption(Methods);
            AddOption(AssemblyList);
            AddOption(WaitForDebugger);

            this.SetHandler(context =>
            {
                Result = context.ParseResult;

                try
                {
                    List<string> errors = new();
                    string crossgen = Result.GetValue(CrossgenPath);
                    if (crossgen == null || !File.Exists(crossgen))
                    {
                        errors.Add("Can't find --crossgen tool.");
                    }

                    string crossgenFilename = Path.GetFileNameWithoutExtension(crossgen).ToLower();
                    if (crossgenFilename == "crossgen")
                    {
                        CodeGeneratorV1 = true;
                    }
                    else if (crossgenFilename != "crossgen2")
                    {
                        errors.Add("--crossgen tool should be crossgen or crossgen2.");
                    }

                    if (Result.FindResultFor(Filename) == null && Result.GetValue(AssemblyList).Count == 0)
                    {
                        errors.Add("No input: Specify --file <arg> or list input assemblies.");
                    }

                    string jitPath = Result.GetValue(JitPath);
                    if (jitPath != null && !File.Exists(jitPath))
                    {
                        errors.Add("Can't find --jit library.");
                    }

                    string filename = Result.GetValue(Filename);
                    if (filename != null && !File.Exists(filename))
                    {
                        errors.Add($"Error reading input file {filename}, file not found.");
                    }

                    if (errors.Count > 0)
                    {
                        throw new Exception(string.Join(Environment.NewLine, errors));
                    }

                    context.ExitCode = new Program(this).Run();
                }
                catch (Exception e)
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine("Error: " + e.Message);
                    Console.Error.WriteLine(e.ToString());

                    Console.ResetColor();

                    context.ExitCode = 1;
                }
            });
        }
    }
}
