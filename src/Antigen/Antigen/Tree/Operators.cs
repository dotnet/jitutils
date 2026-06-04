using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Linq;

namespace Antigen.Tree
{
    public enum OpFlags
    {
        Comparison = 0x1,
        Binary = 0x2,
        Unary = 0x4,
        Divide = 0x8,
        Shift = 0x10,
        IncrementDecrement = 0x20,
        Math = 0x40,
        Assignment = 0x80,
        Logical = 0x100,
        Bitwise = 0x200,
        String = 0x400,
    }

    public enum Operation
    {
        UnaryPlus,
        UnaryMinus,
        PreIncrement,
        PreDecrement,
        PostIncrement,
        PostDecrement,
        LogicalNot,
        BitwiseNot,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        LeftShift,
        RightShift,
        SimpleAssignment,
        AddAssignment,
        SubtractAssignment,
        MultiplyAssignment,
        DivideAssignment,
        ModuloAssignment,
        LeftShiftAssignment,
        RightShiftAssignment,
        LogicalAnd,
        LogicalOr,
        BitwiseAnd,
        BitwiseOr,
        ExclusiveOr,
        AndAssignment,
        OrAssignment,
        ExclusiveOrAssignment,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Equals,
        NotEquals,
        // vector operations
        VectorAdd,
        VectorSubtract,
        VectorMultiply,
        VectorDivide,
        VectorBitwiseAnd,
        VectorBitwiseOr,
        VectorExclusiveOr,
        VectorUnaryPlus,
        VectorUnaryMinus,
        VectorBitwiseNot,
        VectorSimpleAssignment,
        VectorAddAssignment,
        VectorSubtractAssignment,
        VectorMultiplyAssignment,
        VectorDivideAssignment,

    }

    public struct Operator
    {
        public OpFlags Flags;
        public Primitive InputTypes;
        public Primitive ReturnType;
        public bool IsVectorIntrinsics;
        public bool IsVectorNumerics;
        public Operation Oper;
        private readonly string renderText;
        public readonly bool IsVectorOper;

        public bool HasFlag(OpFlags flag)
        {
            bool val = (Flags & flag) != 0;
            return val;
        }

        public bool HasReturnType(ValueType valueType)
        {
            if (valueType.IsVectorType)
            {
                return valueType.AllowedVector(this);
            }
            else
            {
                return (ReturnType & valueType.PrimitiveType) != 0;
            }
        }

        public bool HasAnyPrimitiveType()
        {
            return (ReturnType & Primitive.Any) != 0;
        }

        public bool HasAnyVectorType()
        {
            return IsVectorIntrinsics || IsVectorNumerics;
        }

