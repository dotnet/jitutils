// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Antigen.Expressions;

namespace Antigen.Statements
{
    public enum Kind
    {
        SimpleLoop,
        NormalLoop,
        ComplexLoop
    }

    public class ForStatement : LoopStatement
    {
        public readonly Expression LoopStep;
        public readonly Kind LoopKind;
        public readonly string LoopVar;


        public ForStatement(
            TestCase tc, string loopVar, int nestNum, int numOfSecondaryVars, Expression bounds, List<Statement> loopBody, Expression loopStep, Kind loopKind) :
            base(tc, nestNum, numOfSecondaryVars, bounds, loopBody)
        {
            LoopVar = loopVar;
            LoopStep = loopStep;
            LoopKind = loopKind;
        }

        protected override void PopulatePreLoopBody()
        {
            // Induction variables to be initialized outside the loop
            loopBodyBuilder.AppendFormat("{0};", GenerateIVInitCode(false)).AppendLine();

            // induction variable initialization
            loopBodyBuilder.AppendFormat("for({0};", GenerateIVInitCode(true));

            // condition
            string guardCode = GenerateIVLoopGuardCode();
            loopBodyBuilder.Append(guardCode);

            if (LoopKind == Kind.NormalLoop || LoopKind == Kind.ComplexLoop)
            {
                if (!string.IsNullOrEmpty(guardCode))
                    loopBodyBuilder.Append(" &&");
                loopBodyBuilder.AppendFormat(" {0} < ({1})", LoopVar, Bounds);
            }
            loopBodyBuilder.Append(';');

            // induction variables to be incr/decr
            string stepCode = GenerateIVStepCode();
            loopBodyBuilder.Append(stepCode);

            if (LoopKind == Kind.NormalLoop)
            {
                if (!string.IsNullOrEmpty(stepCode))
                {
                    loopBodyBuilder.Append(" ,");
                }
                loopBodyBuilder.AppendFormat(" {0}++", LoopVar);
            }
            else if (LoopKind == Kind.ComplexLoop)
            {
                if (!string.IsNullOrEmpty(stepCode))
                {
                    loopBodyBuilder.Append(',');
                }
                loopBodyBuilder.Append(' ');
                loopBodyBuilder.Append(LoopStep);
            }
            loopBodyBuilder.AppendLine(")");

            loopBodyBuilder.AppendLine("{");

            // Add step/break condition at the beginning 
            loopBodyBuilder.AppendLine(string.Join(Environment.NewLine, GenerateIVBreakAndStepCode(isCodeForBreakCondAtTheEnd: false)));
        }

        protected override void PopulatePostLoopBody()
        {
            // Add step/break condition at the end 
            loopBodyBuilder.AppendLine(string.Join(Environment.NewLine, GenerateIVBreakAndStepCode(isCodeForBreakCondAtTheEnd: true)));

            Debug.Assert(HasSuccessfullyGenerated(), "ForStatement didn't generate properly. Please check the loop variables.");

            loopBodyBuilder.AppendLine("}");
        }
    }
}
