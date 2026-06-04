// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Antigen.Trimmer.Rewriters.Expressions
{
    public class ParenExprRemoval : SyntaxRewriter
    {
        public override SyntaxNode VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            if (currId++ == id || removeAll)
            {
                isAnyNodeVisited = true;

                return Visit(node.ChildNodes().ToList()[0]);
            }

            return base.VisitParenthesizedExpression(node);
        }
    }
}
