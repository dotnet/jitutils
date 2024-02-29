// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;

public static class Helpers
{
    public static CliRootCommand UseVersion(this CliRootCommand command)
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

#nullable enable
    public static string? GetResolvedPath(ArgumentResult result) =>
        result.Tokens.Count > 0 ? Path.GetFullPath(result.Tokens[0].Value) : null;
#nullable disable
}
