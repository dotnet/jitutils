// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Expressions;

namespace Antigen.Statements
{
    public class VarDeclStatement : Statement
    {
        public readonly Tree.ValueType TypeName;
        public readonly string VariableName;
        public readonly Expression Expression;

        public VarDeclStatement(TestCase testCase, Tree.ValueType typeName, string variableName, Expression rhs) : base(testCase)
        {
            TypeName = typeName;
            VariableName = variableName;
            Expression = rhs;
        }

        public override string ToString()
        {
            return Expression != null ? $"{TypeName} {VariableName} = {Expression};" : $"{TypeName} {VariableName};";
        }
    }
}
