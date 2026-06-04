// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Antigen.Expressions;
using Antigen.Statements;
using Antigen.Tree;
using Microsoft.CodeAnalysis;
using ValueType = Antigen.Tree.ValueType;

namespace Antigen
{
    /// <summary>
    ///     Denotes the class to generate.
    /// </summary>
    public partial class TestClass
    {
        public Scope ClassScope { get; private set; }
        public string ClassName;
        public TestCase TC { get; private set; }
        public Stack<Scope> ScopeStack { get; private set; }
        private List<Weights<MethodSignature>> _methods { get; set; }
        private int _variableId;

        public string GetVariableName(Tree.ValueType variableType)
        {
            return variableType.VariableNameHint() + "_" + _variableId++;
        }

        public AstUtils GetASTUtils()
        {
            return TC.AstUtils;
        }

        public TestClass(TestCase tc, string className)
        {
            ScopeStack = new Stack<Scope>();
            ClassScope = new Scope(tc);
            ClassName = className;
            TC = tc;
            _methods = new List<Weights<MethodSignature>>();
            _variableId = 0;
        }

        private void GenerateVectorMethods()
        {
            bool addAvx = PRNG.Decide(TC.Config.AvxMethodsProbability);
            bool addSse = PRNG.Decide(TC.Config.SSEMethodsProbability);
            bool addTraditional = PRNG.Decide(TC.Config.TraditionalMethodsProbability);
            bool addSve = TC.Config.UseSve;
            bool addAdvsimd = TC.Config.UseSve || PRNG.Decide(TC.Config.AdvSimdMethodsProbability);

            // Register all the vector create methods
            foreach (var vectorMethod in VectorHelpers.GetAllVectorMethods())
            {
                bool isVectorT = vectorMethod.MethodName.StartsWith("Vector.");
                bool isVector2 = vectorMethod.MethodName.StartsWith("Vector2.");
                bool isVector3 = vectorMethod.MethodName.StartsWith("Vector3.");
                bool isVector4 = vectorMethod.MethodName.StartsWith("Vector4.");
                bool isVector64 = vectorMethod.MethodName.StartsWith("Vector64.");
                bool isVector128 = vectorMethod.MethodName.StartsWith("Vector128.");
                bool isVector256 = vectorMethod.MethodName.StartsWith("Vector256.");
                bool isVector512 = vectorMethod.MethodName.StartsWith("Vector512.");

                bool isSve = vectorMethod.MethodName.StartsWith("Sve.");
                bool isAdvSimd = vectorMethod.MethodName.StartsWith("AdvSimd.");
                bool isAdvSimdx64 = vectorMethod.MethodName.StartsWith("AdvSimd.X64.");

                bool isAvx = vectorMethod.MethodName.StartsWith("Avx.");
                bool isAvx2 = vectorMethod.MethodName.StartsWith("Avx2.");
                bool isAvx512BW = vectorMethod.MethodName.StartsWith("Avx512BW.");
                bool isAvx512CD = vectorMethod.MethodName.StartsWith("Avx512CD.");
                bool isAvx512DQ = vectorMethod.MethodName.StartsWith("Avx512DQ.");
                bool isAvx512F = vectorMethod.MethodName.StartsWith("Avx512F.");
                bool isAvx512Vbmi = vectorMethod.MethodName.StartsWith("Avx512Vbmi.");

                bool isSse = vectorMethod.MethodName.StartsWith("Sse.");
                bool isSse2 = vectorMethod.MethodName.StartsWith("Sse2.");
                bool isSse3 = vectorMethod.MethodName.StartsWith("Sse3.");
                bool isSse41 = vectorMethod.MethodName.StartsWith("Sse41.");
                bool isSse42 = vectorMethod.MethodName.StartsWith("Sse42.");
                bool isSsse3 = vectorMethod.MethodName.StartsWith("Ssse3.");

                bool isBmi1 = vectorMethod.MethodName.StartsWith("Bmi1.");
                bool isBmi1x64 = vectorMethod.MethodName.StartsWith("Bmi1.X64.");
                bool isBmi2 = vectorMethod.MethodName.StartsWith("Bmi2.");
                bool isBmi2x64 = vectorMethod.MethodName.StartsWith("Bmi2.X64.");
                bool isFma = vectorMethod.MethodName.StartsWith("Fma.");
                bool isLzcnt = vectorMethod.MethodName.StartsWith("Lzcnt.");
                bool isLzcntx64 = vectorMethod.MethodName.StartsWith("Lzcnt.X64.");
                bool isPclmulqdq = vectorMethod.MethodName.StartsWith("Pclmulqdq.");
                bool isPopcnt = vectorMethod.MethodName.StartsWith("Popcnt.");
                bool isPopcntx64 = vectorMethod.MethodName.StartsWith("Popcnt.X64.");

                string vectorsInMethod = VectorHelpers.GetVectorList(vectorMethod.ToString());
                if (vectorsInMethod != null)
                {
                    // If a parameter is one of the vector that is not supported, then skip this method
                    if (TC.Config.UseSve)
                    {
                        if (vectorsInMethod.Contains("256") || vectorsInMethod.Contains("512"))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (!Vector64.IsHardwareAccelerated && vectorsInMethod.Contains("64")) continue;
                        if (!Vector128.IsHardwareAccelerated && vectorsInMethod.Contains("128")) continue;
                        if (!Vector256.IsHardwareAccelerated && vectorsInMethod.Contains("256")) continue;
                        if (!Vector512.IsHardwareAccelerated && vectorsInMethod.Contains("512")) continue;
                    }
                }

                if (isVectorT || isVector2 || isVector3 || isVector4)
                {
                    RegisterMethod(vectorMethod);
                    continue;
                }

                else if (TC.Config.UseSve)
                {
                    // If force-sve, then just add Sve/AdvSimd methods
                    if (isSve || isAdvSimd || isVector64 || isVector128)
                    {
                        RegisterMethod(vectorMethod);
                    }
                }
                else
                {
                    if (
                        (isVector64 && Vector64.IsHardwareAccelerated)
                        ||
                        (isVector128 && Vector128.IsHardwareAccelerated)
                        ||
                        (isVector256 && Vector256.IsHardwareAccelerated)
                        ||
                        (isVector512 && Vector512.IsHardwareAccelerated)
                        ||
                        (addSve && Sve.IsSupported)
                        ||
                        (addAdvsimd &&
                        ((isAdvSimd && AdvSimd.IsSupported) ||
                        (isAdvSimdx64 && AdvSimd.Arm64.IsSupported)))
                        ||
                        (addAvx &&
                        ((isAvx && Avx.IsSupported) ||
                        (isAvx2 && Avx2.IsSupported) ||
                        (isAvx512BW && Avx512BW.IsSupported) ||
                        (isAvx512CD && Avx512CD.IsSupported) ||
                        (isAvx512DQ && Avx512DQ.IsSupported) ||
                        (isAvx512F && Avx512F.IsSupported) ||
                        (isAvx512Vbmi && Avx512Vbmi.IsSupported)))
                        ||
                        (addSse &&
                        ((isSse && Sse.IsSupported) ||
                        (isSse2 && Sse2.IsSupported) ||
                        (isSse3 && Sse3.IsSupported) ||
                        (isSse41 && Sse41.IsSupported) ||
                        (isSse42 && Sse42.IsSupported) ||
                        (isSsse3 && Ssse3.IsSupported)))
                        ||
                        (addTraditional &&
                        ((isBmi1 && Bmi1.IsSupported) ||
                        (isBmi1x64 && Bmi1.X64.IsSupported) ||
                        (isBmi2 && Bmi2.IsSupported) ||
                        (isBmi2x64 && Bmi2.X64.IsSupported) ||
                        (isFma && Fma.IsSupported) ||
                        (isLzcnt && Lzcnt.IsSupported) ||
                        (isLzcntx64 && Lzcnt.X64.IsSupported) ||
                        (isPclmulqdq && Pclmulqdq.IsSupported) ||
                        (isPopcnt && Popcnt.IsSupported) ||
                        (isPopcntx64 && Popcnt.X64.IsSupported)))
                        )
                    {
                        RegisterMethod(vectorMethod);
                    }
                }
            }
        }

