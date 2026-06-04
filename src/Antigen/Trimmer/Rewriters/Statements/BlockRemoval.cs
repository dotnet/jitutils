// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Antigen.Trimmer.Rewriters.Statements
{
    public class BlockRemoval : SyntaxRewriter
    {
        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            if (node.Statements.Count == 0)
            {
                return null;
            }

            if (currId++ == id || removeAll)
            {
                isAnyNodeVisited = true;

                return Block();
            }

            return base.VisitBlock(node);
        }
    }
}
