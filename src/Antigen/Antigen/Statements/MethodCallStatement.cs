// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Expressions;

namespace Antigen.Statements
{
    public class MethodCallStatement : Statement
    {
        public readonly string MethodName;
        public readonly Expression MethodCallExpr;

        public MethodCallStatement(TestCase testCase, Expression methodCallExpr) : base(testCase)
        {
            MethodCallExpr = methodCallExpr;
        }

        public override string ToString()
        {
            return $"{MethodCallExpr};";
        }
    }
}
