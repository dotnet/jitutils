// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Antigen.Trimmer.Rewriters.Expressions
{
    public class BinaryExpRemoval : SyntaxRewriter
    {
        private static HashSet<string> s_trimmedExpr = new HashSet<string>();

        //TODO: Try replacing LHS <op> RHS, just LHS, just RHS.
        public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (s_trimmedExpr.Contains(node.ToFullString()))
            {
                return node;
            }

            if (currId++ == id || removeAll)
            {
                isAnyNodeVisited = true;

                var left = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(15));
                var right = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(4));
                var trimmedExpr = BinaryExpression(node.Kind(), left, right);
                s_trimmedExpr.Add(trimmedExpr.ToFullString());
                return trimmedExpr;
            }

            return base.VisitBinaryExpression(node);
        }
    }
}
