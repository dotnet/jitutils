// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antigen.Trimmer.Rewriters
{
    public class ExprStmtRemoval : SyntaxRewriter
    {
        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            string assignmentExpr = node.ToFullString();

            if (currId++ == id || removeAll)
            {
                isAnyNodeVisited = true;

                return null;
            }

            return base.VisitExpressionStatement(node);
        }
    }
}
