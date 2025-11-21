// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Program to filter a coreclr SPMI collection to a subset of methods
// for basic Wasm BringUp Testing

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

if (args.Length != 3)
{
    Console.WriteLine("Usage: WasmFilter <coreroot> <spmicollection> <output>");
    return 1;
}

// Verify we see mcs in the coreroot path
string mcsPath = Path.Combine(args[0], "mcs.exe");
if (!File.Exists(mcsPath))
{
    Console.WriteLine($"Error: mcs.exe not found in coreroot path {args[0]}");
    return 1;
}

// Inputs
string spmiCollection = args[1];
string outputPath = args[2];

if (!File.Exists(spmiCollection))
{
    Console.WriteLine($"Error: spmi collection file not found: {spmiCollection}");
    return 1;
}

if (!spmiCollection.Contains("coreclr"))
{
    Console.WriteLine($"Error: spmi collection should be coreclr: {spmiCollection}");
    return 1;
}

// Ensure output directory exists
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: could not create output directory for '{outputPath}': {ex.Message}");
    return 1;
}

string outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";

// Prepare process start info
var psi = new ProcessStartInfo
{
    FileName = mcsPath,
    Arguments = $"-dumpMap \"{spmiCollection}\"",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
};

Console.WriteLine($"Invoking: {psi.FileName} {psi.Arguments}");

string stdout;
string stderr;
int exitCode;

var sw = Stopwatch.StartNew();

using (var proc = Process.Start(psi))
{
    if (proc is null)
    {
        Console.WriteLine("Error: failed to start mcs.exe process.");
        return 1;
    }

    // Read entire streams (this drains buffers, preventing deadlocks)
    stdout = proc.StandardOutput.ReadToEnd();
    stderr = proc.StandardError.ReadToEnd();

    // Optional timeout (e.g., 2 minutes)
    const int timeoutMs = 120_000;
    if (!proc.WaitForExit(timeoutMs))
    {
        Console.WriteLine($"Error: mcs.exe timed out after {timeoutMs / 1000} seconds.");
        try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
        return 1;
    }

    exitCode = proc.ExitCode;
}

sw.Stop();

if (exitCode != 0)
{
    Console.WriteLine($"Note: mcs.exe exited with code {exitCode}");
}

if (!string.IsNullOrWhiteSpace(stderr))
{
    Console.WriteLine("mcs.exe stderr (non-fatal):");
    Console.WriteLine(stderr);
}

Console.WriteLine($"mcs.exe completed in {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine("Filtering output...");

string[] excludeStrings = new string[]
{
    "TestEntryPoint",
    "Array",
    "Box",
    "Call",
    "Conv",
    "Fib",
    "Fact",
    "Localloc",
    "Ind",
    "Jmp",
    "Obj",
    "RMW",
    "Root",
    "struct",
    "Struct",
    "sqrt",
};

string[] includeStrings = new string[]
{
    "BringUpTest_",
};

// Process lines (CSV)

var included = new List<string>();
using (var reader = new StringReader(stdout))
{
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (!includeStrings.Any(s => line.Contains(s)))
            continue;
        if (excludeStrings.Any(s => line.Contains(s)))
            continue;

            // SPMI index is the first field
            int commaIndex = line.IndexOf(',');
            if (commaIndex < 0)
                continue;
            string indexStr = line.Substring(0, commaIndex);
        included.Add(indexStr);
        Console.WriteLine($"Including: {line}");
    }
}

// Write indices to a temp file
string filterFilePath = Path.Combine(outDir, "wasm_filter.mcl");
File.WriteAllLines(filterFilePath, included);

// Reinvoke MCS to filter collection
psi.Arguments = $"-copy \"{filterFilePath}\" \"{spmiCollection}\" \"{outputPath}\"";
psi.RedirectStandardOutput = false;
psi.RedirectStandardError = false;
psi.UseShellExecute = false;

Console.WriteLine($"Invoking: {psi.FileName} {psi.Arguments}");

using (var proc = Process.Start(psi))
{
    if (proc is null)
    {
        Console.WriteLine("Error: failed to start mcs.exe process.");
        return 1;
    }

    const int timeoutMs = 120_000;
    if (!proc.WaitForExit(timeoutMs))
    {
        Console.WriteLine($"Error: mcs.exe timed out after {timeoutMs / 1000} seconds.");
        try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
        return 1;
    }

    exitCode = proc.ExitCode;
}
if (exitCode != 0)
{
    Console.WriteLine($"Error: mcs.exe exited with code {exitCode} during filtering.");
    if (!string.IsNullOrWhiteSpace(stderr))
        Console.WriteLine("stderr:\n" + stderr);
    return 1;
}

return 0;
