// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Antigen.Statements
{
    public class MethodDeclStatement : Statement
    {
        public readonly MethodSignature MethodSignature;
        public readonly List<Statement> MethodBody;

        public MethodDeclStatement(TestCase testCase, MethodSignature methodSignature, List<Statement> methodBody) : base(testCase)
        {
            MethodSignature = methodSignature;
            MethodBody = methodBody;
        }

        public override string ToString()
        {
            StringBuilder strBuilder = new StringBuilder();

            if (MethodSignature.IsNoInline)
            {
                strBuilder.AppendLine("[MethodImpl(MethodImplOptions.NoInlining)]");
            }

            var methodName = MethodSignature.MethodName;
            var returnType = MethodSignature.ReturnType;
            var parameters = MethodSignature.Parameters.Select(p =>
            {
                string parameter;
                if (p.PassingWay == ParamValuePassing.Out)
                {
                    parameter = "out ";
                }
                else if (p.PassingWay == ParamValuePassing.Ref)
                {
                    parameter = "ref ";
                }
                else
                {
                    parameter = "";
                }
                parameter += $"{p.ParamType} {p.ParamName}";
                return parameter;
            });

            strBuilder.AppendFormat("public {0} {1}({2})", returnType, methodName, string.Join(", ", parameters)).AppendLine();
            strBuilder.AppendLine("{");
            strBuilder.AppendLine("unchecked {");
            MethodBody.ForEach(stmt => strBuilder.AppendLine(stmt.ToString()));
            strBuilder.AppendLine("}");
            strBuilder.AppendLine("}");

            return strBuilder.ToString();
        }
    }
}
