// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using Antigen.Tree;
using ValueType = Antigen.Tree.ValueType;

namespace Antigen
{
    public class VectorHelpers
    {
        private static readonly List<Type> s_vectorGenericArgs = new() { typeof(byte), typeof(sbyte), 
            typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double) };
        private static List<MethodSignature> s_allVectorMethods = null;
        private static List<ValueType> s_allVectorTypes = null;

        public static List<MethodSignature> GetAllVectorMethods()
        {
            Debug.Assert(s_allVectorMethods != null);
            return s_allVectorMethods;
        }

        public static List<ValueType> GetAllVectorTypes()
        {
            Debug.Assert(s_allVectorTypes != null);
            return s_allVectorTypes;
        }

        public static List<ValueType> GetVectorTypes(TestCase TC)
        {
            List<ValueType> vectorTypes = new();
            foreach (var vectorType in s_allVectorTypes)
            {
                if (vectorType.IsVectorNumerics() ||
                    vectorType.IsVectorTIntrinsics())
                {
                    vectorTypes.Add(vectorType);
                }

                if (TC.Config.UseSve)
                {
                    if (vectorType.IsVector64Intrinsics() ||
                        vectorType.IsVector128Intrinsics())
                    {
                        vectorTypes.Add(vectorType);
                    }
                }
                else
                {
                    if (Vector64.IsHardwareAccelerated && vectorType.IsVector64Intrinsics())
                    {
                        vectorTypes.Add(vectorType);
                    }
                    else if (Vector128.IsHardwareAccelerated && vectorType.IsVector128Intrinsics())
                    {
                        vectorTypes.Add(vectorType);
                    }
                    else if (Vector256.IsHardwareAccelerated && vectorType.IsVector256Intrinsics())
                    {
                        vectorTypes.Add(vectorType);
                    }
                    else if (Vector512.IsHardwareAccelerated && vectorType.IsVector512Intrinsics())
                    {
                        vectorTypes.Add(vectorType);
                    }
                }
            }
            return vectorTypes;
        }

