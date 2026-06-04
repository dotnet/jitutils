// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Utils
{
    public class RslnUtilities
    {
        private static readonly Regex s_jitAssertionRegEx = new Regex("Assertion failed '(.*)' in '(.*)' during '(.*)'");
        private static readonly Regex s_coreclrAssertionRegEx = new Regex(@"Assert failure(\(PID \d+ \[0x[0-9a-f]+], Thread: \d+ \[0x[0-9a-f]+]\)):(.*)");


        public static SyntaxTree GetValidSyntaxTree(SyntaxNode treeRoot, bool doValidation = true)
        {
            SyntaxTree validTree = CSharpSyntaxTree.ParseText(treeRoot.ToFullString());

#if UNDEFINED
            if (doValidation)
            {
                SyntaxTree syntaxTree = treeRoot.SyntaxTree;
                FindTreeDiff(validTree.GetRoot(), syntaxTree.GetRoot());
            }
#else
            // In release, make sure that we didn't end up generating wrong syntax tree,
            // hence parse the text to reconstruct the tree.
#endif
            return validTree;
        }

        /// <summary>
        ///     Method to find diff of generated tree vs. roslyn generated tree by parsing the
        ///     generated code.
        /// </summary>
        /// <param name="expected"></param>
        /// <param name="actual"></param>
        private static void FindTreeDiff(SyntaxNode expected, SyntaxNode actual)
        {
            if ((expected is LiteralExpressionSyntax) || (actual is LiteralExpressionSyntax))
            {
                // ignore
                return;
            }

            if (!expected.IsEquivalentTo(actual))
            {
                var expectedChildNodes = expected.ChildNodes().ToArray();
                var actualChildNodes = actual.ChildNodes().ToArray();

                int expectedCount = expectedChildNodes.Length;
                int actualCount = actualChildNodes.Length;
                if (expectedCount != actualCount)
                {
                    Debug.Assert(false, $"Child nodes mismatch. Expected= {expected}, Actual= {actual}");
                    return;
                }
                for (int ch = 0; ch < expectedCount; ch++)
                {
                    FindTreeDiff(expectedChildNodes[ch], actualChildNodes[ch]);
                }
                return;
            }
        }

        /// <summary>
        ///     Parse assertion errors in output
        /// </summary>
        /// <param name="output"></param>
        /// <returns></returns>
        public static string ParseAssertionError(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            // If Fatal error, return text as it is.
            if (output.Contains("Fatal error. System.AccessViolationException: Attempted to read or write protected memory. This is often an indication that other memory is corrupt."))
            {
                return "Fatal error. System.AccessViolationException: Attempted to read or write protected memory. This is often an indication that other memory is corrupt.";
            }


            // Otherwise, try to match for JIT asserts
            Match assertionMatch;
            assertionMatch = s_jitAssertionRegEx.Match(output);
            if (assertionMatch.Success)
            {
                Debug.Assert(assertionMatch.Groups.Count == 4);
                return $"Assertion failed '{assertionMatch.Groups[1].Value}' during '{assertionMatch.Groups[3].Value}'";
            }

            assertionMatch = s_coreclrAssertionRegEx.Match(output);
            if (assertionMatch.Success)
            {
                Debug.Assert(assertionMatch.Groups.Count == 3);
                return assertionMatch.Groups[2].Value;
            }
            return null;
        }
    }
}
