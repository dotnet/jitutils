// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;

namespace ManagedCodeGen
{
    internal sealed class JitDasmPmiRootCommand : RootCommand
    {
        public Option<string> AltJit { get; } =
            new("--altjit", "If set, the name of the altjit to use (e.g., clrjit_win_arm64_x64.dll)");
        public Option<string> CorerunPath { get; } =
            new(new[] { "--corerun", "-c" }, result => result.Tokens.Count > 0 ? Path.GetFullPath(result.Tokens[0].Value) : null, true, "The corerun compiler exe");
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
        public Option<bool> Tier0 { get; } =
            new("--tier0", "Generate tier0 code");
        public Option<bool> Cctors { get; } =
            new("--cctors", "Jit and run cctors before jitting other methods");
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
        public Option<bool> NoCopyJit { get; } =
            new("--nocopy", "Correct jit has already been copied into the corerun directory");

        public ParseResult Result;

        public JitDasmPmiRootCommand(string[] args) : base("Managed code gen diff tool")
        {
            AddOption(AltJit);
            AddOption(CorerunPath);
            AddOption(JitPath);
            AddOption(OutputPath);
            AddOption(Filename);
            AddOption(DumpGCInfo);
            AddOption(DumpDebugInfo);
            AddOption(Verbose);
            AddOption(NoDiffable);
            AddOption(Tier0);
            AddOption(Cctors);
            AddOption(Recursive);
            AddOption(PlatformPaths);
            AddOption(Methods);
            AddOption(AssemblyList);
            AddOption(WaitForDebugger);
            AddOption(NoCopyJit);

            this.SetHandler(context =>
            {
                Result = context.ParseResult;

                try
                {
                    List<string> errors = new();
                    string corerun = Result.GetValue(CorerunPath);
                    if (corerun == null || !File.Exists(corerun))
                    {
                        errors.Add("Can't find --corerun tool.");
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
