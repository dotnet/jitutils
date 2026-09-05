// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ManagedCodeGen;
using Analyzer = ManagedCodeGen.Program;

namespace JitAnalyzeRegression;

internal static class Tests
{
    private static readonly string[] MetricNames =
    {
        "CodeSize", "PrologSize", "PerfScore", "InstrCount", "AllocSize", "ExtraAllocBytes",
        "DebugClauseCount", "DebugVarCount", "SpillCount", "SpillWeight", "ResolutionCount", "ResolutionWeight"
    };

    private static string root;
    private static string baseline;
    private static int checks;

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        if (args.Length > 1)
        {
            Console.Error.WriteLine("Usage: dotnet run --project test/jit-analyze/Regression [-c Release] -- [baseline-executable]");
            return 1;
        }

        baseline = args.SingleOrDefault();
        root = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ExtractMetrics();
            LineBoundaries();
            CommandLine();
            TextDiffs();
            ProcessHelpers();
            Console.WriteLine($"PASS: {checks} assertions (parser, line boundaries, CLI and TSV).");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Equal<T>(T expected, T actual, string context)
    {
        checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{context}: expected <{expected}>, actual <{actual}>");
    }

    private static void Contains(string text, string expected)
    {
        Equal(true, text.Contains(expected, StringComparison.Ordinal), $"output contains {expected}");
    }

    private static string Write(string name, string contents)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static string Listing(string name) => $"; Assembly listing for method {name}";

    private static string Summary(string name, int size, int prolog = 0, int perf = 0) =>
        $"; Total bytes of code {size}, prolog size {prolog}, PerfScore {perf} for method {name}";

    private static string Method(string name, int size, int prolog = 0, int perf = 0) =>
        Listing(name) + "\n" + Summary(name, size, prolog, perf) + "\n";

    private static void CheckMethod(Analyzer.MethodInfo method, string name, int count, string offsets, params double[] values)
    {
        Equal(name, method.name, "method name");
        Equal(count, method.functionCount, $"{name} function count");
        Equal(offsets, string.Join(",", method.functionOffsets), $"{name} offsets");
        for (int i = 0; i < MetricNames.Length; i++)
            Equal(values.Length > i ? values[i] : 0, method.Metrics.GetMetric(MetricNames[i]).Value, $"{name} {MetricNames[i]}");
    }

    private static void ExtractMetrics()
    {
        Equal(string.Join(",", MetricNames), string.Join(",", MetricCollection.AllMetrics.Select(m => m.Name)), "metric schema");
        const string repeated = "Namespace.类型:方法(é, 😀)";
        string first = Write("metrics-first.dasm", string.Join("\r\n", new[]
        {
            Listing(repeated),
            $"; Total bytes of code 10, prolog size 2, PerfScore 1.25, instruction count 3, allocated bytes for code 16, SpillCount 2 SpillCountWt 3.50 ResolutionMovs 4 ResolutionMovsWt 5.25 for method {repeated}",
            "; Variable debug info: 4 live range(s), 2 var(s)",
            "ignored for method not-a-method",
            Listing("Zero"),
            "; Total bytes of code 0 for method Zero",
            "; Variable debug info: 5 live range(s), 2 var(s)"
        }));
        string second = Write("metrics-second.dasm", string.Join("\n", new[]
        {
            Listing(repeated),
            $"; Total bytes of code 20, prolog size 3, perf score 2.50, instruction count 6, allocated bytes for code 24, SpillCount 3 SpillCountWt 4.25 ResolutionMovs 5 ResolutionMovsWt 6.50 for method {repeated}",
            Listing("Optional"),
            "; Total bytes of code 7 for method Optional"
        }));
        var methods = Analyzer.ExtractMethodInfo(new[] { first, second }).ToArray();
        Equal(4, methods.Length, "group count");
        CheckMethod(methods[0], repeated, 2, "7", 30, 5, 3.75, 9, 40, 10, 0, 0, 5, 7.75, 9, 11.75);
        CheckMethod(methods[1], "", 2, "2,6", 0, 0, 0, 0, 0, 0, 9, 4);
        CheckMethod(methods[2], "Zero", 2, "4");
        CheckMethod(methods[3], "Optional", 1, "9", 7);

        Equal(0, Analyzer.ExtractMethodInfo(Array.Empty<string>()).Count(), "no input files");
        Equal(0, Analyzer.ExtractMethodInfo(new[] { Write("empty.dasm", "") }).Count(), "empty file");

        // Extra allocation is computed after grouping, not independently per record.
        string mixed = Write("mixed.dasm", Listing("Mixed") + "\n; Total bytes of code 4 for method Mixed\n" +
            Listing("Mixed") + "\n; Total bytes of code 6, allocated bytes for code 16 for method Mixed");
        CheckMethod(Analyzer.ExtractMethodInfo(new[] { mixed }).Single(), "Mixed", 2, "2", 10, 0, 0, 0, 16, 6);

        // The weights require a decimal point; malformed selected lines still form groups.
        string malformed = Write("malformed.dasm",
            "; Total bytes of code invalid for method Odd\n" +
            "; Total bytes of code 1, SpillCount 2 SpillCountWt 3 ResolutionMovs 4 ResolutionMovsWt 5 for method Odd");
        CheckMethod(Analyzer.ExtractMethodInfo(new[] { malformed }).Single(), "Odd", 1, "", 1);
    }

