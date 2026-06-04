using Antigen.Expressions;
using Antigen.Statements;
using Antigen.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Antigen
{
    /// <summary>
    ///     Denotes the method to generate.
    /// </summary>
    public class TestMethod
    {
        private readonly TestClass _testClass;
        protected TestCase TC => _testClass.TC;
        protected readonly string Name;
        private readonly List<Weights<ParamValuePassing>> _valuePassing;


#if DEBUG
        private readonly Dictionary<string, int> _expressionsCount = new();
        private readonly Dictionary<string, int> _statementsCount = new();
#endif

        private int _loopVarCount = 0;

        internal HashSet<string> callsFromThisMethod = new HashSet<string>();

        public AstUtils GetASTUtils()
        {
            return TC.AstUtils;
        }

        protected Scope MethodScope;
        private readonly bool _isMainInvocation;
        protected MethodSignature MethodSignature { get; set; }
        public Scope CurrentScope => _testClass.CurrentScope;

        protected void PushScope(Scope scope)
        {
            _testClass.PushScope(scope);
        }

        protected Scope PopScope()
        {
            return _testClass.PopScope();
        }

        /// <summary>
        ///     Register the method in enclosing class.
        /// </summary>
        protected void RegisterMethod(MethodSignature methodSignature)
        {
            _testClass.RegisterMethod(methodSignature);
        }

        /// <summary>
        ///     Creates leaf method that does not take parameters and has a single return statement.
        /// </summary>
        protected TestMethod(TestClass enclosingClass, string methodName)
        {
            _testClass = enclosingClass;
            Name = methodName;
            MethodScope = new Scope(enclosingClass.TC, ScopeKind.MethodScope, enclosingClass.ClassScope);
        }

        /// <summary>
        ///     Creates test method.
        /// </summary>
        /// <param name="enclosingClass"></param>
        /// <param name="methodName"></param>
        /// <param name="isMainInvocation"></param>
        public TestMethod(TestClass enclosingClass, string methodName, bool isMainInvocation = false)
        {
            _testClass = enclosingClass;
            Name = methodName;
            MethodScope = new Scope(enclosingClass.TC, ScopeKind.MethodScope, enclosingClass.ClassScope);
            _isMainInvocation = isMainInvocation;
            _valuePassing = new()
            {
                new Weights<ParamValuePassing>(ParamValuePassing.None, TC.Config.ParamPassingNoneProbability),
                new Weights<ParamValuePassing>(ParamValuePassing.Ref, TC.Config.ParamPassingRefProbability),
                new Weights<ParamValuePassing>(ParamValuePassing.Out, TC.Config.ParamPassingOutProbability),
                //new Weights<ParamValuePassing>(ParamValuePassing.In, 10),
            };
        }

        public virtual MethodDeclStatement Generate()
        {
            PushScope(MethodScope);

            PopulateMethodSignature();

            List<Statement> methodBody = new List<Statement>();

            // TODO-TEMP initialize one variable of each type
            foreach (var variableType in Tree.ValueType.GetTypes())
            {
                var variableName = _testClass.GetVariableName(variableType);

                var rhs = ExprHelper(ExprKind.LiteralExpression, variableType, 0);
                CurrentScope.AddLocal(variableType, variableName);

                methodBody.Add(new VarDeclStatement(TC, variableType, variableName, rhs));
            }

            // TODO-TEMP initialize one variable of each struct type
            foreach (var structType in CurrentScope.AllStructTypes)
            {
                var variableName = _testClass.GetVariableName(structType);

                CurrentScope.AddLocal(structType, variableName);

                // Add all the fields to the scope
                var listOfStructFields = CurrentScope.GetStructFields(structType);
                foreach (var structField in listOfStructFields)
                {
                    CurrentScope.AddLocal(structField.FieldType, $"{variableName}.{structField.FieldName}");
                }

                methodBody.Add(new VarDeclStatement(TC, structType, variableName, new CreationExpression(TC, structType.TypeName, null)));

                if (!PRNG.Decide(TC.Config.StructAliasProbability))
                {
                    continue;
                }

                var aliasVariableName = _testClass.GetVariableName(structType);

                CurrentScope.AddLocal(structType, aliasVariableName);

                // Add all the fields to the scope
                listOfStructFields = CurrentScope.GetStructFields(structType);
                foreach (var structField in listOfStructFields)
                {
                    CurrentScope.AddLocal(structField.FieldType, $"{aliasVariableName}.{structField.FieldName}");
                }

                methodBody.Add(new VarDeclStatement(TC, structType, aliasVariableName, new VariableExpression(TC, variableName)));
            }

            // TODO-TEMP initialize out and ref method parameters
            var paramsToInitialize = MethodSignature.Parameters.Where(p => p.PassingWay == ParamValuePassing.Out);
            foreach (MethodParam param in paramsToInitialize)
            {
                methodBody.Add(VariableAssignmentHelper(param.ParamType, param.ParamName));
                CurrentScope.AddLocal(param.ParamType, param.ParamName);
            }

            for (int i = 0; i < PRNG.Next(1, TC.Config.MaxStatementCount); i++)
            {
                StmtKind cur = GetASTUtils().GetRandomStatement();
                methodBody.Add(StatementHelper(cur, 0));
            }

            // For main invocation method, invoke all the other methods once
            if (_isMainInvocation)
            {
                foreach (var nonLeafMethod in _testClass.AllNonLeafMethods)
                {
                    //TODO-future: Select any assignOper
                    //Tree.Operator assignOper = GetASTUtils().GetRandomAssignmentOperator();

                    if (nonLeafMethod.Data.IsVectorMethod && !PRNG.Decide(TC.Config.InvokeIntrinsicMethodsProbability))
                    {
                        continue;
                    }

                    Tree.ValueType lhsType = nonLeafMethod.Data.ReturnType;
                    var lhs = ExprHelper(ExprKind.VariableExpression, lhsType, 0);
                    var rhs = MethodCallHelper(nonLeafMethod.Data, 0);

                    methodBody.Add(new AssignStatement(TC, lhsType, lhs, Operator.ForOperation(Operation.SimpleAssignment), rhs));
                }
            }

            // print all variables
            methodBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

            // return statement
            methodBody.Add(StatementHelper(StmtKind.ReturnStatement, 0));

            PopScope();

            RegisterMethod(MethodSignature);

            return new MethodDeclStatement(TC, MethodSignature, methodBody);
        }

        /// <summary>
        ///     Generates method signature of this method.
        /// </summary>
        protected virtual void PopulateMethodSignature()
        {
            MethodSignature = new MethodSignature(Name);
            int numOfParameters = 0;
            if (!_isMainInvocation)
            {
                numOfParameters = PRNG.Next(1, TC.Config.MaxMethodParametersCount);
                MethodSignature.ReturnType = GetRandomExprType();
            }

            List<MethodParam> parameters = new List<MethodParam>();
            MethodSignature.Parameters = parameters;

            for (int paramIndex = 0; paramIndex < numOfParameters; paramIndex++)
            {
                var paramType = GetRandomExprType();
                var passingWay = PRNG.WeightedChoice(_valuePassing);
                string paramName = "p_" + _testClass.GetVariableName(paramType);

                // Add parameters to the scope except the one that is marked as OUT
                // OUT parameters will be added once they are initialized.
                if (passingWay != ParamValuePassing.Out)
                {
                    CurrentScope.AddLocal(paramType, paramName);
                }

                parameters.Add(new MethodParam()
                {
                    ParamName = paramName,
                    ParamType = paramType,
                    PassingWay = passingWay
                });
            }
        }


        /// <summary>
        ///     Generate statement of <paramref name="stmtKind"/>. It will generate terminal statement
        ///     if <paramref name="depth"/> exceeds the threshold.
        /// </summary>
        /// <param name="stmtKind"></param>
        /// <param name="depth"></param>
        /// <returns></returns>
        public Statement StatementHelper(StmtKind stmtKind, int depth)
        {
            //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

            switch (stmtKind)
            {
                case StmtKind.VariableDeclaration:
                    {
                        Tree.ValueType variableType = GetRandomExprType();

                        string variableName = _testClass.GetVariableName(variableType);

                        Expression rhs = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(variableType), variableType, 0);
                        CurrentScope.AddLocal(variableType, variableName);

                        // Add all the fields to the scope
                        if (variableType.PrimitiveType == Primitive.Struct)
                        {
                            var listOfStructFields = CurrentScope.GetStructFields(variableType);
                            foreach (var structField in listOfStructFields)
                            {
                                CurrentScope.AddLocal(structField.FieldType, $"{variableName}.{structField.FieldName}");
                            }
                        }

                        return new VarDeclStatement(TC, variableType, variableName, rhs);
                    }
                case StmtKind.IfElseStatement:
                    {
                        //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

                        Tree.ValueType condValueType = Tree.ValueType.ForPrimitive(Primitive.Boolean);
                        Expression conditionExpr = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(Primitive.Boolean), condValueType, 0);

                        int ifcount = PRNG.Next(1, TC.Config.MaxStatementCount);
                        List<Statement> ifBody = new List<Statement>();

                        PushScope(new Scope(TC, ScopeKind.ConditionalScope, CurrentScope));
                        for (int i = 0; i < ifcount; i++)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            ifBody.Add(StatementHelper(cur, depth + 1));
                        }
                        ifBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'if' body scope

                        int elsecount = PRNG.Next(1, TC.Config.MaxStatementCount);
                        List<Statement> elseBody = new List<Statement>();

                        PushScope(new Scope(TC, ScopeKind.ConditionalScope, CurrentScope));
                        for (int i = 0; i < elsecount; i++)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            elseBody.Add(StatementHelper(cur, depth + 1));
                        }
                        elseBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'else' body scope

                        return new IfElseStatement(TC, conditionExpr, ifBody, elseBody);
                    }
                case StmtKind.AssignStatement:
                    {
                        Tree.Operator assignOper = GetASTUtils().GetRandomAssignmentOperator();
                        Tree.ValueType lhsExprType, rhsExprType;

                        if (assignOper.IsVectorOper)
                        {
                            lhsExprType = GetASTUtils().GetRandomVectorTypeForOperator(assignOper);
                        }
                        else if (assignOper.HasFlag(OpFlags.Divide))
                        {
                            // For divide, just use 'int` type.
                            lhsExprType = Tree.ValueType.ForPrimitive(Primitive.Int);
                            rhsExprType = lhsExprType;
                        }
                        else if (((assignOper.InputTypes & Primitive.Struct) != 0) && PRNG.Decide(0.2) && CurrentScope.NumOfStructTypes > 0)
                        {
                            //TODO-cleanup: Somehow combine GetRandomExprType() and GetRandomStructType() functionality
                            // Currently the only problem is AllStructTypes is in scope object but GetRandomExprType() is
                            // in AstUtils.
                            lhsExprType = CurrentScope.AllStructTypes[PRNG.Next(CurrentScope.NumOfStructTypes)];
                        }
                        else
                        {
                            lhsExprType = GetASTUtils().GetRandomPrimitiveType(assignOper.InputTypes);
                        }

                        if (assignOper.HasFlag(OpFlags.Shift))
                        {
                            rhsExprType = Tree.ValueType.ForPrimitive(Primitive.Int);
                        }
                        else
                        {
                            rhsExprType = lhsExprType;
                        }

                        ExprKind rhsKind = GetASTUtils().GetRandomExpressionReturningValueType(rhsExprType);
                        Expression lhs = ExprHelper(ExprKind.VariableExpression, lhsExprType, 0);
                        Expression rhs = ExprHelper(rhsKind, rhsExprType, 0);

                        return new AssignStatement(TC, lhsExprType, lhs, assignOper, rhs);
                    }
                case StmtKind.ForStatement:
                    {
                        //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

                        var loopVar = CurrentScope.GetRandomVariable(Tree.ValueType.ForPrimitive(Primitive.Int));
                        var numOfSecondaryVars = PRNG.Next(/*GetOptions().MaxNumberOfSecondaryInductionVariable*/ 1 + 1);
                        var forLoopKind = Kind.ComplexLoop;

                        // 50% of the time, we'll make it a simple loop, 25% each normal and complex.
                        if (PRNG.Next(2) == 0)
                            forLoopKind = Kind.SimpleLoop;
                        else if (PRNG.Next(2) == 0)
                            forLoopKind = Kind.NormalLoop;

                        PushScope(new Scope(TC, ScopeKind.LoopScope, CurrentScope));

                        var bounds = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(Primitive.Int), Tree.ValueType.ForPrimitive(Primitive.Int), 0);
                        Tree.ValueType loopStepType;
                        do
                        {
                            loopStepType = Tree.ValueType.GetRandomType();
                        } while (loopStepType.PrimitiveType == Primitive.SveMaskPattern);

                        var loopStepCastExpr = ExprHelper(ExprKind.AssignExpression, loopStepType, 0) as CastExpression;

                        // No need to cast the assign expression inside for-loop. Additionally compiler would complain for it, so just
                        // unwrap the cast expression and use the real assignment expression.
                        var loopStep = loopStepCastExpr.Expression;

                        //TODO-imp: AddInductionVariables
                        //TODO-imp: ctrlFlowStack
                        //TODO future: label
                        List<Statement> forLoopBody = new List<Statement>();
                        for (int i = 0; i < TC.Config.MaxStatementCount; ++i)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            forLoopBody.Add(StatementHelper(cur, depth + 1));
                        }
                        forLoopBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'for-loop' body scope

                        return new ForStatement(TC, loopVar, _loopVarCount++, numOfSecondaryVars, bounds, forLoopBody, loopStep, forLoopKind);
                    }
                case StmtKind.DoWhileStatement:
                    {
                        //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

                        var numOfSecondaryVars = PRNG.Next(/*GetOptions().MaxNumberOfSecondaryInductionVariable*/ 1 + 1);

                        PushScope(new Scope(TC, ScopeKind.LoopScope, CurrentScope));

                        var bounds = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(Primitive.Boolean), Tree.ValueType.ForPrimitive(Primitive.Boolean), 0);

                        //TODO-imp: AddInductionVariables
                        //TODO-imp: ctrlFlowStack
                        //TODO future: label
                        List<Statement> whileLoopBody = new List<Statement>();

                        for (int i = 0; i < PRNG.Next(1, TC.Config.MaxStatementCount); ++i)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            whileLoopBody.Add(StatementHelper(cur, depth + 1));
                        }
                        whileLoopBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'do-while' body scope

                        return new DoWhileStatement(TC, _loopVarCount++, numOfSecondaryVars, bounds, whileLoopBody);
                    }
                case StmtKind.WhileStatement:
                    {
                        //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

                        var numOfSecondaryVars = PRNG.Next(/*GetOptions().MaxNumberOfSecondaryInductionVariable*/ 1 + 1);

                        PushScope(new Scope(TC, ScopeKind.LoopScope, CurrentScope));

                        var bounds = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(Primitive.Boolean), Tree.ValueType.ForPrimitive(Primitive.Boolean), 0);

                        //TODO-imp: AddInductionVariables
                        //TODO-imp: ctrlFlowStack
                        //TODO future: label
                        List<Statement> whileLoopBody = new List<Statement>();

                        for (int i = 0; i < PRNG.Next(1, TC.Config.MaxStatementCount); ++i)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            whileLoopBody.Add(StatementHelper(cur, depth + 1));
                        }
                        whileLoopBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'while' body scope

                        return new WhileStatement(TC, _loopVarCount++, numOfSecondaryVars, bounds, whileLoopBody);
                    }
                case StmtKind.ReturnStatement:
                    {
                        Tree.ValueType returnType = MethodSignature.ReturnType;
                        if (returnType.IsVectorType || returnType.PrimitiveType != Primitive.Void)
                        {
                            Expression returnExpr = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(returnType), returnType, 0);
                            return new ReturnStatement(TC, returnExpr);
                        }
                        return new ReturnStatement(TC, null);
                    }
                case StmtKind.TryCatchFinallyStatement:
                    {
                        //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

                        int catchCounts = PRNG.Next(0, TC.Config.CatchClausesCount);

                        // If there are no catch, then definitely add finally, otherwise skip it.
                        bool hasFinally = catchCounts == 0 || PRNG.Decide(TC.Config.FinallyClauseProbability);
                        List<Statement> tryBody = new List<Statement>();

                        PushScope(new Scope(TC, ScopeKind.BracesScope, CurrentScope));

                        int tryStmtCount = PRNG.Next(1, TC.Config.MaxStatementCount);
                        for (int i = 0; i < tryStmtCount; i++)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            tryBody.Add(StatementHelper(cur, depth + 1));
                        }
                        tryBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'try' body scope

                        List<Tuple<Type, List<Statement>>> catchClauses = new ();

                        // Clone 
                        var allExceptions = Tree.ValueType.AllExceptions.Select(x => x).ToList();
                        var caughtExceptions = new List<Type>();

                        for (int catchId = 0; catchId < catchCounts; catchId++)
                        {
                            var exceptionToCatch = allExceptions[PRNG.Next(allExceptions.Count())];
                            allExceptions.Remove(exceptionToCatch);

                            // If we already generated a catch-clause of superclass, skip remaining catch clauses.
                            if (caughtExceptions.Any(x => exceptionToCatch.IsSubclassOf(x)))
                            {
                                break;
                            }
                            caughtExceptions.Add(exceptionToCatch);

                            int catchStmtCount = PRNG.Next(1, TC.Config.MaxStatementCount);
                            List<Statement> catchBody = new List<Statement>();

                            PushScope(new Scope(TC, ScopeKind.BracesScope, CurrentScope));

                            for (int i = 0; i < catchStmtCount; i++)
                            {
                                StmtKind cur;
                                if (depth < TC.Config.MaxStmtDepth)
                                {
                                    cur = GetASTUtils().GetRandomStatement();
                                }
                                else
                                {
                                    cur = GetASTUtils().GetRandomTerminalStatement();
                                }
                                catchBody.Add(StatementHelper(cur, depth + 1));
                            }
                            catchBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                            PopScope(); // pop 'catch' body scope
                            catchClauses.Add(Tuple.Create(exceptionToCatch, catchBody));
                        }

                        List<Statement> finallyBody = new List<Statement>();
                        if (hasFinally)
                        {
                            PushScope(new Scope(TC, ScopeKind.BracesScope, CurrentScope));

                            int finallyStmtCount = PRNG.Next(1, TC.Config.MaxStatementCount);
                            for (int i = 0; i < finallyStmtCount; i++)
                            {
                                StmtKind cur;
                                if (depth < TC.Config.MaxStmtDepth)
                                {
                                    cur = GetASTUtils().GetRandomStatement();
                                }
                                else
                                {
                                    cur = GetASTUtils().GetRandomTerminalStatement();
                                }
                                finallyBody.Add(StatementHelper(cur, depth + 1));
                            }
                            finallyBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                            PopScope(); // pop 'finally' body scope
                        }

                        return new TryCatchFinallyStatement(TC, tryBody, catchClauses, finallyBody);
                    }
                case StmtKind.SwitchStatement:
                    {
                        //Debug.Assert(depth <= TC.Config.MaxStmtDepth);

                        int caseCount = PRNG.Next(1, TC.Config.MaxCaseCounts);

                        Primitive switchType = new Primitive[] { Primitive.Int, Primitive.Long, Primitive.Char, Primitive.String }[PRNG.Next(4)];
                        Tree.ValueType switchExprType = Tree.ValueType.ForPrimitive(switchType);
                        ExprKind switchExprKind = GetASTUtils().GetRandomExpressionReturningValueType(switchType);
                        Expression switchExpr = ExprHelper(switchExprKind, switchExprType, 0);
                        //IList<SwitchSectionSyntax> listOfCases = new List<SwitchSectionSyntax>();
                        List<Tuple<ConstantValue, List<Statement>>> listOfCases = new ();
                        HashSet<string> usedCaseLabels = new HashSet<string>();

                        // Generate each cases
                        for (int i = 0; i < caseCount; i++)
                        {
                            PushScope(new Scope(TC, ScopeKind.BracesScope, CurrentScope));

                            // Generate statements within each cases
                            int caseStmtCount = PRNG.Next(1, TC.Config.MaxStatementCount);
                            List<Statement> caseBody = new List<Statement>();
                            for (int j = 0; j < caseStmtCount; j++)
                            {
                                StmtKind cur;
                                if (depth < TC.Config.MaxStmtDepth)
                                {
                                    cur = GetASTUtils().GetRandomStatement();
                                }
                                else
                                {
                                    cur = GetASTUtils().GetRandomTerminalStatement();
                                }
                                caseBody.Add(StatementHelper(cur, depth + 1));
                            }
                            caseBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                            PopScope(); // pop 'case' body scope

                            ConstantValue caseConstantValue;
                            do
                            {
                                caseConstantValue = ExprHelper(ExprKind.LiteralExpression, switchExprType, 0) as ConstantValue;
                            } while (!usedCaseLabels.Add(caseConstantValue.Value));

                            listOfCases.Add(Tuple.Create(caseConstantValue, caseBody));
                        }

                        // Generate default
                        PushScope(new Scope(TC, ScopeKind.BracesScope, CurrentScope));

                        // Generate statements within default
                        int defaultStmtCount = PRNG.Next(1, 3);
                        List<Statement> defaultBody = new List<Statement>();
                        for (int j = 0; j < defaultStmtCount; j++)
                        {
                            StmtKind cur;
                            if (depth < TC.Config.MaxStmtDepth)
                            {
                                cur = GetASTUtils().GetRandomStatement();
                            }
                            else
                            {
                                cur = GetASTUtils().GetRandomTerminalStatement();
                            }
                            defaultBody.Add(StatementHelper(cur, depth + 1));
                        }
                        defaultBody.Add(GetLogInvocationStatement(CurrentScope.LocalVariableNames, TC.Config.LocalVariablesLogProbability));

                        PopScope(); // pop 'default' body scope

                        return new SwitchStatement(TC, switchExpr, listOfCases, defaultBody);
                    }
                case StmtKind.MethodCallStatement:
                    {
                        MethodSignature method = _testClass.GetRandomMethod();
                        Tree.ValueType methodReturnType = method.ReturnType;

                        if (method.IsVectorMethod && PRNG.Decide(TC.Config.StoreIntrinsicMethodCallResultProbability))
                        {
                            // For intrinsic methods, store the result in a variable.
                            if (!methodReturnType.IsVectorType && methodReturnType.PrimitiveType != Primitive.Void)
                            {
                                // Perform assignment only if the vector method returns a value.
                                Expression lhs = ExprHelper(ExprKind.VariableExpression, methodReturnType, 0);
                                Operator assignOper = GetASTUtils().GetRandomAssignmentOperator(methodReturnType);
                                Expression rhs = MethodCallHelper(method, 0);

                                if (assignOper.HasFlag(OpFlags.Shift) && (methodReturnType.PrimitiveType != Primitive.Int))
                                {
                                    rhs = new CastExpression(TC, rhs, Tree.ValueType.ForPrimitive(Primitive.Int));
                                }
                                return new AssignStatement(TC, methodReturnType, lhs, assignOper, rhs, true);
                            }
                        }

                        return new MethodCallStatement(TC, MethodCallHelper(method, 0));
                    }
                default:
                    Debug.Assert(false, string.Format("Hit unknown statement type {0}", Enum.GetName(typeof(StmtKind), stmtKind)));
                    break;
            }
            return null;
        }

        /// <summary>
        ///     Generate statement of <paramref name="exprKind"/> that returns a value of <paramref name="exprType"/>
        ///     type. It will generate terminal expression if <paramref name="depth"/> exceeds the threshold.
        /// </summary>
        /// <param name="exprKind"></param>
        /// <param name="exprType"></param>
        /// <param name="depth"></param>
        /// <returns></returns>
        public Expression ExprHelper(ExprKind exprKind, Tree.ValueType exprType, int depth)
        {
            //Debug.Assert(depth <= TC.Config.MaxExprDepth);

            if (exprType.PrimitiveType == Primitive.SveMaskPattern)
            {
                exprKind = PRNG.Decide(0.5) ? ExprKind.LiteralExpression : ExprKind.VariableExpression;
            }

            if (PRNG.Decide(TC.Config.CSEUsageProbability))
            {
                Expression expr = CurrentScope.GetRandomExpression(exprKind, exprType);
                if (expr != null)
                {
                    return expr;
                }
            }

            Expression resultExpression = null;
            switch (exprKind)
            {
                case ExprKind.LiteralExpression:
                {
                    resultExpression = ConstantValue.GetConstantValue(exprType, TC._numerals);
                    break;
                }

                case ExprKind.VariableExpression:
                {
                    resultExpression = new VariableExpression(TC, CurrentScope.GetRandomVariable(exprType));
                    break;
                }

                case ExprKind.BinaryOpExpression:
                {
                    //Debug.Assert(depth <= TC.Config.MaxExprDepth);

                    Operator op = GetASTUtils().GetRandomBinaryOperator(exprType);

                    Tree.ValueType lhsExprType, rhsExprType;

                    if (exprType.IsVectorType)
                    {
                        // The vector type should match exactly.
                        lhsExprType = rhsExprType = exprType;
                    }
                    else
                    {
                        Primitive returnType = exprType.PrimitiveType;

                        if (op.HasFlag(OpFlags.Divide))
                        {
                            // For '/' or '%' operations, just use int as dividend and divisor
                            lhsExprType = Tree.ValueType.ForPrimitive(Primitive.Int);
                            rhsExprType = lhsExprType;
                        }
                        else
                        {
                            // If the return type is boolean, then take any ExprType that returns boolean.
                            // However for other types, choose the same type for BinOp expression as the one used to store the result on LHS.
                            //TODO-future: Consider doing GetRandomExprType(op.InputTypes) below. Currently, if this is done,
                            // we end up getting code like (short)(1233342432.5M + 35435435.5M), where "short" is the exprType and
                            // the literals are selected of different type ("decimal" in this example) and we get compilation error
                            // because they can't be casted to short.
                            lhsExprType = GetASTUtils().GetRandomPrimitiveType(returnType == Primitive.Boolean ? op.InputTypes : returnType);
                            //Tree.ValueType lhsExprType = GetASTUtils().GetRandomExprType(op.InputTypes);
                            rhsExprType = lhsExprType;

                            if (op.HasFlag(OpFlags.Shift))
                            {
                                rhsExprType = Tree.ValueType.ForPrimitive(Primitive.Int);
                            }
                        }
                    }

                    ExprKind lhsExprKind, rhsExprKind;
                    if (depth < TC.Config.MaxExprDepth)
                    {
                        lhsExprKind = GetASTUtils().GetRandomExpressionReturningValueType(lhsExprType);
                        rhsExprKind = GetASTUtils().GetRandomExpressionReturningValueType(rhsExprType);
                    }
                    else
                    {
                        lhsExprKind = GetASTUtils().GetRandomTerminalExpression(_testClass, exprType);
                        rhsExprKind = GetASTUtils().GetRandomTerminalExpression(_testClass, exprType);
                    }

                    // Fold arithmetic binop expressions that has constants.
                    // csc.exe would automatically fold that for us, but by doing it here, we eliminate generate 
                    // errors during compiling the test case.
                    if (op.HasFlag(OpFlags.Math) && lhsExprKind == ExprKind.LiteralExpression && rhsExprKind == ExprKind.LiteralExpression)
                    {
                        resultExpression = ConstantValue.GetConstantValue(exprType, TC._numerals);
                    }
                    else
                    {

                        Expression lhs = ExprHelper(lhsExprKind, lhsExprType, depth + 1);
                        Expression rhs = ExprHelper(rhsExprKind, rhsExprType, depth + 1);

                        resultExpression = new CastExpression(TC, new BinaryExpression(TC, lhsExprType, lhs, op, rhs), exprType);
                    }
                    break;
                }
                case ExprKind.AssignExpression:
                {
                    //Debug.Assert(depth <= TC.Config.MaxExprDepth);

                    Tree.Operator assignOper = GetASTUtils().GetRandomAssignmentOperator(exprType);
                    Tree.ValueType lhsExprType, rhsExprType;
                    lhsExprType = rhsExprType = exprType;

                    if (!assignOper.IsVectorOper)
                    {
                        if (assignOper.HasFlag(OpFlags.Divide))
                        {
                            // For divide, just use 'int` type.
                            lhsExprType = Tree.ValueType.ForPrimitive(Primitive.Int);
                            rhsExprType = lhsExprType;
                        }
                        else if (assignOper.HasFlag(OpFlags.Shift))
                        {
                            rhsExprType = Tree.ValueType.ForPrimitive(Primitive.Int);
                        }
                    }

                    ExprKind rhsKind;
                    if (depth < TC.Config.MaxExprDepth)
                    {
                        rhsKind = GetASTUtils().GetRandomExpressionReturningValueType(rhsExprType);
                    }
                    else
                    {
                        rhsKind = GetASTUtils().GetRandomTerminalExpression(_testClass, rhsExprType);
                    }

                    Expression lhs = ExprHelper(ExprKind.VariableExpression, lhsExprType, depth + 1);
                    Expression rhs = ExprHelper(rhsKind, rhsExprType, depth + 1);

                    resultExpression = new CastExpression(TC, new AssignExpression(TC, lhsExprType, lhs, assignOper, rhs), exprType);
                    break;
                }
                case ExprKind.MethodCallExpression:
                {
                    resultExpression = depth < TC.Config.MaxExprDepth
                        ? MethodCallHelper(_testClass.GetRandomMethod(exprType), depth + 1)
                        : MethodCallHelper(_testClass.GetRandomLeafMethod(exprType), depth + 1);
                        break;
                }

                default:
                    Debug.Assert(false, string.Format("Hit unknown expression type {0}", Enum.GetName(typeof(ExprKind), exprKind)));
                    break;
            }

            if (resultExpression != null)
            {
                CurrentScope.AddExpression(exprKind, exprType, resultExpression);
            }
            return resultExpression;
        }

        /// <summary>
        ///     Generates assignment for variable name
        /// </summary>
        /// <returns></returns>
        public Statement VariableAssignmentHelper(Tree.ValueType exprType, string variableName)
        {
            Expression lhs = new VariableExpression(TC, variableName);
            Expression rhs;

            int noOfAttempts = TC.Config.NumOfAttemptsForExpression;
            do
            {
                rhs = ExprHelper(GetASTUtils().GetRandomExpressionReturningValueType(exprType), exprType, 0);
                // Make sure that we do not end up with same lhs=lhs.
                if (lhs.ToString() != rhs.ToString())
                {
                    break;
                }
            } while (noOfAttempts++ < 5);
            Debug.Assert(lhs.ToString() != rhs.ToString());

            return new AssignStatement(TC, exprType, lhs, Operator.ForOperation(Operation.SimpleAssignment), rhs);
        }

        private Expression MethodCallHelper(MethodSignature methodSig, int depth)
        {
            List<Expression> argumentNodes = new List<Expression>();
            List<ParamValuePassing> passingWays = new List<ParamValuePassing>();

            int paramsCount = methodSig.Parameters.Count;

            for (int paramId = 0; paramId < paramsCount; paramId++)
            {
                MethodParam parameter = methodSig.Parameters[paramId];

                Tree.ValueType argType = parameter.ParamType;
                ExprKind argExprKind;
                // if (argType.PrimitiveType == Primitive.SveMaskPattern)
                // {
                //     argExprKind = GetASTUtils().GetRandomTerminalExpression(_testClass, argType);
                // }
                // else 
                if (parameter.PassingWay == ParamValuePassing.None)
                {
                    if (depth < TC.Config.MaxExprDepth)
                    {
                        argExprKind = GetASTUtils().GetRandomExpressionReturningValueType(argType);
                    }
                    else
                    {
                        argExprKind = GetASTUtils().GetRandomTerminalExpression(_testClass, argType);
                    }
                }
                else
                {
                    argExprKind = ExprKind.VariableExpression;
                }

                Expression argExpr = ExprHelper(argExprKind, argType, depth + 1);

                if (methodSig.IsVectorMethod && !argType.IsVectorType)
                {
                    if ((methodSig.MethodName.Contains("GetElement") || methodSig.MethodName.Contains("WithElement")) && (parameter.ParamName == "index"))
                    {
                        // For GetElement/WithElement, the index should not exceed the element count. So perform modulo operation for (argExpr % ElementCount)
                        VectorType targetVectorType = methodSig.Parameters[0].ParamType.VectorType;
                        ConstantValue elementCountExpr;
                        if (Tree.ValueType.GetElementCount(targetVectorType) == 1)
                        {
                            elementCountExpr = ConstantValue.GetConstantValue(1);
                        }
                        else
                        {
                            elementCountExpr = ConstantValue.GetRandomConstantInt(0, Tree.ValueType.GetElementCount(targetVectorType) - 1);
                        }
                        argExpr = new BinaryExpression(TC, Tree.ValueType.ForPrimitive(Primitive.Int), argExpr, Operator.ForOperation(Operation.BitwiseAnd), elementCountExpr);
                    }
                    else if (argExpr is ConstantValue)
                    {
                        // For vector methods, if the argument type is primitive, add an explicit
                        // cast expression to resolve the ambiguity of the overloaded method.
                        argExpr = new CastExpression(TC, argExpr, argType);
                    }
                }

                // if (argType.PrimitiveType == Primitive.SveMaskPattern)
                // {
                //     argExpr = new CastExpression(TC, argExpr, argType);
                // }

                passingWays.Add(parameter.PassingWay);
                argumentNodes.Add(argExpr);
            }

            return new MethodCallExpression(methodSig.MethodName, argumentNodes, passingWays);
        }

        private Tree.ValueType GetRandomExprType()
        {
            if (PRNG.Decide(TC.Config.StructUsageProbability) && CurrentScope.NumOfStructTypes > 0)
            {
                return CurrentScope.AllStructTypes[PRNG.Next(CurrentScope.NumOfStructTypes)];
            }
            else
            {
                return GetASTUtils().GetRandomValueType();
            }
        }

