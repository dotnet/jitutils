// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Antigen.Expressions;

namespace Antigen.Statements
{
    public class FieldDeclStatement : VarDeclStatement
    {
        public readonly bool IsStatic;
        public readonly bool IsProperty;

        public FieldDeclStatement(TestCase testCase, Tree.ValueType typeName, string variableName, Expression rhs, bool isStatic, bool isProperty = false) : base(testCase, typeName, variableName, rhs)
        {
            IsStatic = isStatic;
            IsProperty = isProperty;
        }

        public override string ToString()
        {
            string qualifiers = IsStatic ? "static" : string.Empty;
            return $"{qualifiers} {base.ToString()}";
        }
    }
}
