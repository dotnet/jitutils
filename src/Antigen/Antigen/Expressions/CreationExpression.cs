// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Antigen.Expressions
{
    public class CreationExpression : Expression
    {
        public readonly string TypeName;
        public readonly List<Expression> Arguments;

        public CreationExpression(TestCase testCase, string typeName, List<Expression> arguments) : base(testCase)
        {
            TypeName = typeName;
            Arguments = arguments;
        }

        public override string ToString()
        {
            return Arguments != null ? $"new {TypeName}({string.Join(",", Arguments)})" : $"new {TypeName}()";
        }
    }
}
