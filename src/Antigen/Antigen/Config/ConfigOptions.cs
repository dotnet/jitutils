using Antigen.Tree;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Reflection;

namespace Antigen.Config
{
    public class ConfigOptions
    {
        private const string WeightSuffix = "Weight";

        public string Name;

        public bool UseSve;

        // Test general configuration
        public int MaxStmtDepth = 4;
        public int MaxExprDepth = 3;

        public int MethodCount = 3;
        public int MaxStatementCount = 2;
        public int VariablesCount = 8;
        public int StructCount = 2;

        // More controls
        public int NumOfAttemptsForExpression = 5;

        // Expression weights
        public double LiteralWeight = 0.025;
        public double VariableWeight = 0.3;
        public double BinaryOpWeight = 1;
        public double MethodCallWeight = 0.15;
        public double AssignWeight = 0.4;

        // Statement weights
        public double VariableDeclarationWeight = 0.03; //TODO: Reduce this and add aliases when we see this.
        public double IfElseStatementWeight = 0.2;
        public double AssignStatementWeight = 0.6;
        public double ForStatementWeight = 0.1;
        public double DoWhileStatementWeight = 0.05;
        public double WhileStatementWeight = 0.08;
        public double TryCatchFinallyStatementWeight = 0.08;
        public double SwitchStatementWeight = 0.1;
        public double MethodCallStatementWeight = 0.4;

        // Type weights
        public double BooleanWeight = 0.3;
        public double ByteWeight = 0.4;
        public double CharWeight = 0.03;
        public double DecimalWeight = 0.5;
        public double DoubleWeight = 0.4;
        public double ShortWeight = 0.4;
        public double IntWeight = 0.45;
        public double LongWeight = 0.48;
        public double SByteWeight = 0.3;
        public double FloatWeight = 0.6;
        public double StringWeight = 0.03;
        public double UShortWeight = 0.4;
        public double UIntWeight = 0.45;
        public double ULongWeight = 0.6;
        public double SveMaskPatternWeight = 0;

        // Vector weights
        public double Vector64_ByteWeight = 0.1;
        public double Vector64_SByteWeight = 0.1;
        public double Vector64_ShortWeight = 0.1;
        public double Vector64_UShortWeight = 0.1;
        public double Vector64_IntWeight = 0.1;
        public double Vector64_UIntWeight = 0.1;
        public double Vector64_LongWeight = 0.1;
        public double Vector64_ULongWeight = 0.1;
        public double Vector64_FloatWeight = 0.1;
        public double Vector64_DoubleWeight = 0.1;

        public double Vector128_ByteWeight = 0.1;
        public double Vector128_SByteWeight = 0.1;
        public double Vector128_ShortWeight = 0.1;
        public double Vector128_UShortWeight = 0.1;
        public double Vector128_IntWeight = 0.1;
        public double Vector128_UIntWeight = 0.1;
        public double Vector128_LongWeight = 0.1;
        public double Vector128_ULongWeight = 0.1;
        public double Vector128_FloatWeight = 0.1;
        public double Vector128_DoubleWeight = 0.1;

        public double Vector256_ByteWeight = 0.1;
        public double Vector256_SByteWeight = 0.1;
        public double Vector256_ShortWeight = 0.1;
        public double Vector256_UShortWeight = 0.1;
        public double Vector256_IntWeight = 0.1;
        public double Vector256_UIntWeight = 0.1;
        public double Vector256_LongWeight = 0.1;
        public double Vector256_ULongWeight = 0.1;
        public double Vector256_FloatWeight = 0.1;
        public double Vector256_DoubleWeight = 0.1;

        public double Vector512_ByteWeight = 0.15;
        public double Vector512_SByteWeight = 0.15;
        public double Vector512_ShortWeight = 0.15;
        public double Vector512_UShortWeight = 0.15;
        public double Vector512_IntWeight = 0.15;
        public double Vector512_UIntWeight = 0.15;
        public double Vector512_LongWeight = 0.15;
        public double Vector512_ULongWeight = 0.15;
        public double Vector512_FloatWeight = 0.15;
        public double Vector512_DoubleWeight = 0.15;

        public double Vector_ByteWeight = 0.15;
        public double Vector_SByteWeight = 0.15;
        public double Vector_ShortWeight = 0.15;
        public double Vector_UShortWeight = 0.15;
        public double Vector_IntWeight = 0.15;
        public double Vector_UIntWeight = 0.15;
        public double Vector_LongWeight = 0.15;
        public double Vector_ULongWeight = 0.15;
        public double Vector_FloatWeight = 0.15;
        public double Vector_DoubleWeight = 0.15;

