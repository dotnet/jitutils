// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Expressions;
using Antigen.Tree;

namespace Antigen.Statements
{
    public class AssignStatement : Statement
    {
        public readonly Expression Left;
        public readonly Operator Op;
        public readonly Expression Right;

        public AssignStatement(TestCase testCase, ValueType leftType, Expression lhs, Operator op, Expression rhs, bool isVectorResult = false) : base(testCase)
        {
            Left = lhs;
            Op = op;

            if (isVectorResult)
            {
                Right = rhs;
            }
            else
            {
                Right = Helper.FixDivideByZero(testCase, leftType, op, rhs);
            }
        }

        public override string ToString()
        {
            return $"{Left} {Op} {Right};";
        }
    }
}