        /// <summary>
        ///     Register method
        /// </summary>
        /// <param name="methodSignature"></param>
        public void RegisterMethod(MethodSignature methodSignature)
        {
            double methodProb = (double)PRNG.Next(1, 100) / 100;
            if (methodSignature.IsVectorCreateMethod)
            {
                // Further reduce the probability of vector create methods because
                // they are anyway used to create the vectors.
                methodProb /= 10;
            }
            _methods.Add(new Weights<MethodSignature>(methodSignature, methodProb));
        }

        /// <summary>
        ///     Returns all non-leaf methods
        /// </summary>
        public IEnumerable<Weights<MethodSignature>> AllNonLeafMethods => _methods.Where(m => !m.Data.IsLeaf);

        /// <summary>
        ///     Returns all leaf methods
        /// </summary>
        public IEnumerable<Weights<MethodSignature>> AllLeafMethods => _methods.Where(m => m.Data.IsLeaf);

        /// <summary>
        ///     Returns all vector methods
        /// </summary>
        public IEnumerable<Weights<MethodSignature>> AllVectorMethods => _methods.Where(m => m.Data.IsVectorMethod);

        /// <summary>
        ///     Returns all vector create methods
        /// </summary>
        public IEnumerable<Weights<MethodSignature>> AllVectorCreateMethods => _methods.Where(m => m.Data.IsVectorCreateMethod);

