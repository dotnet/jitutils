// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antigen.Trimmer.Rewriters.Statements
{
    public class InvocationExprRemoval : SyntaxRewriter
    {
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (currId++ == id || removeAll)
            {
                isAnyNodeVisited = true;
                return null;
            }

            return base.VisitInvocationExpression(node);
        }
    }
}
