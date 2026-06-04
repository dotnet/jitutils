// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;

internal static class Helpers
{
    public static RootCommand UseVersion(this RootCommand command)
    {
        for (int i = 0; i < command.Options.Count; i++)
        {
            if (command.Options[i] is VersionOption)
            {
                command.Options[i] = new VersionOption("--version", "-v");
                break;
            }
        }

        return command;
    }

    public static RootCommand UseExtendedHelp(this RootCommand command, Action<ParseResult> customizer)
    {
        ConfigureHelp(command, customizer);
        return command;
    }

    private static void ConfigureHelp(Command command, Action<ParseResult> customizer)
    {
        foreach (Option option in command.Options)
        {
            if (option is HelpOption helpOption)
            {
                helpOption.Action = new CustomizedHelpAction(helpOption, customizer);
                break;
            }
        }

        foreach (Command subcommand in command.Subcommands)
        {
            ConfigureHelp(subcommand, customizer);
        }
    }

    private sealed class CustomizedHelpAction : SynchronousCommandLineAction
    {
        private readonly HelpAction _helpAction;
        private readonly Action<ParseResult> _customizer;

        public CustomizedHelpAction(HelpOption helpOption, Action<ParseResult> customizer)
        {
            _helpAction = (HelpAction)helpOption.Action!;
            _customizer = customizer;
        }

        public override int Invoke(ParseResult parseResult)
        {
            int result = _helpAction.Invoke(parseResult);
            _customizer(parseResult);
            return result;
        }
    }

#nullable enable
    public static string? GetResolvedPath(ArgumentResult result) =>
        result.Tokens.Count > 0 ? Path.GetFullPath(result.Tokens[0].Value) : null;
#nullable disable
}