    private static void LineBoundaries()
    {
        foreach (string newline in new[] { "\n", "\r\n", "\r" })
        foreach (bool terminalNewline in new[] { false, true })
        foreach (int length in new[] { 0, 1, 65534, 65535, 65536, 65537, 131073 })
        {
            string name = "类:é😀" + new string('x', length);
            string path = Write("boundary.dasm", new string(' ', length) + newline +
                Listing(name) + newline + Summary(name, 17, 3, 2) +
                (terminalNewline ? newline : ""));
            CheckMethod(Analyzer.ExtractMethodInfo(new[] { path }).Single(), name, 1, "1", 17, 3, 2);
        }

        // Place record starts, CRLF pairs and multibyte UTF-8 around a 64 KiB byte boundary.
        foreach (int offset in Enumerable.Range(65532, 9))
        {
            string path = Write("aligned.dasm", new string(' ', offset) + "\r\n" +
                Listing("é😀") + "\r\n" + Summary("é😀", 9));
            CheckMethod(Analyzer.ExtractMethodInfo(new[] { path }).Single(), "é😀", 1, "1", 9);
            path = Write("utf8-aligned.dasm", new string(' ', offset) + "é😀\r\n" +
                Listing("Unicode") + "\r\n" + Summary("Unicode", 9));
            CheckMethod(Analyzer.ExtractMethodInfo(new[] { path }).Single(), "Unicode", 1, "1", 9);
            path = Write("metric-aligned.dasm", "; Total bytes of code 9" +
                new string(' ', offset - "; Total bytes of code 9".Length) +
                "PerfScore 12.25, instruction count 7 for method Boundary");
            CheckMethod(Analyzer.ExtractMethodInfo(new[] { path }).Single(), "Boundary", 0, "", 9, 0, 12.25, 7);
        }

        string mixed = Write("newlines.dasm", "\r\n" + Listing("Mixed") + "\r" +
            Summary("Mixed", 2) + "\n\n" + Listing("Mixed") + "\r\n" + Summary("Mixed", 3));
        CheckMethod(Analyzer.ExtractMethodInfo(new[] { mixed }).Single(), "Mixed", 2, "1,4", 5);

        string bom = Path.Combine(root, "bom.dasm");
        File.WriteAllText(bom, Listing("BOM:é😀") + "\n" + Summary("BOM:é😀", 3), new UTF8Encoding(true));
        CheckMethod(Analyzer.ExtractMethodInfo(new[] { bom }).Single(), "BOM:é😀", 1, "", 3);
    }

