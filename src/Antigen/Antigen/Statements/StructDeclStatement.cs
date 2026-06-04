// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Antigen.Statements
{
    public class StructDeclStatement : Statement
    {
        public readonly string StructName;
        public readonly List<StructField> StructFields;
        public readonly List<StructDeclStatement> NestedStructs;

        public StructDeclStatement(TestCase testCase, string structName, List<StructField> structFields, List<StructDeclStatement> nestedStructs) : base(testCase)
        {
            StructName = structName;
            StructFields = structFields;
            NestedStructs = nestedStructs;
        }

        public override string ToString()
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendFormat("public struct {0} {{", StructName).AppendLine();
            // First define all the nested structs
            NestedStructs.ForEach(ns => strBuilder.AppendFormat("{0}", ns).AppendLine());

            // Next define all the fields
            strBuilder.AppendLine(string.Join(Environment.NewLine, StructFields));
            strBuilder.AppendLine("}");

            return strBuilder.ToString();
        }
    }

    /// <summary>
    ///     Represents a field present in a struct. This is useful to
    ///     expand the fully qualifier name of a field present inside a
    ///     nested struct.
    /// </summary>
    public struct StructField
    {
        public string FieldName;
        public Tree.ValueType FieldType;
        public string Accessor;

        public StructField(Tree.ValueType type, string name, string accessor = "public")
        {
            FieldType = type;
            FieldName = name;
            Accessor = accessor;
        }

        public override string ToString()
        {
            return $"{Accessor} {FieldType} {FieldName};";
        }
    }
}
