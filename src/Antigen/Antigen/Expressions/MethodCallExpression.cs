// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;

namespace Antigen.Expressions
{
    public class MethodCallExpression : Expression
    {
        public readonly string MethodName;
        public readonly List<Expression> Arguments;
        public readonly List<ParamValuePassing> ArgsPassingWays;

        public MethodCallExpression(string methodName, List<Expression> arguments, List<ParamValuePassing> passingWays) : base(null)
        {
            Debug.Assert(arguments.Count == passingWays.Count);

            MethodName = methodName;
            Arguments = arguments;
            ArgsPassingWays = passingWays;
        }

        public override string ToString()
        {
            List<string> finalArgs = new List<string>();

            for (int argId = 0; argId < Arguments.Count; argId++)
            {
                Expression argument = Arguments[argId];
                ParamValuePassing paramPassing = ArgsPassingWays[argId];
                switch (paramPassing)
                {
                    case ParamValuePassing.None:
                        finalArgs.Add(argument.ToString());
                        break;
                    case ParamValuePassing.Ref:
                        finalArgs.Add($"ref {argument}");
                        break;
                    case ParamValuePassing.Out:
                        finalArgs.Add($"out {argument}");
                        break;
                    default:
                        Debug.Assert(false, "Unreachable");
                        break;
                }
            }

            return $"{MethodName}({string.Join(", ", finalArgs)})";
        }
    }
}
