// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using Antigen.Expressions;

namespace Antigen.Statements
{
    public class SwitchStatement : Statement
    {
        public readonly Expression SwitchExpr;
        public readonly List<Tuple<ConstantValue, List<Statement>>> Cases;
        public readonly List<Statement> DefaultBody;

        public SwitchStatement(TestCase testCase,
            Expression switchExpr, List<Tuple<ConstantValue, List<Statement>>> cases, List<Statement> defaultBody) : base(testCase)
        {
            SwitchExpr = switchExpr;
            Cases = cases;
            DefaultBody = defaultBody;
        }

        public override string ToString()
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendFormat("switch ({0})", SwitchExpr).AppendLine();
            strBuilder.AppendLine("{");

            if (Cases != null && Cases.Count > 0)
            {
                foreach (var caseClause in Cases)
                {
                    strBuilder.AppendFormat("case {0}:", caseClause.Item1).AppendLine();
                    strBuilder.AppendLine("{");
                    strBuilder.AppendLine(string.Join(Environment.NewLine, caseClause.Item2));
                    strBuilder.AppendLine("break;");
                    strBuilder.AppendLine("}");
                }
            }

            strBuilder.AppendLine("default:");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine(string.Join(Environment.NewLine, DefaultBody));
            strBuilder.AppendLine("break;");
            strBuilder.AppendLine("}");

            strBuilder.AppendLine("}");
            return strBuilder.ToString();
        }
    }
}
