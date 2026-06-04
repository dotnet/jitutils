// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Antigen.Expressions;

namespace Antigen.Statements
{
    public class DoWhileStatement : LoopStatement
    {
        public DoWhileStatement(TestCase tc, int nestNum, int numOfSecondaryVars, Expression bounds, List<Statement> loopBody) :
                base(tc, nestNum, numOfSecondaryVars, bounds, loopBody)
        {
        }

        protected override void PopulatePreLoopBody()
        {
            // Induction variables to be initialized outside the loop
            loopBodyBuilder.AppendFormat("{0};", GenerateIVInitCode()).AppendLine();
            loopBodyBuilder.AppendLine("do {");

            // Add step/break condition at the beginning 
            loopBodyBuilder.AppendLine(string.Join(Environment.NewLine, GenerateIVBreakAndStepCode(isCodeForBreakCondAtTheEnd: false)));
        }

        protected override void PopulatePostLoopBody()
        {
            // Add step/break condition at the beginning 
            loopBodyBuilder.AppendLine(string.Join(Environment.NewLine, GenerateIVBreakAndStepCode(isCodeForBreakCondAtTheEnd: true)));
            loopBodyBuilder.AppendLine("} while(");

            loopBodyBuilder.AppendFormat("({0})", Bounds);

            string loopGuardCondition = GenerateIVLoopGuardCode();
            if (!string.IsNullOrEmpty(loopGuardCondition))
                loopBodyBuilder.AppendFormat(" && ({0})", loopGuardCondition);

            loopBodyBuilder.AppendLine(");");

            Debug.Assert(HasSuccessfullyGenerated(), "DoWhileStatement didn't generate properly. Please check the loop variables.");
        }
    }

}

