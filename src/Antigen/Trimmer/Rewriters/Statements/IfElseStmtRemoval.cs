// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antigen.Trimmer.Rewriters
{
    public class IfElseStmtRemoval : SyntaxRewriter
    {
        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            if (node.ToFullString().Contains("loopInvariant"))
            {
                // do not count them
                return base.VisitIfStatement(node);
            }

            if (currId++ == id || removeAll)
            {
                isAnyNodeVisited = true;
                //TODO: Also - return node.WithElse(null);

                return null;
            }

            return base.VisitIfStatement(node);
        }
    }
}
