// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Antigen.Expressions
{
    public class VariableExpression : Expression
    {
        public readonly string Name;

        public VariableExpression(TestCase testCase, string name) : base(testCase)
        {
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
