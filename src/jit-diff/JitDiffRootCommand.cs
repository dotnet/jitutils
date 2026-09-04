// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace ManagedCodeGen
{
    internal sealed class JitDiffRootCommand : RootCommand
    {
        public Option<string> BasePath { get; } =
            new("--base", "-b") { Description = "The base compiler directory or tag. Will use crossgen, corerun, or clrjit from this directory.", Arity = ArgumentArity.ZeroOrOne };
        public Option<string> DiffPath { get; } =
            new("--diff", "-d") { Description = "The diff compiler directory or tag. Will use crossgen, corerun, or clrjit from this directory.", Arity = ArgumentArity.ZeroOrOne };
        public Option<string> CrossgenExe { get; } =
            new("--crossgen") { Description = "The crossgen or crossgen2 compiler exe. When this is specified, will use clrjit from the --base and --diff directories with this crossgen." };
        public Option<string> OutputPath { get; } =
            new("--output", "-o") { Description = "The output path." };
        public Option<bool> NoAnalyze { get; } =
            new("--noanalyze") { Description = "Do not analyze resulting base, diff dasm directories. (By default, the directories are analyzed for diffs.)" };
        public Option<bool> Sequential { get; } =
            new("--sequential", "-s") { Description = "Run sequentially; don't do parallel compiles." };
        public Option<string> Tag { get; } =
            new("--tag", "-t") { Description = "Name of root in output directory. Allows for many sets of output." };
        public Option<bool> CoreLib { get; } =
            new("--corelib", "-c") { Description = "Diff System.Private.CoreLib.dll." };
        public Option<bool> Frameworks { get; } =
            new("--frameworks", "-f") { Description = "Diff frameworks." };
        public Option<string> Metric { get; } =
            new("--metrics", "-m") { Description = "Comma-separated metric to use for diff computations. Available metrics: CodeSize(default), PerfScore, PrologSize, InstrCount, AllocSize, ExtraAllocBytes, DebugClauseCount, DebugVarCount" };
        public Option<bool> Benchmarks { get; } =
            new("--benchmarks") { Description = "Diff core benchmarks." };
        public Option<bool> Tests { get; } =
            new("--tests") { Description = "Diff all tests." };
        public Option<bool> GCInfo { get; } =
            new("--gcinfo") { Description = "Add GC info to the disasm output." };
        public Option<bool> DebugInfo { get; } =
            new("--debuginfo") { Description = "Add Debug info to the disasm output." };
        public Option<bool> Verbose { get; } =
            new("--verbose", "-v") { Description = "Enable verbose output." };
        public Option<bool> NoDiffable { get; } =
            new("--nodiffable") { Description = "Generate non-diffable asm (pointer values will be left in output)." };
        public Option<string> CoreRoot { get; } =
            new("--core_root") { Description = "Path to test CORE_ROOT." };
        public Option<string> TestRoot { get; } =
            new("--test_root") { Description = "Path to test tree. Use with --benchmarks or --tests." };
        public Option<string> BaseRoot { get; } =
            new("--base_root") { Description = "Path to root of base dotnet/runtime repo." };
        public Option<string> DiffRoot { get; } =
            new("--diff_root") { Description = "Path to root of diff dotnet/runtime repo." };
        public Option<string> Arch { get; } =
            new("--arch") { Description = "Architecture to diff (x86, x64)." };
        public Option<string> Build { get; } =
            new("--build") { Description = "Build flavor to diff (Checked, Debug)." };
        public Option<string> AltJit { get; } =
            new("--altjit") { Description = "If set, the name of the altjit to use (e.g., clrjit_win_arm64_x64.dll)." };
        public Option<bool> Pmi { get; } =
            new("--pmi") { Description = "Run asm diffs via pmi." };
        public Option<bool> Cctors { get; } =
            new("--cctors") { Description = "With --pmi, jit and run cctors before jitting other methods" };
        public Option<List<string>> AssemblyList { get; } =
            new("--assembly") { Description = "Run asm diffs on a given set of assemblies. An individual item can be an assembly or a directory tree containing assemblies." };
        public Option<bool> Tsv { get; } =
            new("--tsv") { Description = "Dump analysis data to diffs.tsv in output directory." };
        public Option<bool> Tier0 { get; } =
            new("--tier0") { Description = "Diff tier0 codegen where possible." };
        public Option<int> Count { get; } =
            new("--count") { Description = "Provide the count parameter to jit-analyze (default 20)." };

        public Option<string> JobName { get; } =
            new("--job", "-j") { Description = "Name of the job." };
        public Option<string> Number { get; } =
            new("--number", "-n") { Description = "Job number." };
        public Option<bool> LastSuccessful { get; } =
            new("--last_successful", "-l") { Description = "Last successful build." };
        public Option<string> BranchName { get; } =
            new("--branch", "-b") { Description = "Name of branch." };

        public ParseResult Result { get; private set; }
        public jitdiff.Commands SelectedCommand { get; private set; }

        public JitDiffRootCommand(string[] args) : base("Managed codegen diff orchestrator")
        {
            Command diffCommand = new("diff", "Run asm diffs via crossgen.")
            {
                BasePath, DiffPath, CrossgenExe, OutputPath, NoAnalyze, Sequential, Tag, CoreLib, Frameworks, Metric,
                Benchmarks, Tests, GCInfo, DebugInfo, Verbose, NoDiffable, CoreRoot, TestRoot, BaseRoot, DiffRoot,
                Arch, Build, AltJit, Pmi, Cctors, AssemblyList, Tsv, Tier0, Count
            };
            diffCommand.SetAction(result => Execute(result, jitdiff.Commands.Diff));
            Subcommands.Add(diffCommand);

            Command listCommand = new("list", "List defaults and available tools in config.json.")
            {
                Verbose
            };
            listCommand.SetAction(result => Execute(result, jitdiff.Commands.List));
            Subcommands.Add(listCommand);

            Command installCommand = new("install", "Install tool in config.json.")
            {
                JobName, Number, LastSuccessful, BranchName, Verbose
            };
            installCommand.SetAction(result => Execute(result, jitdiff.Commands.Install));
            Subcommands.Add(installCommand);

            Command uninstallCommand = new("uninstall", "Uninstall tool from config.json.")
            {
                Tag
            };
            uninstallCommand.SetAction(result => Execute(result, jitdiff.Commands.Uninstall));
            Subcommands.Add(uninstallCommand);
        }

        private int Execute(ParseResult result, jitdiff.Commands command)
        {
            Result = result;
            SelectedCommand = command;

            try
            {
                jitdiff.Config config = new(this);
                return config.DoCommand switch
                {
                    jitdiff.Commands.Diff => jitdiff.DiffTool.DiffCommand(config),
                    jitdiff.Commands.PmiDiff => jitdiff.DiffTool.DiffCommand(config),
                    jitdiff.Commands.List => config.ListCommand(),
                    jitdiff.Commands.Install => jitdiff.InstallCommand(config),
                    jitdiff.Commands.Uninstall => jitdiff.UninstallCommand(config),
                    _ => 1,
                };
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
        }
    }
}
