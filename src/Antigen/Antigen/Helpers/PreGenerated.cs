// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Antigen.Statements;

namespace Antigen
{
    public static class PreGenerated
    {
        public static readonly string MainClassName = "TestClass";
        private static ArbitraryCodeStatement s_usingStmts = null;
        /// <summary>
        ///     Returns code related to using directives.
        /// </summary>
        public static Statement UsingDirective
        {
            get
            {
                if (s_usingStmts != null)
                {
                    return s_usingStmts;
                }

                string usingCode =
@"// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// This file is auto-generated.
// Seed: -1
//

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Numerics;
";
                bool isArm = (RuntimeInformation.OSArchitecture == Architecture.Arm) || (RuntimeInformation.OSArchitecture == Architecture.Arm64);

                s_usingStmts = new ArbitraryCodeStatement(null, usingCode);
                return s_usingStmts;
            }
        }

        private static ArbitraryCodeStatement s_staticMethods = null;

        /// <summary>
        ///     Returns list of 3 static methods - Main, Log and PrintLog.
        /// </summary>
        public static Statement StaticMethods
        {
            get
            {
                if (s_staticMethods != null)
                {
                    return s_staticMethods;
                }

                StringBuilder staticMethodBuilder = new StringBuilder();

                // Main method
                staticMethodBuilder.AppendLine("public static int Main(string[] args) { ");
                //staticMethodBuilder.AppendLine($"new {MainClassName}().Method0();");
                //staticMethodBuilder.AppendLine("PrintLog();");
                staticMethodBuilder.AppendLine("return Antigen();");
                staticMethodBuilder.AppendLine("}");

                staticMethodBuilder.AppendLine("public static int Antigen() { ");
                staticMethodBuilder.AppendLine($"new {MainClassName}().Method0();");
                staticMethodBuilder.AppendLine("return string.Join(Environment.NewLine, toPrint).GetHashCode();");
                staticMethodBuilder.AppendLine("}");

                // Log method
                staticMethodBuilder.AppendLine("[MethodImpl(MethodImplOptions.NoInlining)]");
                staticMethodBuilder.AppendLine("public static void Log(string varName, object varValue) {");
                staticMethodBuilder.AppendLine(@$"     toPrint.Add($""{{varName}}={{varValue}}"");");
                staticMethodBuilder.AppendLine("}");

                // PrintLog method
                staticMethodBuilder.AppendLine("public static void PrintLog() {");
                staticMethodBuilder.AppendLine("foreach (var entry in toPrint) {");
                staticMethodBuilder.AppendLine("Console.WriteLine(entry);");
                staticMethodBuilder.AppendLine("}}");

                s_staticMethods = new ArbitraryCodeStatement(null, staticMethodBuilder.ToString());

                return s_staticMethods;
            }
        }
    }
}
