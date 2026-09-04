// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ManagedCodeGen
{
    internal sealed partial class Program
    {
        private readonly JitAnalyzeRootCommand _command;
        private readonly bool _reconcile;
        private readonly string _filter;
        private readonly double _overrideTotalBaseMetric;
        private readonly double _overrideTotalDiffMetric;
        private readonly int _count;
        private readonly string _basePath;
        private readonly string _diffPath;

        private static string METRIC_SEP = new string('-', 80);

        private const string DETAILS_MARKER = "SUMMARY_MARKER";
        private const string DETAILS_TEXT =
@"```
<details>

<summary>Detail diffs</summary>

```
";

        public Program(JitAnalyzeRootCommand command)
        {
            _command = command;

            _reconcile = !Get(command.NoReconcile);
            _filter = Get(command.Filter);
            _overrideTotalBaseMetric = Get(command.OverrideTotalBaseMetric);
            _overrideTotalDiffMetric = Get(command.OverrideTotalDiffMetric);
            _count = Get(command.Count);
            _basePath = Get(command.BasePath);
            _diffPath = Get(command.DiffPath);
        }

        public class FileInfo
        {
            public string name;
            public string[] paths;
            public bool isExplicitOnlyFile;

            public override string ToString()
            {
                return name;
            }
        }

        // Custom comparer for the FileInfo class
        private class FileInfoComparer : IEqualityComparer<FileInfo>
        {
            public bool Equals(FileInfo x, FileInfo y)
            {
                if (Object.ReferenceEquals(x, y)) return true;
                if (Object.ReferenceEquals(x, null) || Object.ReferenceEquals(y, null))
                    return false;
                return x.name == y.name;
            }

            // If Equals() returns true for a pair of objects 
            // then GetHashCode() must return the same value for these objects.

            public int GetHashCode(FileInfo fi)
            {
                if (Object.ReferenceEquals(fi, null)) return 0;
                return fi.name == null ? 0 : fi.name.GetHashCode();
            }
        }

        public class MethodInfo
        {
            private MetricCollection metrics;
            public MetricCollection Metrics => metrics;
            public string name;
            public int functionCount;
            public List<int> functionOffsets;

            public MethodInfo()
            {
                metrics = new MetricCollection();
            }

            public override string ToString()
            {
                return string.Format(@"name {0}, {1}, function count {2}, offsets {3}",
                    name, metrics, functionCount,
                    string.Join(", ", functionOffsets.ToArray()));
            }
        }

        // Custom comparer for the MethodInfo class
        private class MethodInfoComparer : IEqualityComparer<MethodInfo>
        {
            public bool Equals(MethodInfo x, MethodInfo y)
            {
                if (Object.ReferenceEquals(x, y)) return true;
                if (Object.ReferenceEquals(x, null) || Object.ReferenceEquals(y, null))
                    return false;
                return x.name == y.name;
            }

            // If Equals() returns true for a pair of objects 
            // then GetHashCode() must return the same value for these objects.

            public int GetHashCode(MethodInfo mi)
            {
                if (Object.ReferenceEquals(mi, null)) return 0;
                return mi.name == null ? 0 : mi.name.GetHashCode();
            }
        }

        public class FileDelta
        {
            public string baseName;
            public string diffName;

            public MetricCollection baseMetrics;
            public MetricCollection diffMetrics;
            public MetricCollection deltaMetrics;
            public MetricCollection relDeltaMetrics;
            public MetricCollection reconciledBaseMetrics;
            public MetricCollection reconciledDiffMetrics;

            public int reconciledCountBase;
            public int reconciledCountDiff;
            public int methodsInBoth;
            public IEnumerable<MethodInfo> methodsOnlyInBase;
            public IEnumerable<MethodInfo> methodsOnlyInDiff;
            public IEnumerable<MethodDelta> methodDeltaList;
            public bool RetainFile = false;

            // Adjust lists to include empty methods in diff|base for methods that appear only in base|diff.
            // Also adjust delta to take these methods into account.
            public void Reconcile()
            {
                List<MethodDelta> reconciles = new List<MethodDelta>();

                reconciledBaseMetrics = new MetricCollection();
                reconciledDiffMetrics = new MetricCollection();

                foreach (MethodInfo m in methodsOnlyInBase)
                {
                    reconciles.Add(new MethodDelta
                    {
                        name = m.name,
                        baseMetrics = new MetricCollection(m.Metrics),
                        diffMetrics = new MetricCollection(),
                        baseOffsets = m.functionOffsets,
                        diffOffsets = null
                    });
                    baseMetrics.Add(m.Metrics);
                    reconciledBaseMetrics.Add(m.Metrics);
                    reconciledCountBase++;
                }

                foreach (MethodInfo m in methodsOnlyInDiff)
                {
                    reconciles.Add(new MethodDelta
                    {
                        name = m.name,
                        baseMetrics = new MetricCollection(),
                        diffMetrics = new MetricCollection(m.Metrics),
                        baseOffsets = null,
                        diffOffsets = m.functionOffsets
                    });
                    diffMetrics.Add(m.Metrics);
                    reconciledDiffMetrics.Add(m.Metrics);
                    reconciledCountDiff++;
                }

                methodDeltaList = methodDeltaList.Concat(reconciles);
                deltaMetrics.Sub(reconciledBaseMetrics);
                deltaMetrics.Add(reconciledDiffMetrics);
            }
        }

        public class MethodDelta
        {
            public string name;
            public MetricCollection baseMetrics;
            public MetricCollection diffMetrics;
            private MetricCollection _deltaMetrics;
            public MetricCollection deltaMetrics
            {
                get
                {
                    if (_deltaMetrics == null)
                    {
                        _deltaMetrics = new MetricCollection(diffMetrics);
                        _deltaMetrics.Sub(baseMetrics);
                    }

                    return _deltaMetrics;
                }
            }

            public MetricCollection _relDeltaMetrics;
            public MetricCollection relDeltaMetrics
            {
                get
                {
                    if (_relDeltaMetrics == null)
                    {
                        _relDeltaMetrics = new MetricCollection(diffMetrics);
                        _relDeltaMetrics.Rel(baseMetrics);
                    }

                    return _relDeltaMetrics;
                }
            }

            public IEnumerable<int> baseOffsets;
            public IEnumerable<int> diffOffsets;

            public override string ToString()
            {
                return name;
            }
        }

        public List<FileInfo> ExtractFileInfo(string path, string filter, string fileExtension, bool recursive)
        {
            // if path is a directory, enumerate files and extract
            // otherwise just extract.
            SearchOption searchOption = (recursive) ?
                SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string fullRootPath = Path.GetFullPath(path);

            if (Directory.Exists(fullRootPath))
            {
                string fileNamePattern = filter ?? "*";
                string searchPattern = fileNamePattern + fileExtension;
                if (Get(_command.ConcatFiles))
                {
                    return new List<FileInfo>
                    {
                        new FileInfo
                        {
                            name = Path.GetFileName(path),
                            paths = Directory.EnumerateFiles(fullRootPath, searchPattern, searchOption).ToArray(),
                            isExplicitOnlyFile = true,
                        },
                    };
                }

                return Directory.EnumerateFiles(fullRootPath, searchPattern, searchOption)
                         .Select(p => new FileInfo
                         {
                             name = p.Substring(fullRootPath.Length).TrimStart(Path.DirectorySeparatorChar),
                             paths = new[] { p }
                         }).ToList();
            }
            else
            {
                // In the single file case just create a list with a single
                // to satisfy the interface.
                return new List<FileInfo>
                { new FileInfo
                    {
                        name = Path.GetFileName(path),
                        paths = new[] { path },
                        isExplicitOnlyFile = true,
                    }
                };
            }
        }

        // Extract lines of the passed in file(s) and create a method info object
        // for each method descript line containing metrics like total bytes, prolog bytes, 
        // and offset in the file.
        //
        // This is the method that knows how to parse jit output and recover the metrics.
        private static IEnumerable<(string line, int index)> ReadMetricLines(string[] filePaths)
        {
            int index = 0;
            foreach (string path in filePaths)
            {
                using var reader = new DisassemblyReader(path);
                while (reader.ReadLine(out ReadOnlySpan<char> line))
                {
                    if (line.StartsWith("; Total bytes of code", StringComparison.Ordinal) ||
                        line.StartsWith("; Assembly listing for method", StringComparison.Ordinal) ||
                        line.StartsWith("; Variable debug info:", StringComparison.Ordinal))
                    {
                        yield return (line.ToString(), index);
                    }
                    index = checked(index + 1);
                }
            }
        }

        public static IEnumerable<MethodInfo> ExtractMethodInfo(string[] filePaths)
        {
            var methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            var lookup = methods.GetAlternateLookup<ReadOnlySpan<char>>();
            foreach (var record in ReadMetricLines(filePaths))
            {
                string line = record.line;
                int nameStart = line.IndexOf("for method ", StringComparison.Ordinal);
                ReadOnlySpan<char> name = nameStart < 0 ? ReadOnlySpan<char>.Empty : line.AsSpan(nameStart + 11);
                if (!lookup.TryGetValue(name, out MethodInfo method))
                {
                    method = new MethodInfo { name = name.ToString(), functionOffsets = new List<int>() };
                    methods.Add(method.name, method);
                }

                Match codeSize = CodeSizePattern().Match(line);
                int totalBytes = ReadInt(codeSize);
                if (totalBytes == 0)
                {
                    method.functionCount = checked(method.functionCount + 1);
                }
                if (!codeSize.Success && record.index != 0)
                {
                    method.functionOffsets.Add(record.index);
                }

                AddInt(method.Metrics, "CodeSize", totalBytes);
                AddInt(method.Metrics, "PrologSize", ReadInt(PrologSizePattern().Match(line)));
                method.Metrics.Add("PerfScore", ReadDouble(PerfScorePattern().Match(line), 2));
                AddInt(method.Metrics, "InstrCount", ReadInt(InstrCountPattern().Match(line)));
                AddInt(method.Metrics, "AllocSize", ReadInt(AllocSizePattern().Match(line)));
                Match debugInfo = DebugInfoPattern().Match(line);
                AddInt(method.Metrics, "DebugClauseCount", ReadInt(debugInfo));
                AddInt(method.Metrics, "DebugVarCount", ReadInt(debugInfo, 2));
                Match spillInfo = SpillInfoPattern().Match(line);
                AddInt(method.Metrics, "SpillCount", ReadInt(spillInfo));
                method.Metrics.Add("SpillWeight", ReadDouble(spillInfo, 2));
                Match resolutionInfo = ResolutionInfoPattern().Match(line);
                AddInt(method.Metrics, "ResolutionCount", ReadInt(resolutionInfo));
                method.Metrics.Add("ResolutionWeight", ReadDouble(resolutionInfo, 2));
            }

            foreach (MethodInfo method in methods.Values)
            {
                double totalCodeSize = method.Metrics.GetMetric("CodeSize").Value;
                double totalAllocSize = method.Metrics.GetMetric("AllocSize").Value;
                Debug.Assert(totalAllocSize == 0 || totalCodeSize <= totalAllocSize);
                method.Metrics.Add("ExtraAllocBytes", totalAllocSize == 0 ? 0 : totalAllocSize - totalCodeSize);
            }
            return methods.Values.ToList();

            static int ReadInt(Match match, int group = 1) =>
                match.Success ? int.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture) : 0;

            static double ReadDouble(Match match, int group) =>
                match.Success ? double.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture) : 0;

            static void AddInt(MetricCollection metrics, string name, int value)
            {
                Metric metric = metrics.GetMetric(name);
                metric.Value = checked((int)metric.Value + value);
            }
        }

        [GeneratedRegex(@"^; Total bytes of code ([0-9]{1,}).* for method ")]
        private static partial Regex CodeSizePattern();
        [GeneratedRegex(@"prolog size ([0-9]{1,})")]
        private static partial Regex PrologSizePattern();
        [GeneratedRegex(@"(PerfScore|perf score) (\d+(\.\d+)?)")]
        private static partial Regex PerfScorePattern();
        [GeneratedRegex(@"instruction count ([0-9]{1,})")]
        private static partial Regex InstrCountPattern();
        [GeneratedRegex(@"allocated bytes for code ([0-9]{1,})")]
        private static partial Regex AllocSizePattern();
        [GeneratedRegex(@"Variable debug info: ([0-9]{1,}) live range\(s\), ([0-9]{1,}) var\(s\)")]
        private static partial Regex DebugInfoPattern();
        [GeneratedRegex(@"SpillCount (\d+) SpillCountWt (\d+\.\d+)")]
        private static partial Regex SpillInfoPattern();
        [GeneratedRegex(@"ResolutionMovs (\d+) ResolutionMovsWt (\d+\.\d+)")]
        private static partial Regex ResolutionInfoPattern();

        // Compare base and diff file lists and produce a sorted list of method
        // deltas by file.  Delta is computed diffBytes - baseBytes so positive
        // numbers are regressions. (lower is better)
        //
        // Todo: handle metrics where "higher is better"
        public FileDelta[][] Comparator(IEnumerable<FileInfo> baseInfo,
            IEnumerable<FileInfo> diffInfo, string[] metricNames)
        {
            // Keep only one pair's parsed methods per worker, and reuse them for every metric.
            return baseInfo.Join(diffInfo, b => b.isExplicitOnlyFile ? "" : b.name,
                d => d.isExplicitOnlyFile ? "" : d.name, (b, d) => (Base: b, Diff: d))
                .AsParallel().AsOrdered()
                .WithDegreeOfParallelism(Math.Min(Environment.ProcessorCount, 8))
                .Select(pair =>
                {
                    var baseMethods = ExtractMethodInfo(pair.Base.paths);
                    var diffMethods = ExtractMethodInfo(pair.Diff.paths);
                    return metricNames.Select(metricName =>
                        CompareFile(pair.Base.name, pair.Diff.name, baseMethods, diffMethods, metricName)).ToArray();
                }).ToArray();
        }

        private FileDelta CompareFile(string baseName, string diffName,
            IEnumerable<MethodInfo> baseMethods, IEnumerable<MethodInfo> diffMethods, string metricName)
        {
            MethodInfoComparer methodInfoComparer = new MethodInfoComparer();
            var jointList = baseMethods.Join(diffMethods,
                    x => x.name, y => y.name, (x, y) => new MethodDelta
                    {
                        name = x.name,
                        baseMetrics = new MetricCollection(x.Metrics),
                        diffMetrics = new MetricCollection(y.Metrics),
                        baseOffsets = x.functionOffsets,
                        diffOffsets = y.functionOffsets
                    })
                    .OrderByDescending(r => r.deltaMetrics.GetMetric(metricName).Value)
                    .ToList();

            FileDelta f = new FileDelta
            {
                baseName = baseName,
                diffName = diffName,
                baseMetrics = jointList.Sum(x => x.baseMetrics),
                diffMetrics = jointList.Sum(x => x.diffMetrics),
                deltaMetrics = jointList.Sum(x => x.deltaMetrics),
                relDeltaMetrics = jointList.Sum(x => x.relDeltaMetrics),
                methodsInBoth = jointList.Count(),
                methodsOnlyInBase = baseMethods.Except(diffMethods, methodInfoComparer).ToList(),
                methodsOnlyInDiff = diffMethods.Except(baseMethods, methodInfoComparer).ToList(),
                methodDeltaList = jointList.Where(x => x.deltaMetrics.GetMetric(metricName).Value != 0).ToList()
            };

            if (_reconcile)
            {
                f.Reconcile();
            }

            return f;
        }

        // Summarize differences across all the files.
        // Output:
        //     Total bytes differences
        //     Top files by difference size
        //     Top diffs by size across all files
        //     Top diffs by percentage size across all files
        //
        public (int, string) Summarize(IEnumerable<FileDelta> fileDeltaList, string metricName)
        {
            StringBuilder summaryContents = new StringBuilder();

            var totalRelDeltaMetrics = fileDeltaList.Sum(x => x.relDeltaMetrics);
            var totalDeltaMetrics = fileDeltaList.Sum(x => x.deltaMetrics);
            var totalBaseMetrics = fileDeltaList.Sum(x => x.baseMetrics);
            var totalDiffMetrics = fileDeltaList.Sum(x => x.diffMetrics);

            string note = Get(_command.Note);
            if (note != null)
            {
                summaryContents.AppendLine($"\n{note}");
            }

            Metric totalBaseMetric = totalBaseMetrics.GetMetric(metricName);
            Metric totalDiffMetric = totalDiffMetrics.GetMetric(metricName);
            Metric totalDeltaMetric = totalDeltaMetrics.GetMetric(metricName);
            Metric totalRelDeltaMetric = totalRelDeltaMetrics.GetMetric(metricName);
            string unitName = totalBaseMetrics.GetMetric(metricName).Unit;
            string metricDisplayName = totalBaseMetrics.GetMetric(metricName).DisplayName;

            summaryContents.Append($"\nSummary of {metricDisplayName} diffs:");
            if (_filter != null)
            {
                summaryContents.Append($" (using filter '{_filter}')");
            }
            summaryContents.AppendLine(string.Format("\n({0} is better)\n", totalBaseMetric.LowerIsBetter ? "Lower" : "Higher"));

            if (_command.Result.GetResult(_command.OverrideTotalBaseMetric) != null)
            {
                Debug.Assert(_command.Result.GetResult(_command.OverrideTotalDiffMetric) != null);
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of base: {1} (overridden on cmd)", unitName, _overrideTotalBaseMetric));
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of diff: {1} (overridden on cmd)", unitName, _overrideTotalDiffMetric));
                double delta = _overrideTotalDiffMetric - _overrideTotalBaseMetric;
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of delta: {1} ({2:P} of base)", unitName, delta, delta / _overrideTotalBaseMetric));
            }
            else if (totalBaseMetric.Value != 0)
            {
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of base: {1}", unitName, totalBaseMetric.Value));
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of diff: {1}", unitName, totalDiffMetric.Value));
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of delta: {1} ({2:P} of base)", unitName, totalDeltaMetric.ValueString, totalDeltaMetric.Value / totalBaseMetric.Value));
                if (totalRelDeltaMetric.Value != 0)
                {
                    summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total relative delta: {0:0.00}", totalRelDeltaMetric.Value));
                }
            }
            else 
            {
                summaryContents.AppendLine(string.Format("Warning: the base metric is 0, the diff metric is {0}, have you used a release version?", totalDiffMetrics.GetMetric(metricName).ValueString));
            }

            if (totalDeltaMetric.Value != 0)
            {
                summaryContents.AppendLine(string.Format("    diff is {0}", totalDeltaMetric.LowerIsBetter == (totalDeltaMetric.Value < 0) ? "an improvement." : "a regression."));
            }
            if (totalRelDeltaMetric.Value != 0)
            {
                summaryContents.AppendLine(string.Format("    relative diff is {0}", totalRelDeltaMetric.LowerIsBetter == (totalRelDeltaMetric.Value < 0) ? "an improvement." : "a regression."));
            }

            summaryContents.AppendLine(DETAILS_MARKER);

            if (_reconcile)
            {
                // See if base or diff had any unique methods
                var uniqueToBase = fileDeltaList.Sum(x => x.reconciledCountBase);
                var uniqueToDiff = fileDeltaList.Sum(x => x.reconciledCountDiff);

                // Only dump reconciliation stats if there was at least one unique
                if (uniqueToBase + uniqueToDiff > 0)
                {
                    var reconciledBaseMetrics = fileDeltaList.Sum(x => x.reconciledBaseMetrics);
                    var reconciledDiffMetrics = fileDeltaList.Sum(x => x.reconciledDiffMetrics);
                    Metric reconciledBaseMetric = reconciledBaseMetrics.GetMetric(metricName);
                    Metric reconciledDiffMetric = reconciledDiffMetrics.GetMetric(metricName);

                    summaryContents.AppendLine(string.Format("\nTotal {0} diff includes {1} {0}s from reconciling methods", unitName,
                        reconciledDiffMetric.Value - reconciledBaseMetric.Value));
                    summaryContents.AppendLine(string.Format("\tBase had {0,4} unique methods, {1,8} unique {2}s", uniqueToBase, reconciledBaseMetric.ValueString, unitName));
                    summaryContents.AppendLine(string.Format("\tDiff had {0,4} unique methods, {1,8} unique {2}s", uniqueToDiff, reconciledDiffMetric.ValueString, unitName));
                }
            }

            if (_count == 0)
            {
                return (Math.Abs(totalDeltaMetric.Value) == 0 ? 0 : -1, summaryContents.ToString());
            }

            // Todo: handle higher is better metrics
            var sortedFileImprovements = fileDeltaList
                                            .Where(x => x.deltaMetrics.GetMetric(metricName).Value < 0)
                                            .OrderBy(d => d.deltaMetrics.GetMetric(metricName).Value).ToList();

            var sortedFileRegressions = fileDeltaList
                                            .Where(x => x.deltaMetrics.GetMetric(metricName).Value > 0)
                                            .OrderByDescending(d => d.deltaMetrics.GetMetric(metricName).Value).ToList();

            int fileImprovementCount = sortedFileImprovements.Count();
            int fileRegressionCount = sortedFileRegressions.Count();
            int sortedFileCount = fileImprovementCount + fileRegressionCount;
            int unchangedFileCount = fileDeltaList.Count() - sortedFileCount;
            bool retainOnlyTopFiles = Get(_command.RetainOnlyTopFiles);

            void DisplayFileMetric(string headerText, int metricCount, IEnumerable<FileDelta> list)
            {
                if (metricCount > 0)
                {
                    summaryContents.AppendLine($"\n{headerText} ({unitName}s):");
                    foreach (var fileDelta in list.Take(Math.Min(metricCount, _count)))
                    {
                        summaryContents.AppendLine(string.Format("    {1,8} : {0} ({2:P} of base)", fileDelta.diffName,
                            fileDelta.deltaMetrics.GetMetric(metricName).ValueString,
                            fileDelta.deltaMetrics.GetMetric(metricName).Value / fileDelta.baseMetrics.GetMetric(metricName).Value));

                        fileDelta.RetainFile = retainOnlyTopFiles;
                    }
                }
            }

            DisplayFileMetric("Top file regressions", fileRegressionCount, sortedFileRegressions);
            DisplayFileMetric("Top file improvements", fileImprovementCount, sortedFileImprovements);

            summaryContents.AppendLine($"\n{sortedFileCount} total files with {metricDisplayName} differences ({fileImprovementCount} improved, {fileRegressionCount} regressed), {unchangedFileCount} unchanged.");

            var methodDeltaList = fileDeltaList
                                        .SelectMany(fd => fd.methodDeltaList, (fd, md) => new
                                        {
                                            path = fd.baseName,
                                            name = md.name,
                                            deltaMetric = md.deltaMetrics.GetMetric(metricName),
                                            baseMetric = md.baseMetrics.GetMetric(metricName),
                                            diffMetric = md.diffMetrics.GetMetric(metricName),
                                            baseCount = md.baseOffsets == null ? 0 : md.baseOffsets.Count(),
                                            diffCount = md.diffOffsets == null ? 0 : md.diffOffsets.Count()
                                        }).ToList();
            var sortedMethodImprovements = methodDeltaList
                                            .Where(x => x.deltaMetric.Value< 0)
                                            .OrderBy(d => d.deltaMetric.Value)
                                            .ThenBy(d => d.name)
                                            .ToList();
            var sortedMethodRegressions = methodDeltaList
                                            .Where(x => x.deltaMetric.Value > 0)
                                            .OrderByDescending(d => d.deltaMetric.Value)
                                            .ThenBy(d => d.name)
                                            .ToList();
            int methodImprovementCount = sortedMethodImprovements.Count();
            int methodRegressionCount = sortedMethodRegressions.Count();
            int sortedMethodCount = methodImprovementCount + methodRegressionCount;
            int unchangedMethodCount = fileDeltaList.Sum(x => x.methodsInBoth) - sortedMethodCount;

            var sortedMethodImprovementsByPercentage = methodDeltaList
                                            .Where(x => x.deltaMetric.Value < 0)
                                            .OrderBy(d => d.deltaMetric.Value / d.baseMetric.Value)
                                            .ThenBy(d => d.name)
                                            .ToList();
            var sortedMethodRegressionsByPercentage = methodDeltaList
                                            .Where(x => x.deltaMetric.Value > 0)
                                            .OrderByDescending(d => d.deltaMetric.Value / d.baseMetric.Value)
                                            .ThenBy(d => d.name)
                                            .ToList();

            void DisplayMethodMetric(string headerText, string subtext, int methodCount, dynamic list)
            {
                if (methodCount > 0)
                {
                    summaryContents.AppendLine($"\n{headerText} ({subtext}s):");
                    foreach (var method in list.GetRange(0, Math.Min(methodCount, _count)))
                    {
                        summaryContents.Append(string.Format("    {2,8} ({3,6:P} of base) : {0} - {1}", method.path, method.name, method.deltaMetric.ValueString,
                            method.deltaMetric.Value / method.baseMetric.Value));

                        if (method.baseCount == method.diffCount)
                        {
                            if (method.baseCount > 1)
                            {
                                summaryContents.Append($" ({method.baseCount} methods)");
                            }
                        }
                        else
                        {
                            summaryContents.Append($" ({method.baseCount} base, {method.diffCount} diff methods)");
                        }
                        summaryContents.AppendLine();
                    }
                }
            }

            DisplayMethodMetric("Top method regressions", unitName, methodRegressionCount, sortedMethodRegressions);
            DisplayMethodMetric("Top method improvements", unitName, methodImprovementCount, sortedMethodImprovements);
            DisplayMethodMetric("Top method regressions", "percentage", methodRegressionCount, sortedMethodRegressionsByPercentage);
            DisplayMethodMetric("Top method improvements", "percentage", methodImprovementCount, sortedMethodImprovementsByPercentage);

            if (!Get(_command.IsSubsetOfDiffs))
            {
                if (Get(_command.IsDiffsOnly))
                {
                    summaryContents.AppendLine($"\n{sortedMethodCount} total methods with {metricDisplayName} differences ({methodImprovementCount} improved, {methodRegressionCount} regressed).");
                }
                else
                {
                    summaryContents.AppendLine($"\n{sortedMethodCount} total methods with {metricDisplayName} differences ({methodImprovementCount} improved, {methodRegressionCount} regressed), {unchangedMethodCount} unchanged.");
                }
            }

            if (!Get(_command.SkipTextDiff))
            {
                // Show files with text diffs but no metric diffs.

                Dictionary<string, int> diffCounts = DiffInText(_diffPath, _basePath);

                // TODO: resolve diffs to particular methods in the files.
                var zeroDiffFilesWithDiffs = fileDeltaList.Where(x => diffCounts.ContainsKey(x.diffName) && (x.deltaMetrics.IsZero()))
                    .OrderByDescending(x => diffCounts[x.baseName]);

                int zeroDiffFilesWithDiffCount = zeroDiffFilesWithDiffs.Count();
                if (zeroDiffFilesWithDiffCount > 0)
                {
                    summaryContents.AppendLine($"\n{zeroDiffFilesWithDiffCount} files had text diffs but no metric diffs.");
                    foreach (var zerofile in zeroDiffFilesWithDiffs.Take(_count))
                    {
                        summaryContents.AppendLine($"{zerofile.baseName} had {diffCounts[zerofile.baseName]} diffs");
                    }
                }
            }

            if (retainOnlyTopFiles)
            {
                if ((fileDeltaList.Count() > 0))
                {
                    void DeleteFile(string path)
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception) { }
                    }

                    int filesDeleted = 0;
                    foreach (var fileToDelete in fileDeltaList)
                    {
                        if (!fileToDelete.RetainFile)
                        {
                            DeleteFile(Path.Combine(_basePath, fileToDelete.baseName));
                            DeleteFile(Path.Combine(_diffPath, fileToDelete.diffName));
                            filesDeleted += 2;
                        }
                    }

                    Console.WriteLine($"Deleted {filesDeleted} .dasm files.");

                }
            }

            return (Math.Abs(totalDeltaMetric.Value) == 0 ? 0 : -1, summaryContents.ToString());
        }

        public static void WarnFiles(List<FileInfo> diffList, List<FileInfo> baseList)
        {
            if (baseList.Count == 1 && baseList[0].isExplicitOnlyFile &&
                diffList.Count == 1 && diffList[0].isExplicitOnlyFile)
            {
                return;
            }

            FileInfoComparer fileInfoComparer = new FileInfoComparer();
            var onlyInBaseList = baseList.Except(diffList, fileInfoComparer);
            var onlyInDiffList = diffList.Except(baseList, fileInfoComparer);

            //  Go through the files and flag anything not in both lists.

            var onlyInBaseCount = onlyInBaseList.Count();
            if (onlyInBaseCount > 0)
            {
                Console.WriteLine("Warning: {0} files in base but not in diff.", onlyInBaseCount);
                Console.WriteLine("\nOnly in base files:");
                foreach (var file in onlyInBaseList)
                {
                    Console.WriteLine(file.name);
                }
            }

            var onlyInDiffCount = onlyInDiffList.Count();
            if (onlyInDiffCount > 0)
            {
                Console.WriteLine("Warning: {0} files in diff but not in base.", onlyInDiffCount);
                Console.WriteLine("\nOnly in diff files:");
                foreach (var file in onlyInDiffList)
                {
                    Console.WriteLine(file.name);
                }
            }
        }

        public static void WarnMethods(IEnumerable<FileDelta> compareList)
        {
            foreach (var delta in compareList)
            {
                var onlyInBaseCount = delta.methodsOnlyInBase.Count();
                var onlyInDiffCount = delta.methodsOnlyInDiff.Count();

                if ((onlyInBaseCount > 0) || (onlyInDiffCount > 0))
                {
                    Console.WriteLine("Mismatched methods in {0}", delta.baseName);
                    if (onlyInBaseCount > 0)
                    {
                        Console.WriteLine("Base:");
                        foreach (var method in delta.methodsOnlyInBase)
                        {
                            Console.WriteLine("    {0}", method.name);
                        }
                    }
                    if (onlyInDiffCount > 0)
                    {
                        Console.WriteLine("Diff:");
                        foreach (var method in delta.methodsOnlyInDiff)
                        {
                            Console.WriteLine("    {0}", method.name);
                        }
                    }
                }
            }
        }

        public static StringBuilder GenerateMarkdown(string summarizedReport)
        {
            StringBuilder markdownBuilder = new StringBuilder();

            markdownBuilder.AppendLine("```");
            markdownBuilder.AppendLine(summarizedReport.Replace(DETAILS_MARKER, DETAILS_TEXT));
            markdownBuilder.AppendLine("```");
            markdownBuilder.AppendLine("");
            markdownBuilder.AppendLine("</details>");

            return markdownBuilder;
        }

        public static StringBuilder GenerateJson(IEnumerable<FileDelta> compareList)
        {
            StringBuilder fileContents = new StringBuilder();

            fileContents.AppendLine(JsonSerializer.Serialize(compareList.Where(file => !file.deltaMetrics.IsZero()), new JsonSerializerOptions { WriteIndented = true }));

            return fileContents;
        }

        public static StringBuilder GenerateTSV(IEnumerable<FileDelta> compareList)
        {
            StringBuilder fileContents = new StringBuilder();

            fileContents.Append($"File\tMethod");
            foreach (Metric metric in MetricCollection.AllMetrics)
            {
                fileContents.Append($"\tBase {metric.Name}\tDiff {metric.Name}\tDelta {metric.Name}\tPercentage {metric.Name}");
            }
            fileContents.AppendLine();

            foreach (var file in compareList)
            {
                foreach (var method in file.methodDeltaList)
                {
                    // Method names often contain commas, so use tabs as field separators
                    fileContents.Append($"{file.baseName}\t{method.name}\t");

                    foreach (Metric metric in MetricCollection.AllMetrics)
                    {
                        // Metric Base Value
                        fileContents.Append($"{method.baseMetrics.GetMetric(metric.Name).Value}\t");

                        // Metric Diff Value
                        fileContents.Append($"{method.diffMetrics.GetMetric(metric.Name).Value}\t");

                        // Metric Delta Value
                        fileContents.Append($"{method.deltaMetrics.GetMetric(metric.Name).Value}\t");

                        // Metric Delta Percentage of Base
                        double deltaPercentage = 0.0;
                        if (method.baseMetrics.GetMetric(metric.Name).Value != 0)
                        {
                            deltaPercentage = method.deltaMetrics.GetMetric(metric.Name).Value / method.baseMetrics.GetMetric(metric.Name).Value;
                        }
                        fileContents.Append($"{deltaPercentage}\t");
                    }

                    fileContents.AppendLine();
                }
            }
            return fileContents;
        }

        // There are files with diffs. Build up a dictionary mapping base file name to net text diff count.
        // Use "git diff" to do the analysis for us, then parse that output.
        //
        // "git diff --diff-filter=M --no-index --exit-code --numstat -z" output shows added/deleted lines:
        //
        // With -diff-filter=M, "git diff" will only compare modified files i.e. the ones that exists in both base and diff
        // and ignore files that are present in one but not in other.
        //
        // With -z, the output uses field terminators of NULs (\0).
        //   added\tremoved\t\0base-path-1\0diff-path-1\0added\tremoved\t....
        //
        // For example:
        // 6\t6\t\0d:\root\dasmset_8\base\Vector3Interop_ro.dasm\0d:\root\dasmset_8\diff\Vector3Interop_ro.dasm\0<next diff information>
        //
        public static Dictionary<string, int> DiffInText(string diffPath, string basePath)
        {
            // run get diff command to see if we have textual diffs.
            // (use git diff since it's already a dependency and cross platform)
            List<string> commandArgs = new List<string>();
            commandArgs.Add("diff");
            commandArgs.Add("--no-index");
            commandArgs.Add("--diff-filter=M");
            commandArgs.Add("--exit-code");
            commandArgs.Add("--numstat");
            commandArgs.Add("-z");
            commandArgs.Add(basePath);
            commandArgs.Add(diffPath);

            ProcessResult result = Utility.ExecuteProcess("git", commandArgs, true);
            Dictionary<string, int> fileToTextDiffCount = new Dictionary<string, int>(); ;

            if (result.ExitCode != 0)
            {
                // There are files with diffs. Build up a dictionary mapping base file name to net text diff count.

                var rawLines = result.StdOut.Split(new[] { "\0", Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                if (rawLines.Length % 3 != 0)
                {
                    Console.WriteLine($"Error parsing output: {result.StdOut}");
                    return fileToTextDiffCount;
                }

                for (int i = 0; i < rawLines.Length; i += 3)
                {
                    string rawStats = rawLines[i];
                    string rawBasePath = rawLines[i + 1];
                    string rawDiffPath = rawLines[i + 2];

                    string[] fields = rawStats.Split(new char[] { ' ', '\t', '"' }, StringSplitOptions.RemoveEmptyEntries);

                    string parsedFullDiffFilePath = Path.GetFullPath(rawDiffPath);
                    string parsedFullBaseFilePath = Path.GetFullPath(rawBasePath);

                    if (!File.Exists(parsedFullBaseFilePath))
                    {
                        Console.WriteLine($"Error parsing path '{rawBasePath}'. `{parsedFullBaseFilePath}` doesn't exist.");
                        continue;
                    }


                    if (!File.Exists(parsedFullDiffFilePath))
                    {
                        Console.WriteLine($"Error parsing path '{rawDiffPath}'. `{parsedFullDiffFilePath}` doesn't exist.");
                        continue;
                    }

                    // Sometimes .dasm is parsed as binary and we don't get numbers, just dashes
                    int addCount = 0;
                    int delCount = 0;
                    Int32.TryParse(fields[0], out addCount);
                    Int32.TryParse(fields[1], out delCount);
                    fileToTextDiffCount[parsedFullBaseFilePath] = addCount + delCount;
                }

                Console.WriteLine($"Found {fileToTextDiffCount.Count()} files with textual diffs.");
            }

            return fileToTextDiffCount;
        }

        private T Get<T>(Option<T> option) => _command.Result.GetValue(option);

        private static int Main(string[] args)
        {
            var command = new JitAnalyzeRootCommand(args).UseVersion();
            return command.Parse(args).Invoke();
        }

        public int Run()
        {
            int retCode = 0;
            try
            {
                // Extract method info from base and diff directory or file.
                bool recursive = Get(_command.Recursive);
                string extension = Get(_command.FileExtension);
                var baseList = ExtractFileInfo(_basePath, _filter, extension, recursive);
                var diffList = ExtractFileInfo(_diffPath, _filter, extension, recursive);

                // Compare the method info for each file and generate a list of
                // non-zero deltas.  The lists that include files in one but not
                // the other are used as the comparator function only compares where it 
                // has both sides.

                IEnumerable<FileDelta> compareList = null;
                StringBuilder tsvContents = new StringBuilder();
                StringBuilder jsonContents = new StringBuilder();
                StringBuilder markdownContents = new StringBuilder();

                string json = Get(_command.Json);
                string tsv = Get(_command.Tsv);
                string md = Get(_command.MD);
                string[] metricNames = Get(_command.Metrics).ToArray();
                FileDelta[][] comparisons = Comparator(baseList, diffList, metricNames);
                for (int metricIndex = 0; metricIndex < metricNames.Length; metricIndex++)
                {
                    string metricName = metricNames[metricIndex];
                    compareList = comparisons.Select(files => files[metricIndex]).ToArray();

                    if (tsv != null)
                    {
                        tsvContents.Append(GenerateTSV(compareList));
                    }

                    if (json != null)
                    {
                        jsonContents.Append(GenerateJson(compareList));
                    }

                    var summarizedReport = Summarize(compareList, metricName);

                    if (md != null)
                    {
                        markdownContents.Append(GenerateMarkdown(summarizedReport.Item2));
                        markdownContents.AppendLine();
                        markdownContents.AppendLine(METRIC_SEP);
                        markdownContents.AppendLine();
                    }

                    retCode += summarizedReport.Item1;
                    Console.WriteLine(summarizedReport.Item2.Replace(DETAILS_MARKER, string.Empty));
                    Console.WriteLine(METRIC_SEP);
                }


                // Generate warning lists if requested.
                if (Get(_command.Warn))
                {
                    WarnFiles(diffList, baseList);
                    WarnMethods(compareList);
                }

                if (tsvContents.Length > 0)
                {
                    File.WriteAllText(tsv, tsvContents.ToString());
                }

                if (jsonContents.Length > 0)
                {
                    File.WriteAllText(json, jsonContents.ToString());
                }

                if (markdownContents.Length > 0)
                {
                    File.WriteAllText(md, markdownContents.ToString());
                }

                return retCode;
            }
            catch (System.IO.DirectoryNotFoundException e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                return 0;
            }
            catch (System.IO.FileNotFoundException e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                return 0;
            }
        }
    }
}
