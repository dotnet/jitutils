// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Antigen.Statements
{
    public class ArbitraryCodeStatement : Statement
    {
        public readonly string Code;

        public ArbitraryCodeStatement(TestCase testCase, string code) : base(testCase)
        {
            Code = code;
        }

        public override string ToString()
        {
            return Code;
        }
    }
}
