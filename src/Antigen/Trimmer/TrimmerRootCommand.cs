// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Trimmer
{
    internal sealed class TrimmerRootCommand : RootCommand
    {
        public Option<string> CoreRunPath { get; } =
            new("--CoreRun", "-c") { Description = "Path to CoreRun/CoreRun.exe.", Required = true };
        public Option<string> ParentPid { get; } =
            new("--ParentPid", "-p") { Description = "Antigen process id" };
        public Option<string> IssuesFolder { get; } =
            new("--IssuesFolder", "-o") { Description = "Path to folder where trimmed issue will be copied." };
        public Option<string> ReproFile { get; } =
            new("--ReproFile", "-f") { Description = "Full path of the repro file." };
        public Option<string> AltJitName { get; } =
            new("--AltJitName", "-j") { Description = "Name of altjit. By default, current OS/arch." };
        public Option<string> AltJitMethodName { get; } =
            new("--AltJitMethodName", "-m") { Description = "Name of method for altjit. By default, current OS/arch." };

        public ParseResult Result { get; private set; }

        public TrimmerRootCommand(string[] args) : base("Antigen test case trimmer")
        {
            Options.Add(CoreRunPath);
            Options.Add(ParentPid);
            Options.Add(IssuesFolder);
            Options.Add(ReproFile);
            Options.Add(AltJitName);
            Options.Add(AltJitMethodName);

            SetAction(result =>
            {
                Result = result;
                return TestTrimmer.Run(this);
            });
        }
    }
}
