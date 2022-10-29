// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;

namespace ManagedCodeGen
{
    internal sealed class JitAnalyzeRootCommand : RootCommand
    {
        public Option<string> BasePath { get; } =
            new(new[] { "--base", "-b" }, "Base file or directory");
        public Option<string> DiffPath { get; } =
            new(new[] { "--diff", "-d" }, "Diff file or directory");
        public Option<bool> Recursive { get; } =
            new(new[] { "--recursive", "-r" }, "Search directories recursively");
        public Option<string> FileExtension { get; } =
            new("--file-extension", _ => ".dasm", true, "File extension to look for");
        public Option<int> Count { get; } =
            new(new[] { "--count", "-c" }, _ => 20, true, "Count of files and methods (at most) to output in the summary. (count) improvements and (count) regressions of each will be included");
        public Option<bool> Warn { get; } =
            new(new[] { "--warn", "-w" }, "Generate warning output for files/methods that only exists in one dataset or the other (only in base or only in diff)");
        public Option<List<string>> Metrics { get; } =
            new(new[] { "--metrics", "-m" }, _ =>  new List<string> { "CodeSize" }, true, $"Comma-separated metric to use for diff computations. Available metrics: {MetricCollection.ListMetrics()}");
        public Option<string> Note { get; } =
            new("--note", "Descriptive note to add to summary output");
        public Option<bool> NoReconcile { get; } =
            new("--no-reconcile", "Do not reconcile unique methods in base/diff");
        public Option<string> Json { get; } =
            new("--json", "Dump analysis data to specified file in JSON format");
        public Option<string> Tsv { get; } =
            new("--tsv", "Dump analysis data to specified file in tab-separated format");
        public Option<string> MD { get; } =
            new("--md", "Dump analysis data to specified file in markdown format");
        public Option<string> Filter { get; } =
            new("--filter", "Only consider assembly files whose names match the filter");
        public Option<bool> SkipTextDiff { get; } =
            new("--skip-text-diff", "Skip analysis that checks for files that have textual diffs but no metric diffs");
        public Option<bool> RetainOnlyTopFiles { get; } =
            new("--retain-only-top-files", "Retain only the top 'count' improvements/regressions .dasm files. Delete other files. Useful in CI scenario to reduce the upload size");
        public Option<double> OverrideTotalBaseMetric { get; } =
            new("--override-total-base-metric", "Override the total base metric shown in the output with this value. Useful when only changed .dasm files are present and these values are known");
        public Option<double> OverrideTotalDiffMetric { get; } =
            new("--override-total-diff-metric", "Override the total diff metric shown in the output with this value. Useful when only changed .dasm files are present and these values are known");
        public Option<bool> IsDiffsOnly { get; } =
            new("--is-diffs-only", "Specify that the disassembly files are only produced for contexts with diffs, so avoid producing output making assumptions about the number of contexts");
        public Option<bool> IsSubsetOfDiffs { get; } =
            new("--is-subset-of-diffs", "Specify that the disassembly files are only a subset of the contexts with diffs, so avoid producing output making assumptions about the remaining diffs");

        public ParseResult Result;

        public JitAnalyzeRootCommand(string[] args) : base("Compare and analyze `*.dasm` files from baseline/diff")
        {
            AddOption(BasePath);
            AddOption(DiffPath);
            AddOption(Recursive);
            AddOption(FileExtension);
            AddOption(Count);
            AddOption(Warn);
            AddOption(Metrics);
            AddOption(Note);
            AddOption(NoReconcile);
            AddOption(Json);
            AddOption(Tsv);
            AddOption(MD);
            AddOption(Filter);
            AddOption(SkipTextDiff);
            AddOption(RetainOnlyTopFiles);
            AddOption(OverrideTotalBaseMetric);
            AddOption(OverrideTotalDiffMetric);
            AddOption(IsDiffsOnly);
            AddOption(IsSubsetOfDiffs);

            this.SetHandler(context =>
            {
                Result = context.ParseResult;

                try
                {
                    List<string> errors = new();
                    if (Result.GetValueForOption(BasePath) == null)
                    {
                        errors.Add("Base path (--base) is required");
                    }

                    if (Result.GetValueForOption(DiffPath) == null)
                    {
                        errors.Add("Diff path (--diff) is required");
                    }

                    foreach (string metricName in Result.GetValueForOption(Metrics))
                    {
                        if (!MetricCollection.ValidateMetric(metricName))
                        {
                            errors.Add($"Unknown metric '{metricName}'. Available metrics: {MetricCollection.ListMetrics()}");
                        }
                    }

                    if ((Result.FindResultFor(OverrideTotalBaseMetric) == null) != (Result.FindResultFor(OverrideTotalDiffMetric) == null))
                    {
                        errors.Add("override-total-base-metric and override-total-diff-metric must either both be specified or both not be specified");
                    }

                    if (errors.Count > 0)
                    {
                        throw new Exception(string.Join(Environment.NewLine, errors));
                    }

                    context.ExitCode = new Program(this).Run();
                }
                catch (Exception e)
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine("Error: " + e.Message);
                    Console.Error.WriteLine(e.ToString());

                    Console.ResetColor();

                    context.ExitCode = 1;
                }
            });
        }
    }
}