        public double Vector2Weight = 0.1;
        public double Vector3Weight = 0.1;
        public double Vector4Weight = 0.1;

        // Operator weights
        public double UnaryPlusWeight = 1;
        public double UnaryMinusWeight = 1;
        public double PreIncrementWeight = 1;
        public double PreDecrementWeight = 1;
        public double PostIncrementWeight = 1;
        public double PostDecrementWeight = 1;
        public double LogicalNotWeight = 1;
        public double BitwiseNotWeight = 1;
        public double TypeOfWeight = 1;

        public double AddWeight = 0.9;
        public double SubtractWeight = 0.5;
        public double MultiplyWeight = 0.5;
        public double DivideWeight = 0.4;
        public double ModuloWeight = 0.5;
        public double LeftShiftWeight = 0.6;
        public double RightShiftWeight = 0.4;

        public double SimpleAssignmentWeight = 0.9;
        public double AddAssignmentWeight = 0.5;
        public double SubtractAssignmentWeight = 0.6;
        public double MultiplyAssignmentWeight = 0.5;
        public double DivideAssignmentWeight = 0.5;
        public double ModuloAssignmentWeight = 0.3;
        public double LeftShiftAssignmentWeight = 0.5;
        public double RightShiftAssignmentWeight = 0.6;

        public double LogicalAndWeight = 0.5;
        public double LogicalOrWeight = 0.4;

        public double BitwiseAndWeight = 0.45;
        public double BitwiseOrWeight = 0.35;
        public double ExclusiveOrWeight = 0.45;

        public double AndAssignmentWeight = 0.54;
        public double OrAssignmentWeight = 0.59;
        public double ExclusiveOrAssignmentWeight = 0.68;

        public double LessThanWeight = 0.6;
        public double LessThanOrEqualWeight = 0.6;
        public double GreaterThanWeight = 0.51;
        public double GreaterThanOrEqualWeight = 0.51;
        public double EqualsWeight = 0.8;
        public double NotEqualsWeight = 0.8;

        public double VectorAddWeight = 0.3;
        public double VectorSubtractWeight = 0.4;
        public double VectorMultiplyWeight = 0.45;
        public double VectorDivideWeight = 0.2;
        public double VectorBitwiseAndWeight = 0.37;
        public double VectorBitwiseOrWeight = 0.48;
        public double VectorExclusiveOrWeight = 0.34;
        public double VectorUnaryPlusWeight = 0.33;
        public double VectorUnaryMinusWeight = 0.12;
        public double VectorBitwiseNotWeight = 0.29;
        public double VectorSimpleAssignmentWeight = 0.364;
        public double VectorAddAssignmentWeight = 0.341;
        public double VectorSubtractAssignmentWeight = 0.24;
        public double VectorMultiplyAssignmentWeight = 0.63;
        public double VectorDivideAssignmentWeight = 0.2;


        /// <summary>
        ///     Probablity of removing loop parameters -- see comments on BoundParameters in ForStatement 
        /// </summary>
        public double LoopParametersRemovalProbability = 0.1;

        /// <summary>
        ///     Probability of having forward loop whose induction variable always increases
        /// </summary>
        public double LoopForwardProbability = 0.7;

        /// <summary>
        ///     Probabilty whether loop induction variable should start from loop invariant value or +/- 3
        /// </summary>
        public double LoopStartFromInvariantProbabilty = 0.5;

        /// <summary>
        ///     Probabilty whether loop step should happen pre or post break condition
        /// </summary>
        public double LoopStepPreBreakCondProbability = 0.5;

        /// <summary>
        ///     Probability of usage of array.length vs. loopinvariant
        /// </summary>
        public double UseLoopInvariantVariableProbability = 1.0; // Always have 1.0 for now to stop making .length as invariant because that could lead to long loops

        /// <summary>
        ///     This will put loop condition at the end of the loop body.
        ///     Always use ContinueStatementWeight = 0 if this is true (see lessmath_no_continue.xml), otherwise there is a chance of infinite loop here.
        ///     This is a quick fix for now.  We need to come up with a better solution for this for IE11.
        /// </summary>
        public bool AllowLoopCondAtEnd = false;

        /// <summary>
        ///     Nested struct probability
        /// </summary>
        public double NestedStructProbability = 0.2;

