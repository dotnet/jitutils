// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System;

public class BenchmarkInfo
{
    public string Name { get; init; }
    public double Ratio { get; set; }

    public string CleanName
    {
        get
        {
            string cleanName = Name;
            if (cleanName.Length > 100)
            {
                int parensIndex = cleanName.IndexOf('(');
                static string Last(string s, int num) => s.Length < num ? s : s[^num..];
                if (parensIndex == -1)
                {
                    cleanName = Last(cleanName, 100);
                }
                else
                {
                    string benchmarkName = cleanName[..parensIndex];
                    string paramsStr = cleanName[(parensIndex + 1)..^1];
                    cleanName = Last(benchmarkName, Math.Max(50, 100 - paramsStr.Length)) + "(" + Last(paramsStr, Math.Max(50, 100 - benchmarkName.Length)) + ")";
                }
            }

            foreach (char illegalChar in Path.GetInvalidFileNameChars())
            {
                cleanName = cleanName.Replace(illegalChar, '_');
            }

            cleanName = cleanName.Replace(' ', '_');

            return cleanName;
        }
    }

    public string CsvName => CleanName.Replace(',', '_');

}