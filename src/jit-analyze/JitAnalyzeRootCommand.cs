// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;

namespace ManagedCodeGen
{
    internal sealed class JitAnalyzeRootCommand : CliRootCommand
    {
        public CliOption<string> BasePath { get; } =
            new("--base", "-b") { Description = "Base file or directory" };
        public CliOption<string> DiffPath { get; } =
            new("--diff", "-d") { Description = "Diff file or directory" };
        public CliOption<bool> Recursive { get; } =
            new("--recursive", "-r") { Description = "Search directories recursively" };
        public CliOption<string> FileExtension { get; } =
            new("--file-extension") { DefaultValueFactory = _ => ".dasm", Description = "File extension to look for" };
        public CliOption<int> Count { get; } =
            new("--count", "-c") { DefaultValueFactory = _ => 20, Description = "Count of files and methods (at most) to output in the summary. (count) improvements and (count) regressions of each will be included" };
        public CliOption<bool> Warn { get; } =
            new("--warn", "-w") { Description = "Generate warning output for files/methods that only exists in one dataset or the other (only in base or only in diff)" };
        public CliOption<List<string>> Metrics { get; } =
            new("--metrics", "-m") { DefaultValueFactory = _ => new List<string> { "CodeSize" }, Description = $"Metrics to use for diff computations. Available metrics: {MetricCollection.ListMetrics()}" };
        public CliOption<string> Note { get; } =
            new("--note") { Description = "Descriptive note to add to summary output" };
        public CliOption<bool> NoReconcile { get; } =
            new("--no-reconcile") { Description = "Do not reconcile unique methods in base/diff" };
        public CliOption<string> Json { get; } =
            new("--json") { Description = "Dump analysis data to specified file in JSON format" };
        public CliOption<string> Tsv { get; } =
            new("--tsv") { Description = "Dump analysis data to specified file in tab-separated format" };
        public CliOption<string> MD { get; } =
            new("--md") { Description = "Dump analysis data to specified file in markdown format" };
        public CliOption<string> Filter { get; } =
            new("--filter") { Description = "Only consider assembly files whose names match the filter" };
        public CliOption<bool> SkipTextDiff { get; } =
            new("--skip-text-diff") { Description = "Skip analysis that checks for files that have textual diffs but no metric diffs" };
        public CliOption<bool> RetainOnlyTopFiles { get; } =
            new("--retain-only-top-files") { Description = "Retain only the top 'count' improvements/regressions .dasm files. Delete other files. Useful in CI scenario to reduce the upload size" };
        public CliOption<double> OverrideTotalBaseMetric { get; } =
            new("--override-total-base-metric") { CustomParser = result =>
            {
                string optionValue = result.Tokens[0].Value;
                if (double.TryParse(optionValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
                    return parsedValue;

                 result.AddError($"Cannot parse argument '{optionValue}' for option '--override-total-base-metric' as expected type '{typeof(double).FullName}'.");
                 return 0;
            }, Description = "Override the total base metric shown in the output with this value. Useful when only changed .dasm files are present and these values are known" };
        public CliOption<double> OverrideTotalDiffMetric { get; } =
            new("--override-total-diff-metric") { CustomParser = result =>
            {
                string optionValue = result.Tokens[0].Value;
                if (double.TryParse(optionValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
                    return parsedValue;

                 result.AddError($"Cannot parse argument '{optionValue}' for option '--override-total-diff-metric' as expected type '{typeof(double).FullName}'.");
                 return 0;
            }, Description = "Override the total diff metric shown in the output with this value. Useful when only changed .dasm files are present and these values are known" };
        public CliOption<bool> IsDiffsOnly { get; } =
            new("--is-diffs-only") { Description = "Specify that the disassembly files are only produced for contexts with diffs, so avoid producing output making assumptions about the number of contexts" };
        public CliOption<bool> IsSubsetOfDiffs { get; } =
            new("--is-subset-of-diffs") { Description = "Specify that the disassembly files are only a subset of the contexts with diffs, so avoid producing output making assumptions about the remaining diffs" };
        public CliOption<bool> ConcatFiles { get; } =
            new("--concat-files") { Description = "Consider all files in the base and diff to be part of the same logical unit of functions" };

        public ParseResult Result;

        public JitAnalyzeRootCommand(string[] args) : base("Compare and analyze `*.dasm` files from baseline/diff")
        {
            Options.Add(BasePath);
            Options.Add(DiffPath);
            Options.Add(Recursive);
            Options.Add(FileExtension);
            Options.Add(Count);
            Options.Add(Warn);
            Options.Add(Metrics);
            Options.Add(Note);
            Options.Add(NoReconcile);
            Options.Add(Json);
            Options.Add(Tsv);
            Options.Add(MD);
            Options.Add(Filter);
            Options.Add(SkipTextDiff);
            Options.Add(RetainOnlyTopFiles);
            Options.Add(OverrideTotalBaseMetric);
            Options.Add(OverrideTotalDiffMetric);
            Options.Add(IsDiffsOnly);
            Options.Add(IsSubsetOfDiffs);
            Options.Add(ConcatFiles);

            SetAction(result =>
            {
                Result = result;

                try
                {
                    List<string> errors = new();
                    if (Result.GetValue(BasePath) == null)
                    {
                        errors.Add("Base path (--base) is required");
                    }

                    if (Result.GetValue(DiffPath) == null)
                    {
                        errors.Add("Diff path (--diff) is required");
                    }

                    foreach (string metricName in Result.GetValue(Metrics))
                    {
                        if (!MetricCollection.ValidateMetric(metricName))
                        {
                            errors.Add($"Unknown metric '{metricName}'. Available metrics: {MetricCollection.ListMetrics()}");
                        }
                    }

                    if ((Result.GetResult(OverrideTotalBaseMetric) == null) != (Result.GetResult(OverrideTotalDiffMetric) == null))
                    {
                        errors.Add("override-total-base-metric and override-total-diff-metric must either both be specified or both not be specified");
                    }

                    if (errors.Count > 0)
                    {
                        throw new Exception(string.Join(Environment.NewLine, errors));
                    }

                    return new Program(this).Run();
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
            });
        }
    }
}
