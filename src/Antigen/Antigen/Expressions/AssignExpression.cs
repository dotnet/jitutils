// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Tree;

namespace Antigen.Expressions
{
    public class AssignExpression : BinaryExpression
    {
        public AssignExpression(TestCase testCase, Tree.ValueType leftType, Expression lhs, Operator op, Expression rhs) : base(testCase, leftType, lhs, op, rhs)
        {
        }
    }
}