        private static readonly List<Operator> operators = new List<Operator>()
        {
            new Operator(Operation.UnaryPlus,                "+",    "+i",  Primitive.Numeric, Primitive.Numeric,          OpFlags.Unary | OpFlags.Math),
            new Operator(Operation.UnaryMinus,               "-",    "-i",  Primitive.Numeric, Primitive.Numeric,          OpFlags.Unary | OpFlags.Math),
            new Operator(Operation.PreIncrement,             "++",   "++i", Primitive.Numeric | Primitive.Char, Primitive.Numeric| Primitive.Char,           OpFlags.Unary | OpFlags.Math | OpFlags.IncrementDecrement),
            new Operator(Operation.PreDecrement,             "--",   "--i", Primitive.Numeric| Primitive.Char, Primitive.Numeric| Primitive.Char,           OpFlags.Unary | OpFlags.Math | OpFlags.IncrementDecrement),
            new Operator(Operation.PostIncrement,            "++",   "i++", Primitive.Numeric| Primitive.Char, Primitive.Numeric| Primitive.Char,          OpFlags.Unary | OpFlags.Math | OpFlags.IncrementDecrement),
            new Operator(Operation.PostDecrement,            "--",   "i--", Primitive.Numeric | Primitive.Char, Primitive.Numeric| Primitive.Char,          OpFlags.Unary | OpFlags.Math | OpFlags.IncrementDecrement),
            new Operator(Operation.LogicalNot,               "!",    "!i",  Primitive.Boolean, Primitive.Boolean,         OpFlags.Unary | OpFlags.Logical),
            new Operator(Operation.BitwiseNot,               "~",    "~i",  Primitive.Integer | Primitive.Char, Primitive.Integer,         OpFlags.Unary | OpFlags.Math | OpFlags.Bitwise),
            //new Operator(Operation.TypeOf,        "typeof(i)",           OpFlags.Unary),

            new Operator(Operation.Add,                      "+",    "i+j",   Primitive.Numeric, Primitive.Numeric,           OpFlags.Binary | OpFlags.Math | OpFlags.String),
            new Operator(Operation.Add,                      "+",    "i concat j",   Primitive.String, Primitive.String,           OpFlags.Binary | OpFlags.String),
            new Operator(Operation.Subtract,                 "-",    "i-j",  Primitive.Numeric, Primitive.Numeric,           OpFlags.Binary | OpFlags.Math),
            new Operator(Operation.Multiply,                 "*",    "i*j",  Primitive.Numeric, Primitive.Numeric,           OpFlags.Binary | OpFlags.Math),
            new Operator(Operation.Divide,                   "/",    "i/j",   Primitive.Numeric, Primitive.Numeric,          OpFlags.Binary | OpFlags.Math | OpFlags.Divide),
            new Operator(Operation.Modulo,                   "%",    "i%j",    Primitive.Numeric, Primitive.Numeric,        OpFlags.Binary | OpFlags.Math | OpFlags.Divide),
            new Operator(Operation.LeftShift,                "<<",   "i<<j", /*TODO-future: different for lhs, rhs*/  Primitive.SignedInteger | Primitive.Char, Primitive.SignedInteger,            OpFlags.Binary | OpFlags.Math | OpFlags.Shift),
            new Operator(Operation.RightShift,               ">>",   "i>>j",  Primitive.SignedInteger| Primitive.Char, Primitive.SignedInteger,            OpFlags.Binary | OpFlags.Math | OpFlags.Shift),

            new Operator(Operation.SimpleAssignment,         "=",      "i=j",  Primitive.Any | Primitive.Struct, Primitive.Any | Primitive.Struct,    OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            new Operator(Operation.AddAssignment,            "+=",     "i+=j",  Primitive.Numeric | Primitive.String, Primitive.Numeric | Primitive.String,     OpFlags.Binary | OpFlags.Math | OpFlags.Assignment | OpFlags.String),
            new Operator(Operation.SubtractAssignment,       "-=",    "i-=j", Primitive.Numeric, Primitive.Numeric,     OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            new Operator(Operation.MultiplyAssignment,       "*=",    "i*=j",  Primitive.Numeric, Primitive.Numeric,    OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            new Operator(Operation.DivideAssignment,         "/=",    "i/=j", Primitive.Numeric, Primitive.Numeric,     OpFlags.Binary | OpFlags.Math | OpFlags.Divide | OpFlags.Assignment),
            new Operator(Operation.ModuloAssignment,         "%=",    "i%=j", Primitive.Numeric, Primitive.Numeric,    OpFlags.Binary | OpFlags.Math | OpFlags.Divide | OpFlags.Assignment),
            new Operator(Operation.LeftShiftAssignment,      "<<=",   "i<<=j", Primitive.Integer, Primitive.Integer,   OpFlags.Binary | OpFlags.Math | OpFlags.Shift | OpFlags.Assignment),
            new Operator(Operation.RightShiftAssignment,     ">>=",  "i>>=j", Primitive.Integer, Primitive.Integer,    OpFlags.Binary | OpFlags.Math | OpFlags.Shift | OpFlags.Assignment),

            new Operator(Operation.LogicalAnd,               "&&",   "i&&j", Primitive.Boolean,Primitive.Boolean,            OpFlags.Binary | OpFlags.Logical),
            new Operator(Operation.LogicalOr,                "||",   "i||j",  Primitive.Boolean,Primitive.Boolean,           OpFlags.Binary | OpFlags.Logical),

            //TODO: below can also be logical as per https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/operators/boolean-logical-operators
            new Operator(Operation.BitwiseAnd,               "&",    "i&j",   Primitive.Integer | Primitive.Char, Primitive.Integer,           OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise),
            new Operator(Operation.BitwiseOr,                "|",    "i|j",    Primitive.Integer | Primitive.Char, Primitive.Integer,          OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise),
            new Operator(Operation.ExclusiveOr,              "^",    "i^j",    Primitive.Integer | Primitive.Char, Primitive.Integer,         OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise),

            new Operator(Operation.AndAssignment,            "&=",   "i&=j",   Primitive.Integer,Primitive.Integer,         OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise | OpFlags.Assignment),
            new Operator(Operation.OrAssignment,             "|=",   "i|=j",   Primitive.Integer,Primitive.Integer,        OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise | OpFlags.Assignment),
            new Operator(Operation.ExclusiveOrAssignment,    "^=",   "i^=j", Primitive.Integer,Primitive.Integer,   OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise | OpFlags.Assignment),

            new Operator(Operation.LessThan,                 "<",    "i<j",    Primitive.Numeric  | Primitive.Char, Primitive.Boolean,         OpFlags.Binary | OpFlags.Comparison),
            new Operator(Operation.LessThanOrEqual,          "<=",   "i<=j",  Primitive.Numeric | Primitive.Char, Primitive.Boolean,        OpFlags.Binary | OpFlags.Comparison),
            new Operator(Operation.GreaterThan,              ">",    "i>j",    Primitive.Numeric | Primitive.Char, Primitive.Boolean,       OpFlags.Binary | OpFlags.Comparison),
            new Operator(Operation.GreaterThanOrEqual,       ">=",   "i>=j", Primitive.Numeric | Primitive.Char, Primitive.Boolean,        OpFlags.Binary | OpFlags.Comparison),
            new Operator(Operation.Equals,                   "==",   "i==j",     Primitive.Any, Primitive.Boolean,         OpFlags.Binary | OpFlags.Comparison | OpFlags.String),
            new Operator(Operation.NotEquals,                "!=",   "i!=j",      Primitive.Any, Primitive.Boolean,         OpFlags.Binary | OpFlags.Comparison | OpFlags.String),

            // vector operators
            new Operator(Operation.VectorAdd,                      "+",    "i+j",   true, true,            OpFlags.Binary | OpFlags.Math),
            new Operator(Operation.VectorSubtract,                 "-",    "i-j",   true, true,            OpFlags.Binary | OpFlags.Math),
            new Operator(Operation.VectorMultiply,                 "*",    "i*j",   true, true,            OpFlags.Binary | OpFlags.Math),
            //new Operator(Operation.VectorDivide,                   "/",    "i/j",   true, true,            OpFlags.Binary | OpFlags.Math),
            new Operator(Operation.VectorBitwiseAnd,               "&",    "i&j",   true, false,           OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise),
            new Operator(Operation.VectorBitwiseOr,                "|",    "i|j",   true, false,           OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise),
            new Operator(Operation.VectorExclusiveOr,              "^",    "i^j",   true, false,           OpFlags.Binary | OpFlags.Math | OpFlags.Bitwise),
            //new Operator(Operation.VectorEquals,                   "==",   "i==j",  VectorType.VectorT, Primitive.Boolean,            OpFlags.Binary | OpFlags.Comparison),
            //new Operator(Operation.VectorNotEquals,                "!=",   "i!=j",  VectorType.VectorT, Primitive.Boolean,            OpFlags.Binary | OpFlags.Comparison),
            new Operator(Operation.VectorUnaryPlus,                "+",    "+i",    true, false,           OpFlags.Unary | OpFlags.Math),
            new Operator(Operation.VectorUnaryMinus,               "-",    "-i",    true, false,           OpFlags.Unary | OpFlags.Math),
            new Operator(Operation.VectorBitwiseNot,               "~",    "~i",    true, false,           OpFlags.Unary | OpFlags.Math | OpFlags.Bitwise),
            new Operator(Operation.VectorSimpleAssignment,         "=",      "i=j",  true, true,    OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            new Operator(Operation.VectorAddAssignment,            "+=",     "i+=j", true, true,     OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            new Operator(Operation.VectorSubtractAssignment,       "-=",    "i-=j",  true, true,     OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            new Operator(Operation.VectorMultiplyAssignment,       "*=",    "i*=j",  true, true,    OpFlags.Binary | OpFlags.Math | OpFlags.Assignment),
            //new Operator(Operation.VectorDivideAssignment,         "/=",    "i/=j",  true, true,     OpFlags.Binary | OpFlags.Math | OpFlags.Divide | OpFlags.Assignment),
        };

        private Operator(Operation oper, string operatorText, string operation, Primitive inputTypes, Primitive outputType, OpFlags flags)
        {
            Oper = oper;
            renderText = operatorText;
            InputTypes = inputTypes;
            ReturnType = outputType;
            Flags = flags;
            IsVectorOper = false;
            IsVectorIntrinsics = false;
            IsVectorNumerics = false;
        }

        private Operator(Operation oper, string operatorText, string operation, bool isVectorIntrinsics, bool isVectorNumerics, OpFlags flags)
        {
            Oper = oper;
            renderText = operatorText;
            IsVectorIntrinsics = isVectorIntrinsics;
            IsVectorNumerics = isVectorNumerics;
            Flags = flags;
            IsVectorOper = true;
        }

        public static List<Operator> GetOperators()
        {
            return operators;
        }

        public static Operator ForOperation(Operation operKind)
        {
            return operators.First(o => o.Oper == operKind);
        }

        public override string ToString()
        {
            return renderText;
        }
    }
}