    private static void CommandLine()
    {
        string baseFile = Write("base/keep.dasm",
            Method("Shared", 10, 2) + Method("Removed", 5, 1) + Method("Stable", 7, 1) + Method("PerfOnly", 3, 1, 1));
        string diffFile = Write("diff/keep.dasm",
            Method("Shared", 14, 1) + Method("Added", 8, 2) + Method("Stable", 7, 1) + Method("PerfOnly", 3, 1, 2));
        Write("base/baseOnly.dasm", Method("BaseUnique", 100));
        Write("diff/diffOnly.dasm", Method("DiffUnique", 200));
        string baseDir = Path.GetDirectoryName(baseFile);
        string diffDir = Path.GetDirectoryName(diffFile);

        var reconciled = Invoke(baseDir, diffDir, "--warn");
        Equal(-1, reconciled.Code, "reconciled exit code");
        Totals(reconciled.Output, 25, 32, 7);
        Contains(reconciled.Output, "Total byte diff includes 3 bytes from reconciling methods");
        Contains(reconciled.Output, "Warning: 1 files in base but not in diff.");
        Contains(reconciled.Output, "Warning: 1 files in diff but not in base.");
        Contains(reconciled.Output, "Mismatched methods in keep.dasm\nBase:\n    Removed\nDiff:\n    Added");
        CheckTsv(reconciled.Tsv, new[] { "Shared", "Removed", "Added" });

        var common = Invoke(baseDir, diffDir, "--no-reconcile", "--warn");
        Equal(-1, common.Code, "unreconciled exit code");
        Totals(common.Output, 20, 24, 4);
        Equal(false, common.Output.Contains("from reconciling methods"), "reconciliation disabled");
        CheckTsv(common.Tsv, new[] { "Shared" });

        var filtered = Invoke(baseDir, diffDir, "--filter", "keep", "--warn", "--metrics", "CodeSize", "--metrics", "PerfScore");
        Equal(-2, filtered.Code, "multiple changed metrics exit code");
        Totals(filtered.Output, 25, 32, 7);
        Contains(filtered.Output, "Summary of Perf Score diffs: (using filter 'keep')");
        Contains(filtered.Output, "Total PerfScoreUnits of base: 1\nTotal PerfScoreUnits of diff: 2");
        Equal(false, filtered.Output.Contains("files in base but not"), "filter excludes unique files");
        CheckTsv(filtered.Tsv, new[] { "Shared", "Removed", "Added", "PerfOnly", "Removed", "Added" }, headers: 2);

        var concat = Invoke(baseDir, diffDir, "--concat-files", "--warn");
        Equal(-1, concat.Code, "concatenated exit code");
        Totals(concat.Output, 125, 232, 107);
        Equal(false, concat.Output.Contains("files in base but not"), "concat suppresses file mismatch warnings");
        Contains(concat.Output, "BaseUnique");
        Contains(concat.Output, "DiffUnique");
        var concatCommon = Invoke(baseDir, diffDir, "--concat-files", "--no-reconcile");
        Totals(concatCommon.Output, 20, 24, 4);

        string renamed = Write("renamed.dasm", File.ReadAllText(diffFile));
        var single = Invoke(baseFile, renamed, "--warn");
        Equal(-1, single.Code, "unequal single filenames exit code");
        Totals(single.Output, 25, 32, 7);
        CheckTsv(single.Tsv, new[] { "Shared", "Removed", "Added" });
        Equal(false, single.Output.Contains("files in base but not"), "explicit files match despite unequal names");

        var identical = Invoke(baseFile, baseFile, "--warn");
        Equal(0, identical.Code, "identical input exit code");
        Totals(identical.Output, 25, 25, 0);
        CheckTsv(identical.Tsv, Array.Empty<string>());
    }

    private static void Totals(string output, int before, int after, int delta)
    {
        Contains(output, $"Total bytes of base: {before}\nTotal bytes of diff: {after}\nTotal bytes of delta: {delta} (");
    }