        /// <summary>
        ///     Get random method that returns specfic returnType. Null if no such
        ///     method is generated yet.
        /// </summary>
        public MethodSignature GetRandomMethod(Tree.ValueType returnType)
        {
            var matchingMethods = _methods.Where(m => m.Data.ReturnType.Equals(returnType)).ToList();
            if (matchingMethods.Count == 0)
            {
                return null;
            }
            return PRNG.WeightedChoice(matchingMethods);
        }

        /// <summary>
        ///     Get random vector create method that returns specific returnType.
        /// </summary>
        /// <param name="returnType"></param>
        /// <returns></returns>
        public MethodSignature GetRandomVectorCreateMethod(Tree.ValueType returnType)
        {
            var matchingMethods = AllVectorCreateMethods.Where(m => m.Data.ReturnType.Equals(returnType)).ToList();
            if (matchingMethods.Count == 0)
            {
                return null;
            }
            return PRNG.WeightedChoice(matchingMethods);
        }

        /// <summary>
        ///     Get random vector create method that returns specific returnType.
        /// </summary>
        /// <param name="returnType"></param>
        /// <returns></returns>
        public MethodSignature GetRandomVectorMethod(Tree.ValueType returnType)
        {
            var matchingMethods = AllVectorMethods.Where(m => m.Data.ReturnType.Equals(returnType));

            if (matchingMethods.Count() == 0)
            {
                return null;
            }
            return PRNG.WeightedChoice(matchingMethods);
        }

        /// <summary>
        ///     Gets random leaf method that returns specific returnType. Null if no such
        ///     method is generated yet.
        /// </summary>
        /// <param name="returnType"></param>
        /// <returns></returns>
        public MethodSignature GetRandomLeafMethod(Tree.ValueType returnType)
        {
            if (returnType.IsVectorType)
            {
                // VectorType is not marked as leaf-method. So just return from overall methods list.
                return GetRandomVectorMethod(returnType);
            }

            var matchingMethods = AllLeafMethods.Where(m => m.Data.ReturnType.Equals(returnType));
            if (matchingMethods.Count() == 0)
            {
                return null;
            }
            return PRNG.WeightedChoice(matchingMethods);
        }

        /// <summary>
        ///     Get random method that returns any type.
        /// </summary>
        /// <returns></returns>
        public MethodSignature GetRandomMethod()
        {
            return PRNG.WeightedChoice(_methods);
        }

        public Scope CurrentScope
        {
            get { return ScopeStack.Peek(); }
        }