        private static void RecordVectorTypes()
        {
            s_allVectorTypes =
            [
                new ValueType(VectorType.Vector64_Byte, "Vector64<byte>", "v64_byte"),
                new ValueType(VectorType.Vector64_SByte, "Vector64<sbyte>", "v64_sbyte"),
                new ValueType(VectorType.Vector64_Short, "Vector64<short>", "v64_short"),
                new ValueType(VectorType.Vector64_UShort, "Vector64<ushort>", "v64_ushort"),
                new ValueType(VectorType.Vector64_Int, "Vector64<int>", "v64_int"),
                new ValueType(VectorType.Vector64_UInt, "Vector64<uint>", "v64_uint"),
                new ValueType(VectorType.Vector64_Long, "Vector64<long>", "v64_long"),
                new ValueType(VectorType.Vector64_ULong, "Vector64<ulong>", "v64_ulong"),
                new ValueType(VectorType.Vector64_Float, "Vector64<float>", "v64_float"),
                new ValueType(VectorType.Vector64_Double, "Vector64<double>", "v64_double"),
                new ValueType(VectorType.Vector128_Byte, "Vector128<byte>", "v128_byte"),
                new ValueType(VectorType.Vector128_SByte, "Vector128<sbyte>", "v128_sbyte"),
                new ValueType(VectorType.Vector128_Short, "Vector128<short>", "v128_short"),
                new ValueType(VectorType.Vector128_UShort, "Vector128<ushort>", "v128_ushort"),
                new ValueType(VectorType.Vector128_Int, "Vector128<int>", "v128_int"),
                new ValueType(VectorType.Vector128_UInt, "Vector128<uint>", "v128_uint"),
                new ValueType(VectorType.Vector128_Long, "Vector128<long>", "v128_long"),
                new ValueType(VectorType.Vector128_ULong, "Vector128<ulong>", "v128_ulong"),
                new ValueType(VectorType.Vector128_Float, "Vector128<float>", "v128_float"),
                new ValueType(VectorType.Vector128_Double, "Vector128<double>", "v128_double"),
                new ValueType(VectorType.Vector256_Byte, "Vector256<byte>", "v256_byte"),
                new ValueType(VectorType.Vector256_SByte, "Vector256<sbyte>", "v256_sbyte"),
                new ValueType(VectorType.Vector256_Short, "Vector256<short>", "v256_short"),
                new ValueType(VectorType.Vector256_UShort, "Vector256<ushort>", "v256_ushort"),
                new ValueType(VectorType.Vector256_Int, "Vector256<int>", "v256_int"),
                new ValueType(VectorType.Vector256_UInt, "Vector256<uint>", "v256_uint"),
                new ValueType(VectorType.Vector256_Long, "Vector256<long>", "v256_long"),
                new ValueType(VectorType.Vector256_ULong, "Vector256<ulong>", "v256_ulong"),
                new ValueType(VectorType.Vector256_Float, "Vector256<float>", "v256_float"),
                new ValueType(VectorType.Vector256_Double, "Vector256<double>", "v256_double"),
                new ValueType(VectorType.Vector512_Byte, "Vector512<byte>", "v512_byte"),
                new ValueType(VectorType.Vector512_SByte, "Vector512<sbyte>", "v512_sbyte"),
                new ValueType(VectorType.Vector512_Short, "Vector512<short>", "v512_short"),
                new ValueType(VectorType.Vector512_UShort, "Vector512<ushort>", "v512_ushort"),
                new ValueType(VectorType.Vector512_Int, "Vector512<int>", "v512_int"),
                new ValueType(VectorType.Vector512_UInt, "Vector512<uint>", "v512_uint"),
                new ValueType(VectorType.Vector512_Long, "Vector512<long>", "v512_long"),
                new ValueType(VectorType.Vector512_ULong, "Vector512<ulong>", "v512_ulong"),
                new ValueType(VectorType.Vector512_Float, "Vector512<float>", "v512_float"),
                new ValueType(VectorType.Vector512_Double, "Vector512<double>", "v512_double"),
                new ValueType(VectorType.Vector_Byte, "Vector<byte>", "v_byte"),
                new ValueType(VectorType.Vector_SByte, "Vector<sbyte>", "v_sbyte"),
                new ValueType(VectorType.Vector_Short, "Vector<short>", "v_short"),
                new ValueType(VectorType.Vector_UShort, "Vector<ushort>", "v_ushort"),
                new ValueType(VectorType.Vector_Int, "Vector<int>", "v_int"),
                new ValueType(VectorType.Vector_UInt, "Vector<uint>", "v_uint"),
                new ValueType(VectorType.Vector_Long, "Vector<long>", "v_long"),
                new ValueType(VectorType.Vector_ULong, "Vector<ulong>", "v_ulong"),
                new ValueType(VectorType.Vector_Float, "Vector<float>", "v_float"),
                new ValueType(VectorType.Vector_Double, "Vector<double>", "v_double"),
                new ValueType(VectorType.Vector2, "Vector2", "v2"),
                new ValueType(VectorType.Vector3, "Vector3", "v3"),
                new ValueType(VectorType.Vector4, "Vector4", "v4"),
            ];
        }

