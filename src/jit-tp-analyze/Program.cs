// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ManagedCodeGen;

internal class Program
{
    private static readonly Regex _traceLineRegex = new("(\\d+) +: (.*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _baseTracePath;
    private readonly string _diffTracePath;
    private readonly double _noise;

    public Program(JitTpAnalyzeRootCommand command, ParseResult result)
    {
        _baseTracePath = result.GetValue(command.BasePath);
        _diffTracePath = result.GetValue(command.DiffPath);
        _noise = result.GetValue(command.Noise);
    }

    public void Run()
    {
        TextWriter output = Console.Out;
        Dictionary<string, long> baseTrace = ParseTrace(_baseTracePath);
        Dictionary<string, long> diffTrace = ParseTrace(_diffTracePath);
        HashSet<string> allRecordedFunctions = new();
        foreach (var function in baseTrace)
        {
            allRecordedFunctions.Add(function.Key);
        }
        foreach (var function in diffTrace)
        {
            allRecordedFunctions.Add(function.Key);
        }

        long baseTotalInsCount = baseTrace.Sum(x => x.Value);
        long diffTotalInsCount = diffTrace.Sum(x => x.Value);
        double totalPercentageDiff = GetPercentageDiff(baseTotalInsCount, diffTotalInsCount);
        output.WriteLine($"Base: {baseTotalInsCount}, Diff: {diffTotalInsCount}, {FormatPercentageDiff(totalPercentageDiff, "0000")}");
        output.WriteLine();

        // Now create a list of functions which contributed to the difference.
        long totalAbsInsCountDiff = 0;
        List<FunctionDiff> diffs = new();
        foreach (string functionName in allRecordedFunctions)
        {
            long diffInsCount = diffTrace.GetValueOrDefault(functionName);
            long baseInsCount = baseTrace.GetValueOrDefault(functionName);
            long insCountDiff = diffInsCount - baseInsCount;
            if (insCountDiff == 0)
            {
                continue;
            }

            diffs.Add(new()
            {
                Name = functionName,
                InsCountDiff = insCountDiff,
                InsPercentageDiff = GetPercentageDiff(baseInsCount, diffInsCount),
                TotalInsPercentageDiff = (double)insCountDiff / baseTotalInsCount * 100
            });

            totalAbsInsCountDiff += Math.Abs(insCountDiff);
        }

        foreach (ref FunctionDiff diff in CollectionsMarshal.AsSpan(diffs))
        {
            diff.ContributionPercentage = (double)Math.Abs(diff.InsCountDiff) / totalAbsInsCountDiff * 100;
        }

        // Filter out functions below the noise level.
        diffs = diffs.Where(d => d.ContributionPercentage > _noise).ToList();
        diffs.Sort((x, y) => y.InsCountDiff.CompareTo(x.InsCountDiff));

        int maxNameLength = 0;
        int maxInsCountDiffLength = 0;
        int maxInsPercentageDiffLength = 0;
        foreach (ref FunctionDiff diff in CollectionsMarshal.AsSpan(diffs))
        {
            maxNameLength = Math.Max(maxNameLength, diff.Name.Length);
            maxInsCountDiffLength = Math.Max(maxInsCountDiffLength, $"{diff.InsCountDiff}".Length);
            maxInsPercentageDiffLength = Math.Max(maxInsPercentageDiffLength, FormatPercentageDiff(diff.InsPercentageDiff).Length);
        }

        foreach (ref FunctionDiff diff in CollectionsMarshal.AsSpan(diffs))
        {
            output.WriteLine(
                $"{{0,-{maxNameLength}}} : {{1,-{maxInsCountDiffLength}}} : {{2,-{maxInsPercentageDiffLength}}} : {{3,-6:P2}} : {{4}}",
                diff.Name,
                diff.InsCountDiff,
                double.IsInfinity(diff.InsPercentageDiff) ? "NA" : FormatPercentageDiff(diff.InsPercentageDiff),
                diff.ContributionPercentage / 100,
                FormatPercentageDiff(diff.TotalInsPercentageDiff, "0000"));
        }
    }

    private Dictionary<string, long> ParseTrace(string path)
    {
        Dictionary<string, long> trace = new();
        foreach (string line in File.ReadLines(path))
        {
            Match match = _traceLineRegex.Match(line);
            if (match.Success)
            {
                trace.Add(match.Groups[2].Value, long.Parse(match.Groups[1].Value));
            }
        }

        return trace;
    }

    private static double GetPercentageDiff(double baseValue, double diffValue) =>
        (diffValue - baseValue) / baseValue * 100;

    private static string FormatPercentageDiff(double value, string precision = "00") =>
        (value > 0 ? "+" : "") + value.ToString($"0.{precision}") + "%";

    private static void Main(string[] args) =>
        new CommandLineConfiguration(new JitTpAnalyzeRootCommand().UseVersion()).Invoke(args);

    private struct FunctionDiff
    {
        public string Name;
        public long InsCountDiff;
        public double InsPercentageDiff;
        public double ContributionPercentage;
        public double TotalInsPercentageDiff;
    }
}

internal class JitTpAnalyzeRootCommand : RootCommand
{
    public JitTpAnalyzeRootCommand() : base("Compare PIN-based throughput traces")
    {
        Options.Add(BasePath);
        Options.Add(DiffPath);
        Options.Add(Noise);

        SetAction(result =>
        {
            try
            {
                Program jitTpDiff = new(this, result);
                jitTpDiff.Run();
            }
            catch (Exception e)
            {
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error: " + e.Message);
                Console.Error.WriteLine(e.ToString());
                Console.ResetColor();
                return 1;
            }

            return 0;
        });
    }

    public Option<string> BasePath { get; } =
        new("--base", "-b") { Description = "Base trace file", DefaultValueFactory = (_) => "basetp.txt" };
    public Option<string> DiffPath { get; } =
        new("--diff", "-d") { Description = "Diff trace file", DefaultValueFactory = (_) => "difftp.txt" };
    public Option<double> Noise { get; } =
        new("--noise", "-n") { Description = "Minimal contribution percentage for inclusion into the summary", DefaultValueFactory = (_) => 0.1 };
}
