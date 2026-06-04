// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Antigen.Tree
{
    public enum ExprKind
    {
        LiteralExpression,
        VariableExpression,
        BinaryOpExpression,
        AssignExpression,
        MethodCallExpression,
    }

    public enum StmtKind
    {
        VariableDeclaration,
        IfElseStatement,
        AssignStatement,
        ForStatement,
        DoWhileStatement,
        WhileStatement,
        ReturnStatement,
        TryCatchFinallyStatement,
        SwitchStatement,
        MethodCallStatement,
    }
}