        public static void RecordVectorMethods()
        {
            Debug.Assert(s_allVectorTypes == null);
            
            if (s_allVectorTypes != null)
            {
                return;
            }

            RecordVectorTypes();

            s_allVectorMethods = new List<MethodSignature>();

            RecordIntrinsicMethods(typeof(Vector));
            RecordIntrinsicMethods(typeof(Vector2));
            RecordIntrinsicMethods(typeof(Vector3));
            RecordIntrinsicMethods(typeof(Vector4));
            RecordVectorCtors(typeof(Vector2));
            RecordVectorCtors(typeof(Vector3));
            RecordVectorCtors(typeof(Vector4));
            RecordIntrinsicMethods(typeof(Vector64));
            RecordIntrinsicMethods(typeof(Vector128));
            RecordIntrinsicMethods(typeof(Vector256));
            RecordIntrinsicMethods(typeof(Vector512));
            RecordIntrinsicMethods(typeof(AdvSimd));
            RecordIntrinsicMethods(typeof(AdvSimd.Arm64), "AdvSimd.Arm64");
            RecordIntrinsicMethods(typeof(Sve));
            RecordIntrinsicMethods(typeof(System.Runtime.Intrinsics.X86.Aes));
            RecordIntrinsicMethods(typeof(Bmi1));
            RecordIntrinsicMethods(typeof(Bmi1.X64), "Bmi1.X64");
            RecordIntrinsicMethods(typeof(Bmi2));
            RecordIntrinsicMethods(typeof(Bmi2.X64), "Bmi2.X64");
            RecordIntrinsicMethods(typeof(Fma));
            RecordIntrinsicMethods(typeof(Lzcnt));
            RecordIntrinsicMethods(typeof(Lzcnt.X64), "Lzcnt.X64");
            RecordIntrinsicMethods(typeof(Pclmulqdq));
            RecordIntrinsicMethods(typeof(Popcnt));
            RecordIntrinsicMethods(typeof(Popcnt.X64), "Popcnt.X64");
            RecordIntrinsicMethods(typeof(Avx));
            RecordIntrinsicMethods(typeof(Avx2));
            RecordIntrinsicMethods(typeof(Avx512BW));
            RecordIntrinsicMethods(typeof(Avx512CD));
            RecordIntrinsicMethods(typeof(Avx512DQ));
            RecordIntrinsicMethods(typeof(Avx512F));
            RecordIntrinsicMethods(typeof(Avx512Vbmi));
            RecordIntrinsicMethods(typeof(Sse));
            RecordIntrinsicMethods(typeof(Sse2));
            RecordIntrinsicMethods(typeof(Sse3));
            RecordIntrinsicMethods(typeof(Sse41));
            RecordIntrinsicMethods(typeof(Sse42));
            RecordIntrinsicMethods(typeof(Sse));
        }

        private static bool ShouldSkipVectorMethod(string fullMethodName)
        {
            // We do not support these types, so ignore these methods.
            return fullMethodName.Contains("IntPtr") || fullMethodName.Contains("ValueTuple") ||
                    fullMethodName.Contains("Matrix") || fullMethodName.Contains("Span") ||
                    fullMethodName.Contains("Quaternion") || fullMethodName.Contains("[]") ||
                    fullMethodName.Contains("*") || fullMethodName.Contains("ByRef") ||
                    fullMethodName.Contains("Numerics.Plane") || fullMethodName.Contains("Divide") ||
                    /*fullMethodName.Contains("SveMaskPattern") ||*/ fullMethodName.Contains("SvePrefetchType") ||
                    fullMethodName.Contains("FloatComparisonMode") || fullMethodName.Contains("FloatRoundingMode") ||
                    fullMethodName.Contains("MidpointRounding") || fullMethodName.Contains("Unsafe");
        }

        /// <summary>
        ///     Record the vector methods as well as the ones that creates the Vector.
        ///     Applicable for Vector64, Vector128, Vector256, Vector512.
        /// </summary>
        /// <param name="vectorType"></param>
        private static void RecordIntrinsicMethods(Type vectorType, string vectorTypeName = null)
        {
            if (string.IsNullOrEmpty(vectorTypeName))
            {
                vectorTypeName = vectorType.Name;
            }
            var methods = vectorType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            var genericMethods = methods.Where(m => m.IsGenericMethod);
            var nonGenericMethods = methods.Where(m => !m.IsGenericMethod);
            HashSet<string> nonGenericAdded = new HashSet<string>();
            foreach (var method in nonGenericMethods)
            {
                string fullMethodName = method.ToString();

                if (method.IsSpecialName)
                {
                    // special methods like properties / operators
                    continue;
                }

                if (ShouldSkipVectorMethod(fullMethodName))
                {
                    continue;
                }

                s_allVectorMethods.Add(CreateMethodSignature(vectorTypeName, method));
                nonGenericAdded.Add(method.Name);
            }

            foreach (var method in genericMethods)
            {
                if (nonGenericAdded.Contains(method.Name))
                {
                    // We have already added generic instances of this method.
                    // No need to add further

                    continue;
                }

                if (method.IsSpecialName)
                {
                    // special methods like properties / operators
                    continue;
                }

                string fullMethodName = method.ToString();

                if (ShouldSkipVectorMethod(fullMethodName))
                {
                    continue;
                }

                if (method.GetGenericArguments().Count() == 1)
                {
                    // Only instantiate generic single argument methods
                    foreach (var genericArgument in s_vectorGenericArgs)
                    {
                        var genericInitVectorMethod = method.MakeGenericMethod(genericArgument);
                        s_allVectorMethods.Add(CreateMethodSignature(vectorTypeName, genericInitVectorMethod));
                    }
                }
            }
        }

