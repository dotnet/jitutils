// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;

namespace ManagedCodeGen
{
    internal sealed class JitDasmPmiRootCommand : CliRootCommand
    {
        public CliOption<string> AltJit { get; } =
            new("--altjit") { Description = "If set, the name of the altjit to use (e.g., clrjit_win_arm64_x64.dll)" };
        public CliOption<string> CorerunPath { get; } =
            new("--corerun", "-c") { CustomParser = Helpers.GetResolvedPath, DefaultValueFactory = Helpers.GetResolvedPath, Description = "The corerun compiler exe" };
        public CliOption<string> JitPath { get; } =
            new("--jit", "-j") { CustomParser = Helpers.GetResolvedPath, DefaultValueFactory = Helpers.GetResolvedPath, Description = "The full path to the jit library" };
        public CliOption<string> OutputPath { get; } =
            new("--output", "-o") { Description = "The output path" };
        public CliOption<string> Filename { get; } =
            new("--file", "-f") { Description = "Name of file to take list of assemblies from. Both a file and assembly list can be used" };
        public CliOption<bool> DumpGCInfo { get; } =
            new("--gcinfo") { Description = "Add GC info to the disasm output" };
        public CliOption<bool> DumpDebugInfo { get; } =
            new("--debuginfo") { Description = "Add Debug info to the disasm output" };
        public CliOption<bool> Verbose { get; } =
            new("--verbose") { Description = "Enable verbose output" };
        public CliOption<bool> NoDiffable { get; } =
            new("--nodiffable") { Description = "Generate non-diffable asm (pointer values will be left in output)" };
        public CliOption<bool> Tier0 { get; } =
            new("--tier0") { Description = "Generate tier0 code" };
        public CliOption<bool> Cctors { get; } =
            new("--cctors") { Description = "Jit and run cctors before jitting other methods" };
        public CliOption<bool> Recursive { get; } =
            new("--recursive", "-r") { Description = "Search directories recursively" };
        public CliOption<List<string>> PlatformPaths { get; } =
            new("--platform", "-p") { Description = "Path to platform assemblies" };
        public CliOption<List<string>> Methods { get; } =
            new("--methods", "-m") { Description = "List of methods to disasm" };
        public CliArgument<List<string>> AssemblyList { get; } =
            new("--assembly") { Description = "The list of assemblies or directories to scan for assemblies" };
        public CliOption<bool> WaitForDebugger { get; } =
            new("--wait", "-w") { Description = "Wait for debugger to attach" };
        public CliOption<bool> NoCopyJit { get; } =
            new("--nocopy") { Description = "Correct jit has already been copied into the corerun directory" };

        public ParseResult Result;

        public JitDasmPmiRootCommand(string[] args) : base("Managed code gen diff tool")
        {
            Options.Add(AltJit);
            Options.Add(CorerunPath);
            Options.Add(JitPath);
            Options.Add(OutputPath);
            Options.Add(Filename);
            Options.Add(DumpGCInfo);
            Options.Add(DumpDebugInfo);
            Options.Add(Verbose);
            Options.Add(NoDiffable);
            Options.Add(Tier0);
            Options.Add(Cctors);
            Options.Add(Recursive);
            Options.Add(PlatformPaths);
            Options.Add(Methods);
            Options.Add(WaitForDebugger);
            Options.Add(NoCopyJit);

            Arguments.Add(AssemblyList);

            SetAction(result =>
            {
                Result = result;

                try
                {
                    List<string> errors = new();
                    string corerun = Result.GetValue(CorerunPath);
                    if (corerun == null || !File.Exists(corerun))
                    {
                        errors.Add("Can't find --corerun tool.");
                    }

                    if (Result.GetResult(Filename) == null && Result.GetValue(AssemblyList).Count == 0)
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

                    return new Program(this).Run();
                }
                catch (Exception e)
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine("Error: " + e.Message);
                    Console.Error.WriteLine(e.ToString());

                    Console.ResetColor();

                    return 1;
                }
            });
        }
    }
}
