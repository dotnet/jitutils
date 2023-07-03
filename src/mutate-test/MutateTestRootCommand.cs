// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;

namespace MutateTest
{
    internal sealed class MutateTestRootCommand : CliRootCommand
    {
        public CliArgument<string> InputFilePath { get; } =
            new("input-test-case") { Description = "Input test case file or directory (for --recursive)", Arity = ArgumentArity.OneOrMore };
        public CliOption<bool> EHStress { get; } =
            new("--ehStress") { Description = "Add EH to methods" };
        public CliOption<bool> StructStress { get; } =
            new("--structStress") { Description = "Replace locals with structs" };
        public CliOption<bool> ShowResults { get; } =
            new("--showResults") { Description = "Add EH to methods" };
        public CliOption<bool> Verbose { get; } =
            new("--verbose") { Description = "Describe each transformation" };
        public CliOption<bool> Quiet { get; } =
            new("--quiet") { Description = "Produce minimal output" };
        public CliOption<bool> Recursive { get; } =
            new("--recursive") { Description = "Process each file recursively" };
        public CliOption<int> Seed { get; } =
            new("--seed") { DefaultValueFactory = _ => 42, Description = "Random seed" };
        public CliOption<bool> StopAtFirstFailure { get; } =
            new("--stopAtFirstFailure") { Description = "Stop each test at first failure" };
        public CliOption<bool> EmptyBlocks { get; } =
            new("--emptyBlocks") { Description = "Transform empty blocks" };
        public CliOption<int> SizeLimit { get; } =
            new("--sizeLimit") { DefaultValueFactory = _ => 10000, Description = "Don't process programs larger than this size" };
        public CliOption<int> TimeLimit { get; } =
            new("--timeLimit") { DefaultValueFactory = _ => 10000, Description = "Don't stress programs where compile + run takes more than this many milliseconds" };
        public CliOption<bool> Projects { get; } =
            new("--projects") { Description = "Look for .csproj files instead of .cs files when doing recursive exploration" };
        public CliOption<bool> OnlyFailures { get; } =
            new("--onlyFailures") { Description = "Only emit output for cases that fail at runtime" };

        public ParseResult Result { get; private set; }

        public MutateTestRootCommand(string[] args) : base(".NET JIT mutate test utility")
        {
            Arguments.Add(InputFilePath);
            Options.Add(EHStress);
            Options.Add(StructStress);
            Options.Add(ShowResults);
            Options.Add(Verbose);
            Options.Add(Quiet);
            Options.Add(Recursive);
            Options.Add(Seed);
            Options.Add(StopAtFirstFailure);
            Options.Add(EmptyBlocks);
            Options.Add(SizeLimit);
            Options.Add(TimeLimit);
            Options.Add(Projects);
            Options.Add(OnlyFailures);

            SetAction(result =>
            {
                Result = result;

                try
                {
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
