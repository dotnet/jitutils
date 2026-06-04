// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Antigen.Trimmer.Rewriters
{
    public class SyntaxRewriter : CSharpSyntaxRewriter
    {
        protected int id = -1;
        protected int currId = 0;
        protected bool removeAll = false;
        protected bool isAnyNodeVisited = false;

        public void RemoveAll()
        {
            removeAll = true;
        }

        public void RemoveOneByOne()
        {
            removeAll = false;
        }

        /// <summary>
        ///     Returns total visited in recent call to Visit().
        /// </summary>
        public int TotalVisited => currId;

        public bool IsAnyNodeVisited => isAnyNodeVisited;

        public void Reset()
        {
            isAnyNodeVisited = false;
            currId = 0;
        }

        public void UpdateId(int newId)
        {
            id = newId;
        }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.Kind() == SyntaxKind.SingleLineCommentTrivia ||
                trivia.Kind() == SyntaxKind.MultiLineCommentTrivia)
            {
                return default(SyntaxTrivia);
            }

            return base.VisitTrivia(trivia);
        }
    }
}
