// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace ManagedCodeGen
{
    internal sealed class JitFormatRootCommand : RootCommand
    {
        public Option<string> Arch { get; } =
            new("--arch", "-a") { Description = "The architecture of the build (options: arm64, x64, x86)" };
        public Option<string> OS { get; } =
            new("--os", "-o") { Description = "The operating system of the build (options: windows, osx, linux, etc.)" };
        public Option<string> Build { get; } =
            new("--build", "-b") { Description = "The build type of the build (options: Release, Checked, Debug)" };
        public Option<string> RuntimePath { get; } =
            new("--runtime", "-r") { Description = "Full path to runtime directory" };
        public Option<string> CompileCommands { get; } =
            new("--compile-commands") { Description = "Full path to compile_commands.json" };
        public Option<bool> Verbose { get; } =
            new("--verbose") { Description = "Enable verbose output." };
        public Option<bool> Untidy { get; } =
            new("--untidy") { Description = "Do not run clang-tidy" };
        public Option<bool> NoFormat { get; } =
            new("--noformat") { Description = "Do not run clang-format" };
        public Option<bool> Cross { get; } =
            new("--cross") { Description = "If on Linux, run the configure build as a cross build." };
        public Option<bool> Fix { get; } =
            new("--fix", "-f") { Description = "Fix formatting errors discovered by clang-format and clang-tidy." };
        public Option<bool> IgnoreErrors { get; } =
            new("--ignore-errors", "-i") { Description = "Ignore clang-tidy errors" };
        public Option<List<string>> Projects { get; } =
            new("--projects") { Description = "List of build projects clang-tidy should consider (e.g. dll, standalone, protojit, etc.). Default: dll" };
        public Argument<List<string>> Filenames { get; } =
            new("filenames") { Description = "Optional list of files that should be formatted." };

        public ParseResult Result { get; private set; }

        public JitFormatRootCommand(string[] args) : base("JIT formatting tool")
        {
            Options.Add(Arch);
            Options.Add(OS);
            Options.Add(Build);
            Options.Add(RuntimePath);
            Options.Add(CompileCommands);
            Options.Add(Verbose);
            Options.Add(Untidy);
            Options.Add(NoFormat);
            Options.Add(Cross);
            Options.Add(Fix);
            Options.Add(IgnoreErrors);
            Options.Add(Projects);
            Arguments.Add(Filenames);

            SetAction(result =>
            {
                Result = result;
                return jitformat.Execute(this);
            });
        }
    }
}
