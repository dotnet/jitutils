// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using Antigen.Expressions;

namespace Antigen.Statements
{
    public class IfElseStatement : Statement
    {
        public readonly Expression Condition;
        public readonly List<Statement> IfBody;
        public readonly List<Statement> ElseBody;

        public IfElseStatement(TestCase testCase, Expression condition, List<Statement> ifBody, List<Statement> elseBody) : base(testCase)
        {
            Condition = condition;
            IfBody = ifBody;
            ElseBody = elseBody;
        }

        public override string ToString()
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendFormat("if ({0})", Condition).AppendLine();
            strBuilder.AppendLine("{");
            strBuilder.AppendLine(string.Join(Environment.NewLine, IfBody));
            strBuilder.AppendLine("}");
            if (ElseBody != null && ElseBody.Count > 0)
            {
                strBuilder.AppendLine("else");
                strBuilder.AppendLine("{");
                strBuilder.AppendLine(string.Join(Environment.NewLine, ElseBody));
                strBuilder.AppendLine("}");
            }
            return strBuilder.ToString();
        }
    }
}
