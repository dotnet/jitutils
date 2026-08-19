// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private static readonly string[] s_vectorGenericArgNames = new[]
        {
            "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32",
            "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double"
        };

        private static List<Type> s_vectorGenericArgs = null;
        private static List<MethodSignature> s_allVectorMethods = null;
        private static List<ValueType> s_allVectorTypes = null;

        // When set, build the method pool by reading CORE_ROOT rather than by reflecting over
        // Antigen's own runtime.
        private static MetadataLoadContext s_metadataContext = null;
        private static Assembly s_coreLib = null;

        // Allow float-to-int reinterpret casts (Vector.As) in the method pool
        public static bool AllowFloatToIntegralReinterpret = false;

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

        /// <summary>
        ///     Build the method pool. When <paramref name="coreRootDirectory"/> is supplied, the
        ///     pool is read from CORE_ROOT's assemblies (the framework the generated tests will
        ///     actually execute against). When null, falls back to reflecting over Antigen's own
        ///     runtime, which is the historical behavior.
        /// </summary>
        public static void RecordVectorMethods(string coreRootDirectory = null)
        {
            Debug.Assert(s_allVectorTypes == null);
            
            if (s_allVectorTypes != null)
            {
                return;
            }

            InitializeMetadataContext(coreRootDirectory);

            RecordVectorTypes();

            s_allVectorMethods = new List<MethodSignature>();

            s_vectorGenericArgs = s_vectorGenericArgNames.Select(ResolveType).Where(t => t != null).ToList();
            if (s_vectorGenericArgs.Count != s_vectorGenericArgNames.Length)
            {
                throw new InvalidOperationException(
                    $"Could not resolve the primitive types used to instantiate generic vector methods. " +
                    $"Resolved {s_vectorGenericArgs.Count} of {s_vectorGenericArgNames.Length}.");
            }

            RecordIntrinsicMethods("System.Numerics.Vector", "Vector");
            RecordIntrinsicMethods("System.Numerics.Vector2", "Vector2");
            RecordIntrinsicMethods("System.Numerics.Vector3", "Vector3");
            RecordIntrinsicMethods("System.Numerics.Vector4", "Vector4");
            RecordVectorCtors("System.Numerics.Vector2");
            RecordVectorCtors("System.Numerics.Vector3");
            RecordVectorCtors("System.Numerics.Vector4");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Vector64", "Vector64");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Vector128", "Vector128");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Vector256", "Vector256");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Vector512", "Vector512");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Arm.AdvSimd", "AdvSimd");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Arm.AdvSimd+Arm64", "AdvSimd.Arm64");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.Arm.Sve", "Sve");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Aes", "Aes");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Bmi1", "Bmi1");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Bmi1+X64", "Bmi1.X64");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Bmi2", "Bmi2");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Bmi2+X64", "Bmi2.X64");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Fma", "Fma");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Lzcnt", "Lzcnt");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Lzcnt+X64", "Lzcnt.X64");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Pclmulqdq", "Pclmulqdq");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Popcnt", "Popcnt");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Popcnt+X64", "Popcnt.X64");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx", "Avx");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx2", "Avx2");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx512BW", "Avx512BW");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx512CD", "Avx512CD");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx512DQ", "Avx512DQ");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx512F", "Avx512F");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Avx512Vbmi", "Avx512Vbmi");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Sse", "Sse");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Sse2", "Sse2");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Sse3", "Sse3");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Sse41", "Sse41");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Sse42", "Sse42");
            RecordIntrinsicMethods("System.Runtime.Intrinsics.X86.Sse", "Sse");
        }

        /// <summary>
        ///     Open CORE_ROOT for metadata-only inspection. Nothing here is ever executed, so the
        ///     assemblies do not need to match the runtime Antigen is running on.
        /// </summary>
        private static void InitializeMetadataContext(string coreRootDirectory)
        {
            if (string.IsNullOrEmpty(coreRootDirectory))
            {
                return;
            }

            string coreLibPath = Path.Combine(coreRootDirectory, "System.Private.CoreLib.dll");
            if (!File.Exists(coreLibPath))
            {
                Console.WriteLine($"WARNING: {coreLibPath} not found; falling back to Antigen's own framework " +
                                  $"for the method pool. Generated tests may use APIs that do not exist in CORE_ROOT.");
                return;
            }

            s_metadataContext = new MetadataLoadContext(
                new PathAssemblyResolver(Directory.GetFiles(coreRootDirectory, "*.dll")));
            s_coreLib = s_metadataContext.LoadFromAssemblyPath(coreLibPath);
        }

        /// <summary>
        ///     Resolve a type by full metadata name from CORE_ROOT when available, otherwise from
        ///     Antigen's own runtime.
        /// </summary>
        private static Type ResolveType(string fullName)
        {
            if (s_coreLib != null)
            {
                Type fromCoreLib = s_coreLib.GetType(fullName);
                if (fromCoreLib != null)
                {
                    return fromCoreLib;
                }

                foreach (var assembly in s_metadataContext.GetAssemblies())
                {
                    Type candidate = assembly.GetType(fullName);
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }

                Console.WriteLine($"WARNING: '{fullName}' was not found in CORE_ROOT's " +
                                  $"System.Private.CoreLib (searched {s_metadataContext.GetAssemblies().Count()} " +
                                  $"loaded assemblies). Any methods on it will be missing from the pool.");
                return null;
            }

            Type fromOwnRuntime = Type.GetType(fullName);
            if (fromOwnRuntime == null)
            {
                Console.WriteLine($"WARNING: '{fullName}' was not found in Antigen's own framework. " +
                                  $"Any methods on it will be missing from the pool.");
            }

            return fromOwnRuntime;
        }

        /// <summary>
        ///     True if every generic argument of <paramref name="method"/> appears somewhere in its
        ///     parameter list, and so can be inferred by the C# compiler at a call site that does not
        ///     spell out type arguments.
        /// </summary>
        private static bool CanInferGenericArguments(MethodInfo method)
        {
            var genericArguments = method.GetGenericArguments();
            var parameters = method.GetParameters();

            foreach (var genericArgument in genericArguments)
            {
                bool found = false;
                foreach (var parameter in parameters)
                {
                    if (MentionsType(parameter.ParameterType, genericArgument))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool MentionsType(Type type, Type sought)
        {
            if (type.IsGenericParameter)
            {
                return type.Name == sought.Name;
            }

            if (type.HasElementType)
            {
                return MentionsType(type.GetElementType(), sought);
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    if (MentionsType(argument, sought))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ShouldSkipVectorMethod(string fullMethodName)        {
            // We do not support these types, so ignore these methods.
            // Need both "Byref" and "&" as MethodInfo.ToString() can render these differently
            return fullMethodName.Contains("IntPtr") || fullMethodName.Contains("ValueTuple") ||
                    fullMethodName.Contains("Matrix") || fullMethodName.Contains("Span") ||
                    fullMethodName.Contains("Quaternion") || fullMethodName.Contains("[]") ||
                    fullMethodName.Contains("*") || fullMethodName.Contains("ByRef") ||
                    fullMethodName.Contains("&") ||
                    fullMethodName.Contains("Numerics.Plane") || fullMethodName.Contains("Divide") ||
                    /*fullMethodName.Contains("SveMaskPattern") ||*/ fullMethodName.Contains("SvePrefetchType") ||
                    fullMethodName.Contains("FloatComparisonMode") || fullMethodName.Contains("FloatRoundingMode") ||
                    fullMethodName.Contains("MidpointRounding") || fullMethodName.Contains("Unsafe");
        }

        // Look for float->integral reinterpret casts using "Vector.As".
        // A pretty common source of false positives is
        //  some_fp_vector -> Vector.As -> some_int_vector -> print_bits
        // The JIT is permitted to choose different bitwise representations for NaN
        private static bool IsFloatToIntegralReinterpretation(MethodInfo method)
        {
            if (!method.Name.StartsWith("As", StringComparison.Ordinal))
            {
                return false;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                return false;
            }

            return IsFloatingPointElementVector(parameters[0].ParameterType) &&
                   IsIntegralElementVector(method.ReturnType);
        }

        /// <summary>
        ///     Returns the element type of a closed generic vector type, or null if the type is not
        ///     one (Vector2/Vector3/Vector4 and open generics both return null).
        /// </summary>
        private static Type VectorElementType(Type type)
        {
            if (!type.IsGenericType || type.ContainsGenericParameters)
            {
                return null;
            }

            var genericArgs = type.GetGenericArguments();
            return genericArgs.Length == 1 ? genericArgs[0] : null;
        }

        private static bool IsFloatingPointElementVector(Type type)
        {
            var elementType = VectorElementType(type);
            // Compared by name, not by typeof(): when the pool is built from CORE_ROOT metadata
            // these Types come from a MetadataLoadContext, so reference equality against the
            // runtime's typeof(float) is always false and this filter would silently stop working.
            return elementType != null &&
                   (elementType.FullName == "System.Single" || elementType.FullName == "System.Double");
        }

        private static readonly HashSet<string> s_integralElementTypeNames = new HashSet<string>()
        {
            "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
            "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
            "System.IntPtr", "System.UIntPtr"
        };

        private static bool IsIntegralElementVector(Type type)
        {
            var elementType = VectorElementType(type);
            return elementType != null && s_integralElementTypeNames.Contains(elementType.FullName);
        }

        private static void RecordIntrinsicMethods(string typeFullName, string vectorTypeName)
        {
            Type resolved = ResolveType(typeFullName);
            if (resolved == null)
            {
                // Expected for intrinsic classes absent from this framework version.
                return;
            }

            RecordIntrinsicMethods(resolved, vectorTypeName);
        }

        private static void RecordVectorCtors(string typeFullName)
        {
            Type resolved = ResolveType(typeFullName);
            if (resolved != null)
            {
                RecordVectorCtors(resolved);
            }
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

                if (!AllowFloatToIntegralReinterpret && IsFloatToIntegralReinterpretation(method))
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

                if (!CanInferGenericArguments(method))
                {
                    // Something like "Vector128<float>.Pi()" with no args breaks our type inference,
                    // leave these out
                    continue;
                }

                if (method.GetGenericArguments().Count() == 1)
                {
                    // Only instantiate generic single argument methods
                    foreach (var genericArgument in s_vectorGenericArgs)
                    {
                        var genericInitVectorMethod = method.MakeGenericMethod(genericArgument);

                        if (!AllowFloatToIntegralReinterpret && IsFloatToIntegralReinterpretation(genericInitVectorMethod))
                        {
                            continue;
                        }

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
