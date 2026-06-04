// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Antigen.Expressions
{
    public class CastExpression : Expression
    {
        public readonly Expression Expression;
        public readonly Tree.ValueType ToType;

        public CastExpression(TestCase testCase, Expression expr, Tree.ValueType toType) : base(testCase)
        {
            Expression = expr;
            ToType = toType;
        }

        public override string ToString()
        {
            return $"(({ToType}) ({Expression}))";
        }
    }
}
