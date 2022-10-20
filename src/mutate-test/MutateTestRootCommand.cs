// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;

namespace MutateTest
{
    internal sealed class MutateTestRootCommand : RootCommand
    {
        public Argument<string> InputFilePath { get; } =
            new("input-test-case", "Input test case file or directory (for --recursive)") { Arity = ArgumentArity.OneOrMore };
        public Option<bool> EHStress { get; } =
            new(new[] { "--ehStress" }, "Add EH to methods");
        public Option<bool> StructStress { get; } =
            new(new[] { "--structStress" }, "Replace locals with structs");
        public Option<bool> ShowResults { get; } =
            new(new[] { "--showResults" }, "Add EH to methods");
        public Option<bool> Verbose { get; } =
            new(new[] { "--verbose" }, "Describe each transformation");
        public Option<bool> Quiet { get; } =
            new(new[] { "--quiet" }, "Produce minimal output");
        public Option<bool> Recursive { get; } =
            new(new[] { "--recursive" }, "Process each file recursively");
        public Option<int> Seed { get; } =
            new(new[] { "--seed" }, () => 42, "Random seed");
        public Option<bool> StopAtFirstFailure { get; } =
            new(new[] { "--stopAtFirstFailure" }, "Stop each test at first failure");
        public Option<bool> EmptyBlocks { get; } =
            new(new[] { "--emptyBlocks" }, "Transform empty blocks");
        public Option<int> SizeLimit { get; } =
            new(new[] { "--sizeLimit" }, () => 10000, "Don't process programs larger than this size");
        public Option<int> TimeLimit { get; } =
            new(new[] { "--timeLimit" }, () => 10000, "Don't stress programs where compile + run takes more than this many milliseconds");
        public Option<bool> Projects { get; } =
            new(new[] { "--projects" }, "Look for .csproj files instead of .cs files when doing recursive exploration");
        public Option<bool> OnlyFailures { get; } =
            new(new[] { "--onlyFailures" }, "Only emit output for cases that fail at runtime");

        public ParseResult Result { get; private set; }

        public MutateTestRootCommand(string[] args) : base(".NET JIT mutate test utility")
        {
            AddArgument(InputFilePath);
            AddOption(EHStress);
            AddOption(StructStress);
            AddOption(ShowResults);
            AddOption(Verbose);
            AddOption(Quiet);
            AddOption(Recursive);
            AddOption(Seed);
            AddOption(StopAtFirstFailure);
            AddOption(EmptyBlocks);
            AddOption(SizeLimit);
            AddOption(TimeLimit);
            AddOption(Projects);
            AddOption(OnlyFailures);

            this.SetHandler(context =>
            {
                Result = context.ParseResult;

                try
                {
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