        public void PushScope(Scope scope)
        {
            ScopeStack.Push(scope);
        }

        public Scope PopScope()
        {
            Scope ret = ScopeStack.Pop();
            //Debug.Assert(ret.Parent == ScopeStack.Peek());
            return ret;
        }

        public ClassDeclStatement Generate()
        {
            // push class scope
            PushScope(ClassScope);

            List<Statement> classMembers = new List<Statement>();

            if (TC.ContainsVectorData)
            {
                GenerateVectorMethods();
            }

            classMembers.AddRange(GenerateStructs());
            classMembers.AddRange(GenerateFields(isStatic: true));
            classMembers.AddRange(GenerateFields(isStatic: false));

            // special
            classMembers.Add(new FieldDeclStatement(TC, Tree.ValueType.ForPrimitive(Primitive.Int), Constants.LoopInvariantName, ConstantValue.GetRandomConstantInt(0, 10), true));
            classMembers.Add(new ArbitraryCodeStatement(TC, "private static List<string> toPrint = new List<string>();"));

            classMembers.AddRange(GenerateLeafMethods());
            classMembers.AddRange(GenerateMethods());
            classMembers.Add(PreGenerated.StaticMethods);

            // pop class scope
            PopScope();

            return new ClassDeclStatement(TC, ClassName, classMembers);
        }

        /// <summary>
        ///     Generate structs in this class
        /// </summary>
        /// <returns></returns>
        private List<Statement> GenerateStructs()
        {
            List<Statement> structs = new List<Statement>();

            for (int structIndex = 1; structIndex <= TC.Config.StructCount; structIndex++)
            {
                string structName = $"S{structIndex}";
                var structDecl = GenerateStruct(structName, structName, structIndex, 1);
                structs.Add(structDecl);
                CurrentScope.AddStructType(structName, structDecl.StructFields);
            }

            StructDeclStatement GenerateStruct(string structName, string structType, int structIndex, int depth)
            {
                List<StructDeclStatement> nestedStructs = new List<StructDeclStatement>();
                List<StructField> fieldsMetadata = new List<StructField>();
                int fieldCount = PRNG.Next(1, TC.Config.StructFieldCount);
                for (int fieldIndex = 1; fieldIndex <= fieldCount; fieldIndex++)
                {
                    if (PRNG.Decide(TC.Config.NestedStructProbability) && depth < TC.Config.NestedStructDepth)
                    {
                        string nestedStructName = $"S{structIndex}_D{depth}_F{fieldIndex}";
                        string nestedStructType = structType + "." + nestedStructName;
                        var structDecl = GenerateStruct(nestedStructName, nestedStructType, structIndex, depth + 1);
                        nestedStructs.Add(structDecl);
                        CurrentScope.AddStructType(nestedStructType, structDecl.StructFields);

                        //structName = nestedStructName;
                        continue;
                    }

                    Tree.ValueType fieldType;
                    string fieldName;

                    if (PRNG.Decide(TC.Config.StructFieldTypeProbability) && CurrentScope.NumOfStructTypes > 0)
                    {
                        fieldType = CurrentScope.AllStructTypes[PRNG.Next(CurrentScope.NumOfStructTypes)];
                    }
                    else
                    {
                        fieldType = GetASTUtils().GetRandomValueType();
                    }

                    fieldName = GetVariableName(fieldType);
                    //fieldsTree.Add(new VarDeclStatement(TC, fieldType, fieldName, null));
                    fieldsMetadata.Add(new StructField(fieldType, fieldName));
                }
                return new StructDeclStatement(TC, structName, fieldsMetadata, nestedStructs);
            }

            return structs;
        }

        /// <summary>
        ///     Generate methods in this class
        /// </summary>
        /// <returns></returns>
        private IList<MethodDeclStatement> GenerateMethods()
        {
            List<MethodDeclStatement> methods = new List<MethodDeclStatement>();

            for (int i = 1; i < TC.Config.MethodCount; i++)
            {
                var testMethod = new TestMethod(this, "Method" + i);
                methods.Add(testMethod.Generate());
            }
            methods.Add(new TestMethod(this, "Method0", true).Generate());

            return methods;
        }

