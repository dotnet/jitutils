// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Text;

namespace Antigen.Statements
{
    public class ClassDeclStatement : Statement
    {
        public readonly string ClassName;
        public readonly List<Statement> ClassMembers;

        public ClassDeclStatement(TestCase testCase, string className, List<Statement> classMembers) : base(testCase)
        {
            ClassName = className;
            ClassMembers = classMembers;
        }

        public override string ToString()
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendFormat("public class {0} {{", ClassName).AppendLine();
            ClassMembers.ForEach(member => strBuilder.AppendLine(member.ToString()));
            strBuilder.AppendLine("}");

            return strBuilder.ToString();
        }
    }
}
