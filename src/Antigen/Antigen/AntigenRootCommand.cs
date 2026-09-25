// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Antigen
{
    internal sealed class AntigenRootCommand : RootCommand
    {
        public Option<string> CoreRunPath { get; } =
            new("--CoreRun", "-c") { Description = "Full path to CoreRun/CoreRun.exe.", Required = true };
        public Option<string> IssuesFolder { get; } =
            new("--IssuesFolder", "-o") { Description = "Full path to folder where issues will be copied.", Required = true };
        public Option<int> NumTestCases { get; } =
            new("--NumTestCases", "-n") { Description = "Number of test cases to execute. By default, 1000." };
        public Option<int> RunDuration { get; } =
            new("--RunDuration", "-d") { Description = "Duration in minutes to run. By default until NumTestCases, but if Duration is given, will override the NumTestCases." };
        public Option<bool> AllowFloatToIntegralReinterpret { get; } =
            new("--AllowFloatToIntegralReinterpret") { Description = "Allow reinterpret-casting floating point vectors to integral types (for example, Vector128.AsInt32()). Can cause false positives due to differing NaN representations." };

        public ParseResult Result { get; private set; }

        public AntigenRootCommand(string[] args) : base("Antigen JIT fuzzer")
        {
            Options.Add(CoreRunPath);
            Options.Add(IssuesFolder);
            Options.Add(NumTestCases);
            Options.Add(RunDuration);
            Options.Add(AllowFloatToIntegralReinterpret);

            SetAction(result =>
            {
                Result = result;
                return Program.Run(this);
            });
        }
    }
}