        /// <summary>
        ///     Generate leaf methods in this class.
        /// </summary>
        private IList<MethodDeclStatement> GenerateLeafMethods()
        {
            List<MethodDeclStatement> leafMethods = new List<MethodDeclStatement>();
            int leafMethodId = 0;
            foreach (Tree.ValueType variableType in Tree.ValueType.GetTypes())
            {
                var testMethod = new TestLeafMethod(this, "LeafMethod" + leafMethodId++, variableType);
                leafMethods.Add(testMethod.Generate());
            }

            foreach (Tree.ValueType structType in CurrentScope.AllStructTypes)
            {
                var testMethod = new TestLeafMethod(this, "LeafMethod" + leafMethodId++, structType);
                leafMethods.Add(testMethod.Generate());
            }
            return leafMethods;
        }

        /// <summary>
        ///     Generate fields in this class.
        /// </summary>
        private List<Statement> GenerateFields(bool isStatic)
        {
            List<Statement> fields = new List<Statement>();

            // TODO-TEMP initialize one variable of each type
            foreach (Tree.ValueType variableType in Tree.ValueType.GetTypes())
            {
                string variableName = (isStatic ? "s_" : string.Empty) + GetVariableName(variableType);
                Expression rhs = ConstantValue.GetConstantValue(variableType, TC._numerals);
                CurrentScope.AddLocal(variableType, variableName);

                fields.Add(new FieldDeclStatement(TC, variableType, variableName, rhs, isStatic));
            }

            if (TC.ContainsVectorData)
            {
                foreach (Tree.ValueType variableType in VectorHelpers.GetVectorTypes(TC))
                {
                    Debug.Assert(variableType.IsVectorType);
                    string variableName = (isStatic ? "s_" : string.Empty) + GetVariableName(variableType);

                    Expression rhs;
                    if (PRNG.Decide(0.8))
                    {
                        MethodSignature createSig = GetRandomVectorCreateMethod(variableType);

                        List<Expression> argumentNodes = new List<Expression>();
                        List<ParamValuePassing> passingWays = new List<ParamValuePassing>();

                        int parametersCount = createSig.Parameters.Count;

                        for (int i = 0; i < parametersCount; i++)
                        {
                            MethodParam methodParam = createSig.Parameters[i];
                            Tree.ValueType argType = methodParam.ParamType;
                            Debug.Assert(!argType.IsVectorType);

                            Expression parameterValue = ConstantValue.GetConstantValue(argType, TC._numerals);
                            if (i == 0)
                            {
                                parameterValue = new CastExpression(TC, parameterValue, argType);
                            }

                            argumentNodes.Add(parameterValue);
                            passingWays.Add(ParamValuePassing.None);
                        }

                        rhs = new MethodCallExpression(createSig.MethodName, argumentNodes, passingWays);
                    }
                    else
                    {
                        rhs = ConstantValue.GetConstantValue(variableType, TC._numerals);
                    }

                    CurrentScope.AddLocal(variableType, variableName);
                    fields.Add(new FieldDeclStatement(TC, variableType, variableName, rhs, isStatic));
                }
            }

            // TODO-TEMP initialize one variable of each struct type
            foreach (Tree.ValueType structType in CurrentScope.AllStructTypes)
            {
                string variableName = (isStatic ? "s_" : string.Empty) + GetVariableName(structType);

                Expression rhs = new CreationExpression(TC, structType.TypeName, null);
                CurrentScope.AddLocal(structType, variableName);

                // Add all the fields to the scope
                var listOfStructFields = CurrentScope.GetStructFields(structType);
                foreach (var structField in listOfStructFields)
                {
                    CurrentScope.AddLocal(structField.FieldType, $"{variableName}.{structField.FieldName}");
                }

                fields.Add(new FieldDeclStatement(TC, structType, variableName, rhs, isStatic));
            }

            //TODO: Define some more constants

            return fields;
        }
    }
}
