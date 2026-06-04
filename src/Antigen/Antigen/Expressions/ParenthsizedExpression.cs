// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Tree;

namespace Antigen.Expressions
{
    public class ParenthsizedExpression : Expression
    {
        public readonly Node Node;
        public ParenthsizedExpression(TestCase testCase, Node node) : base(testCase)
        {
            Node = node;
        }

        public override string ToString()
        {
            return $"({Node})";
        }
    }
}