        /// <summary>
        ///     Field count of structs
        /// </summary>
        public int StructFieldCount = 4;

        /// <summary>
        ///     Nested struct depth
        /// </summary>
        public int NestedStructDepth = 2;

        /// <summary>
        ///     Probability of field type being struct
        /// </summary>
        public double StructFieldTypeProbability = 0.2;

        /// <summary>
        ///     Struct variable declaration which is alias of existing variable
        /// </summary>
        public double StructAliasProbability = 0.2;

        /// <summary>
        ///     Log local variables in a block probability
        /// </summary>
        public double LocalVariablesLogProbability = 0.5;

        /// <summary>
        ///     Number of catch clauses
        /// </summary>
        public int CatchClausesCount = 2;

        /// <summary>
        ///     Finally clause probabiliy
        /// </summary>
        public double FinallyClauseProbability = 0.5;

        /// <summary>
        ///     Parameter passing probability - None.
        /// </summary>
        public double ParamPassingNoneProbability = 0.6;

        /// <summary>
        ///     Parameter passing probability - Ref.
        /// </summary>
        public double ParamPassingRefProbability = 0.2;

        /// <summary>
        ///     Parameter passing probability - Out.
        /// </summary>
        public double ParamPassingOutProbability = 0.2;

        /// <summary>
        ///     Leaf methods NoInline probability.
        /// </summary>
        public double LeafMethodsNoInlineProbability = 0.4;

        /// <summary>
        ///     Maximum no. of method parameters
        /// </summary>
        public int MaxMethodParametersCount = 10;

        /// <summary>
        ///     Struct usage probability
        /// </summary>
        public double StructUsageProbability = 0.4;

        /// <summary>
        ///     Maximum number of case counts in switch statement
        /// </summary>
        public int MaxCaseCounts = 4;

        /// <summary>
        ///     Probability of using CSE.
        /// </summary>
        public double CSEUsageProbability = 0.3;

        /// <summary>
        ///     Avx/Avx2 methods probability
        /// </summary>
        public double TraditionalMethodsProbability = 0.089;

        /// <summary>
        ///     Avx/Avx2 methods probability
        /// </summary>
        public double AvxMethodsProbability = 0.29;

        /// <summary>
        ///     SSE* methods probability
        /// </summary>
        public double SSEMethodsProbability = 0.198;

        /// <summary>
        ///     AdvSimd methods probability
        /// </summary>
        public double AdvSimdMethodsProbability = 0.35;

        /// <summary>
        ///     AdvSimd methods probability
        /// </summary>
        public double SveMethodsProbability = 0.65;

        /// <summary>
        ///     Probability in which vector methods will be included.
        /// </summary>
        public double VectorDataProbability = 0.5;

        /// <summary>
        ///     Number of methods to be included. Only relevant ifVectorMethodsProbability is non-zero.
        /// </summary>
        public double RegisterIntrinsicMethodsProbability = 0.4;

        /// <summary>
        ///     Number of methods to be invoked from Method0. Only relevant ifVectorMethodsProbability is non-zero.
        /// </summary>
        public double InvokeIntrinsicMethodsProbability = 0.001;

        /// <summary>
        ///     Probability of storing the method call results in a variable.
        /// </summary>
        public double StoreIntrinsicMethodCallResultProbability = 0.7;

        public override string ToString()
        {
            return Name;
        }

        public double Lookup(Tree.ValueType type)
        {
            return type.IsVectorType ? Lookup(type.VectorType + WeightSuffix) : Lookup(type.PrimitiveType + WeightSuffix);
        }

        public double Lookup(Operator oper)
        {
            string str = Enum.GetName(typeof(Operation), oper.Oper);
            return Lookup(str + WeightSuffix);
        }

        public double Lookup(StmtKind stmt)
        {
            string str = Enum.GetName(typeof(StmtKind), stmt);
            return Lookup(str + WeightSuffix);
        }

        public double Lookup(ExprKind expr)
        {
            string str = Enum.GetName(typeof(ExprKind), expr);
            str = str.Replace("Expression", "");
            return Lookup(str + WeightSuffix);
        }

        private double Lookup(string str)
        {
            FieldInfo target = typeof(ConfigOptions).GetField(str, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (target == null)
            {
                Console.WriteLine("ERROR: didn't find weight for {0}; using 0 instead", str);
                return 0;
            }

            return (double)target.GetValue(this);
        }
    }
}
