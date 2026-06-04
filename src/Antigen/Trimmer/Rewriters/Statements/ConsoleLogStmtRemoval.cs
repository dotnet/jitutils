// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antigen.Trimmer.Rewriters.Statements
{
    /// <summary>
    ///     Only removes all Console.Log at once.
    /// </summary>
    public class ConsoleLogStmtRemoval : SyntaxRewriter
    {
        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (removeAll)
            {
                if ((node.ToFullString().Trim().StartsWith("Console.WriteLine")) || (node.ToFullString().Trim().StartsWith("Log")))
                {
                    isAnyNodeVisited = true;
                    return null;
                }
            }

            return base.VisitExpressionStatement(node);
        }
    }
}
