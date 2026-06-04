// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Expressions;

namespace Antigen.Statements
{
    public class ReturnStatement : Statement
    {
        public readonly Expression Expression;

        public ReturnStatement(TestCase testCase, Expression expression) : base(testCase)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            string expression = Expression != null ? Expression.ToString() : string.Empty;
            return $"return {expression};";
        }
    }
}
