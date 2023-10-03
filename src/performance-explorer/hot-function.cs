// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;

public class HotFunction
{
    public double Fraction { get; set; }
    public string CodeType { get; set; }
    public string Name { get; set; }

    string disasmName;
    string friendlyName;

    public string DisasmName { get { SetNames(); return disasmName; } }
    public string FriendlyName { get { SetNames(); return friendlyName; } }

    void SetNames()
    {
        int classNameStart = Name.IndexOf(']') + 1;
        int classInstantiationStart = Name.IndexOf('[', classNameStart); // skip module name
                                                                         // Remove all instantiations, module name, and parameters
        string name = RemoveMatched(Name, '(', ')');
        name = RemoveMatched(name, '[', ']');

        int lastDot = name.LastIndexOf('.');
        while (name[lastDot - 1] == '.')
        {
            lastDot--;
        }
        string methodName = name[(lastDot + 1)..];
        string className = name[..lastDot];
        if (classInstantiationStart != -1 && classInstantiationStart < lastDot)
        {
            className = Name[classNameStart..classInstantiationStart];
        }
        disasmName = $"*{className}:*{methodName}";
        friendlyName = $"{className}.{methodName}";
    }

    private static string RemoveMatched(string text, char open, char close)
    {
        StringBuilder newString = new StringBuilder(text.Length);
        int nest = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                nest++;
            }
            else if (nest > 0 && text[i] == close)
            {
                nest--;
            }
            else if (nest == 0)
            {
                newString.Append(text[i]);
            }
        }

        return newString.ToString();
    }

    public string DasmFileName
    {
        get
        {
            string fileName = FriendlyName + ".dasm";
            foreach (char illegalChar in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(illegalChar, '_');
            fileName = fileName.Replace(' ', '_');
            return fileName;
        }
    }
}