        /// <summary>
        ///     Create method signature for the vectorTypeName that has the MethodInfo.
        /// </summary>
        /// <param name="vectorTypeName"></param>
        /// <param name="method"></param>
        /// <returns>Method signature</returns>
        private static MethodSignature CreateMethodSignature(string vectorTypeName, MethodInfo method)
        {
            var ms = new MethodSignature($"{vectorTypeName}.{method.Name}", isVectorGeneric: method.IsGenericMethod, isVectorMethod: true)
            {
                ReturnType = Tree.ValueType.ParseType(method.ReturnType.ToString())
            };
            var containsVectorParam = false;

            foreach (var methodParameter in method.GetParameters())
            {
                if (methodParameter.ParameterType.Name.StartsWith("Vector"))
                {
                    containsVectorParam = true;
                }
                ms.Parameters.Add(new MethodParam()
                {
                    ParamName = methodParameter.Name,
                    ParamType = Tree.ValueType.ParseType(methodParameter.ParameterType.ToString()),
                    PassingWay = ParamValuePassing.None
                });
            }

            if ((method.Name == "Create" || method.Name == "CreateScalar") && !containsVectorParam)
            {
                // Ignore vector param for Create methods because we might not have those variables
                // available.
                ms.IsVectorCreateMethod = true;
            }

            return ms;
        }

        /// <summary>
        ///     Record the vector methods as well as the ones that creates the Vector.
        ///     Applicable for Vector2, Vector3, Vector4.
        /// </summary>
        /// <param name="vectorType"></param>
        private static void RecordVectorCtors(Type vectorType)
        {
            var ctors = vectorType.GetConstructors();

            foreach (var ctor in ctors)
            {
                string fullMethodName = ctor.ToString();

                if (fullMethodName.Contains("IntPtr") || fullMethodName.Contains("ValueTuple") ||
                    fullMethodName.Contains("Matrix") || fullMethodName.Contains("Span") ||
                    fullMethodName.Contains("Quaternion") || fullMethodName.Contains("Vector"))
                {
                    continue;
                }

                var ms = new MethodSignature($"new {vectorType.Name}", isVectorGeneric: false, isVectorMethod: true, isVectorCreateMethod: true)
                {
                    ReturnType = Tree.ValueType.ParseType(vectorType.ToString())
                };

                foreach (var methodParameter in ctor.GetParameters())
                {
                    ms.Parameters.Add(new MethodParam()
                    {
                        ParamName = methodParameter.Name,
                        ParamType = Tree.ValueType.ParseType(methodParameter.ParameterType.ToString()),
                        PassingWay = ParamValuePassing.None
                    });
                }

                s_allVectorMethods.Add(ms);
            }
        }

        private static readonly Regex multipleVectorsRegex = new Regex(@"Vector(64|128|256|512)");

        /// <summary>
        ///     Returns Vector length for given type. `null` if not a VectorType.
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        public static string GetVectorList(string typeName)
        {
            var vectorTypeMatches = multipleVectorsRegex.Matches(typeName);
            if (vectorTypeMatches.Count == 0)
            {
                return null;
            }

            string result = "|";
            foreach (Match vectorTypeMatch in vectorTypeMatches)
            {
                Debug.Assert(vectorTypeMatch.Groups.Count == 2);
                result += (vectorTypeMatch.Groups[1].Value + "|");
            }

            return result;
        }
    }
}