    private static void ProcessHelpers()
    {
        const string key = "jit-analyze-test.value";
        ProcessResult legacy = Utility.ExecuteProcess("git",
            new[] { "-c", $"\"{key}=two words\"", "config", "--get", key },
            capture: true, workingDirectory: root);
        Equal(0, legacy.ExitCode, "legacy process exit code");
        Equal("two words" + Environment.NewLine, legacy.StdOut, "legacy pre-quoted arguments");
        Equal("", legacy.StdErr, "legacy process stderr");

        const string value = "spaces \"quotes\" backslash\\ and\ttabs";
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = root };
        foreach (string argument in new[] { "-c", $"{key}={value}", "config", "--get", key })
            startInfo.ArgumentList.Add(argument);
        ProcessResult result = Utility.ExecuteProcess(startInfo, capture: true);
        Equal(0, result.ExitCode, "argument-list process exit code");
        Equal(value + Environment.NewLine, result.StdOut, "literal argument boundaries");
        Equal("", result.StdErr, "argument-list process stderr");

        startInfo = new ProcessStartInfo("git");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Path.Combine(root, "missing directory"));
        startInfo.ArgumentList.Add("status");
        result = Utility.ExecuteProcess(startInfo, capture: true);
        Equal(128, result.ExitCode, "failed child exit code");
        Equal("", result.StdOut, "failed child stdout");
        Equal(true, result.StdErr.Length > 0, "failed child stderr captured");
    }

    private static void TextDiffs()
    {
        string before = Path.Combine(root, "text base");
        string after = Path.Combine(root, "text diff");
        string textOnly = Write("text base/nested/text only.dasm", Method("Same", 10) + "; old\n");
        Write("text diff/nested/text only.dasm", Method("Same", 10) + "; new\n");
        string binary = Write("text base/binary.dasm", "\0old");
        Write("text diff/binary.dasm", "\0new");
        string longFile = Write("text base/long.dasm", new string('x', 131072) + "a\n");
        Write("text diff/long.dasm", new string('x', 131072) + "b\n");
        Write("text base/identical.dasm", Method("Unchanged", 5));
        Write("text diff/identical.dasm", Method("Unchanged", 5));
        string metricChanged = Write("text base/metric-changed.dasm", Method("Changed", 10));
        Write("text diff/metric-changed.dasm", Method("Changed", 11));
        Write("text base/removed.dasm", "removed");
        Write("text diff/added.dasm", "added");
        if (!OperatingSystem.IsWindows())
        {
            Write("text base/tab\tand\nnewline.dasm", "old\n");
            Write("text diff/tab\tand\nnewline.dasm", "new\n");
            Directory.CreateSymbolicLink(Path.Combine(before, "directory-link"), before);
            Directory.CreateSymbolicLink(Path.Combine(after, "directory-link"), after);
            File.CreateSymbolicLink(Path.Combine(before, "dangling-link"), "missing-base");
            File.CreateSymbolicLink(Path.Combine(after, "dangling-link"), "missing-diff");
        }

        Dictionary<string, int> counts = Analyzer.DiffInText(after, before);
        Equal(OperatingSystem.IsWindows() ? 4 : 7, counts.Count, "text diff file count");
        Equal(2, counts[textOnly], "text-only diff count");
        Equal(0, counts[binary], "binary diff count");
        Equal(2, counts[longFile], "difference after multiple buffers");
        Equal(2, counts[metricChanged], "full counts remain available for metric changes");
        Equal(0, Analyzer.DiffInText(before, before).Count, "identical trees");
        Equal(2, Analyzer.DiffInText(Path.Combine(after, "nested/text only.dasm"), textOnly)[textOnly], "single file counts");
        if (!OperatingSystem.IsWindows())
        {
            Equal(2, Analyzer.DiffInText(Path.Combine(after, "directory-link"), Path.Combine(before, "directory-link"))
                [Path.Combine(before, "directory-link")], "directory links are compared without traversal");
            Directory.Delete(Path.Combine(before, "directory-link"));
            Directory.Delete(Path.Combine(after, "directory-link"));
        }

        string[] args = { "--base", before, "--diff", after, "--recursive" };
        TextWriter oldOut = Console.Out;
        using var stdout = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Equal(-1, new JitAnalyzeRootCommand(args).Parse(args).Invoke(), "mixed text and metric exit code");
        }
        finally
        {
            Console.SetOut(oldOut);
        }
        Contains(stdout.ToString(), $"nested{Path.DirectorySeparatorChar}text only.dasm had 2 diffs");
        Contains(stdout.ToString(), $"Found {(OperatingSystem.IsWindows() ? 4 : 6)} files with textual diffs.");
        Equal(false, stdout.ToString().Contains("metric-changed.dasm had"), "metric changes do not need displayed line counts");
    }

    private static void CheckTsv(string tsv, string[] methods, int headers = 1)
    {
        string expectedHeader = "File\tMethod" + string.Concat(MetricNames.Select(n =>
            $"\tBase {n}\tDiff {n}\tDelta {n}\tPercentage {n}"));
        string[] lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Equal(headers, lines.Count(l => l == expectedHeader), "TSV headers (one per metric)");
        string[] rows = lines.Where(l => l != expectedHeader).ToArray();
        Equal(string.Join(",", methods), string.Join(",", rows.Select(l => l.Split('\t')[1])), "TSV row order");
        foreach (string row in rows)
        {
            string[] fields = row.Split('\t');
            Equal(51, fields.Length, "TSV field count including trailing tab");
            Equal("keep.dasm", fields[0], "TSV uses base filename");
            double[] before = fields[1] switch
            {
                "Shared" => new double[] { 10, 2, 0 },
                "Removed" => new double[] { 5, 1, 0 },
                "Added" => new double[] { 0, 0, 0 },
                "PerfOnly" => new double[] { 3, 1, 1 },
                _ => throw new Exception("Unexpected TSV method")
            };
            double[] after = fields[1] switch
            {
                "Shared" => new double[] { 14, 1, 0 },
                "Removed" => new double[] { 0, 0, 0 },
                "Added" => new double[] { 8, 2, 0 },
                "PerfOnly" => new double[] { 3, 1, 2 },
                _ => throw new Exception("Unexpected TSV method")
            };
            for (int i = 0; i < MetricNames.Length; i++)
            {
                double b = i < before.Length ? before[i] : 0;
                double d = i < after.Length ? after[i] : 0;
                double[] expected = { b, d, d - b, b == 0 ? 0 : (d - b) / b };
                for (int j = 0; j < expected.Length; j++)
                    Equal(expected[j].ToString(CultureInfo.InvariantCulture), fields[2 + i * 4 + j], $"TSV {fields[1]} {MetricNames[i]} column {j}");
            }
            Equal("", fields[^1], "TSV trailing tab");
        }
    }

    private static (int Code, string Output, string Tsv) Invoke(string before, string after, params string[] options)
    {
        string tsv = Path.Combine(root, "result.tsv");
        string json = Path.Combine(root, "result.json");
        string markdown = Path.Combine(root, "result.md");
        File.Delete(tsv);
        string[] args = new[] { "--base", before, "--diff", after, "--skip-text-diff", "--tsv", tsv, "--json", json, "--md", markdown }.Concat(options).ToArray();
        TextWriter oldOut = Console.Out;
        TextWriter oldError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int code;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            code = new JitAnalyzeRootCommand(args).Parse(args).Invoke();
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldError);
        }
        Equal("", stderr.ToString(), "CLI stderr");
        string output = stdout.ToString().Replace("\r\n", "\n");
        string table = File.ReadAllText(tsv).Replace("\r\n", "\n");
        string jsonOutput = File.ReadAllText(json);
        string markdownOutput = File.ReadAllText(markdown);
        if (baseline != null)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(baseline) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args)
                process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.Environment["LC_ALL"] = "C";
            process.StartInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
            File.Delete(tsv);
            process.Start();
            var baselineOut = process.StandardOutput.ReadToEndAsync();
            var baselineError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Equal(code & 255, process.ExitCode & 255, "baseline exit code");
            Equal("", baselineError.GetAwaiter().GetResult(), "baseline stderr");
            Equal(output, baselineOut.GetAwaiter().GetResult().Replace("\r\n", "\n"), "baseline stdout");
            Equal(table, File.ReadAllText(tsv).Replace("\r\n", "\n"), "baseline TSV");
            Equal(jsonOutput, File.ReadAllText(json), "baseline JSON");
            Equal(markdownOutput, File.ReadAllText(markdown), "baseline markdown");
        }
        return (code, output, table);
    }
}