//        private ExpressionSyntax Annotate(ExpressionSyntax expression, string comment)
//        {
//#if DEBUG
//            string typeName = expression.GetType().Name;
//            if (!_expressionsCount.ContainsKey(typeName))
//            {
//                _expressionsCount[typeName] = 0;
//            }
//            _expressionsCount[typeName]++;
//            return expression.WithTrailingTrivia(TriviaList(Comment($"/* E#{_expressionsCount[typeName]}: {comment} */")));
//#else
//            return expression;
//#endif
//        }

//        private StatementSyntax Annotate(StatementSyntax statement, string comment, int depth)
//        {
//#if DEBUG
//            string typeName = statement.GetType().Name;
//            if (!_statementsCount.ContainsKey(typeName))
//            {
//                _statementsCount[typeName] = 0;
//            }
//            _statementsCount[typeName]++;
//            return statement.WithTrailingTrivia(TriviaList(Comment($"/* {depth}: S#{_statementsCount[typeName]}: {comment} */")));
//#else
//            return statement;
//#endif
//        }


        /// <summary>
        ///     Bulk generate log invocation statements for all <paramref name="variableNames"/>.
        /// </summary>
        /// <param name="variableNames"></param>
        /// <returns></returns>
        public static ArbitraryCodeStatement GetLogInvocationStatement(List<string> variableNames, double logProbability)
        {
            var strBuilder = new StringBuilder();
            // For variable names, just take 10 characters for longer variable names
            variableNames.ForEach(variableToLog =>
            {
                if (PRNG.Decide(logProbability))
                {
                    strBuilder.AppendLine($"Log(\"{variableToLog.Substring(0, Math.Min(variableToLog.Length, 10))}\", {variableToLog});");
                }
            });
            return new ArbitraryCodeStatement(null, strBuilder.ToString());
        }
    }

    public class MethodSignature
    {
        public string MethodName;
        public Tree.ValueType ReturnType;
        public List<MethodParam> Parameters;
        public bool IsLeaf;
        public bool IsNoInline;
        public bool IsVectorGeneric;
        public bool IsVectorMethod;
        public bool IsVectorCreateMethod;

        public MethodSignature(string methodName, bool isLeaf = false, bool isNoInline = false, bool isVectorGeneric = false, bool isVectorMethod = false, bool isVectorCreateMethod = false)
        {
            MethodName = methodName;
            ReturnType = Tree.ValueType.ForVoid();
            Parameters = new List<MethodParam>();
            IsLeaf = isLeaf;
            IsNoInline = isNoInline;
            IsVectorGeneric = isVectorGeneric;
            IsVectorMethod = isVectorMethod;
            IsVectorCreateMethod = isVectorCreateMethod;
        }

        public override bool Equals(object obj)
        {
            if (obj is not MethodSignature otherMethodSig)
            {
                return false;
            }
            return MethodName == otherMethodSig.MethodName &&
                (ReturnType.Equals(otherMethodSig.ReturnType)) &&
                Parameters.Count == otherMethodSig.Parameters.Count &&
                Enumerable.Range(0, Parameters.Count)
                    .All(pIndex => (Parameters[pIndex].ParamType.Equals(otherMethodSig.Parameters[pIndex].ParamType) &&
                                    (Parameters[pIndex].PassingWay == otherMethodSig.Parameters[pIndex].PassingWay)));
        }

        public override int GetHashCode()
        {
            int hashCode = 0;
            foreach (var p in Parameters)
            {
                hashCode ^= p.ParamType.GetHashCode();
            }
            return MethodName.GetHashCode() ^ ReturnType.GetHashCode() ^ hashCode;
        }

        public override string ToString()
        {
            var paramList = Parameters.Select(p => $"{(p.PassingWay == ParamValuePassing.None ? "" : Enum.GetName(typeof(ParamValuePassing), p.PassingWay))} {p.ParamType}");
            return $"{ReturnType} {MethodName}({string.Join(", ", paramList)})";
        }
    }

    public class MethodParam
    {
        public string ParamName;
        public Tree.ValueType ParamType;
        public ParamValuePassing PassingWay;

        public override string ToString()
        {
            return $"{Enum.GetName(typeof(ParamValuePassing), PassingWay)} {ParamType} {ParamName}";
        }
    }

    public enum ParamValuePassing
    {
        None,
        //TODO-future: need to add ability of marking variables readonly
        //In,
        Out,
        Ref
    };

    public class TestLeafMethod : TestMethod
    {
        private Tree.ValueType _returnType;
        public TestLeafMethod(TestClass enclosingClass, string methodName, Tree.ValueType returnType)
            : base(enclosingClass, methodName)
        {
            _returnType = returnType;
        }

        protected override void PopulateMethodSignature()
        {
            MethodSignature = new MethodSignature(Name)
            {
                Parameters = new List<MethodParam>(),
                ReturnType = _returnType,
                IsLeaf = true,
                IsNoInline = PRNG.Decide(0.5)
            };
        }

        public override MethodDeclStatement Generate()
        {
            GetASTUtils().EnterLeafMethod();

            // return statement
            PopulateMethodSignature();
            List<Statement> methodBody = new List<Statement>();

            methodBody.Add(StatementHelper(StmtKind.ReturnStatement, 0));

            RegisterMethod(MethodSignature);

            GetASTUtils().LeaveLeafMethod();

            return new MethodDeclStatement(TC, MethodSignature, methodBody);
        }
    }
}
