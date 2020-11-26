// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Text;
using System.Runtime.CompilerServices;

namespace ManagedCodeGen
{
    // Allow Linq to be able to sum up MetricCollections
    public static class jitanalyzeExtensions
    {
        public static jitanalyze.MetricCollection Sum(this IEnumerable<jitanalyze.MetricCollection> source)
        {
            jitanalyze.MetricCollection result = new jitanalyze.MetricCollection();

            foreach (jitanalyze.MetricCollection s in source)
            {
                result.Add(s);
            }

            return result;
        }

        public static jitanalyze.MetricCollection Sum<T>(this IEnumerable<T> source, Func<T, jitanalyze.MetricCollection> selector)
        {
            return source.Select(x => selector(x)).Sum();
        }
    }

    public class jitanalyze
    {
        public class Config
        {
            private ArgumentSyntax _syntaxResult;
            private string _basePath = null;
            private string _diffPath = null;
            private bool _recursive = false;
            private string _fileExtension = ".dasm";
            private bool _full = false;
            private bool _warn = false;
            private int _count = 20;
            private string _json;
            private string _tsv;
            private bool _noreconcile = false;
            private string _note;
            private string _filter;
            private string _metric;

            public Config(string[] args)
            {
                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    syntax.DefineOption("b|base", ref _basePath, "Base file or directory.");
                    syntax.DefineOption("d|diff", ref _diffPath, "Diff file or directory.");
                    syntax.DefineOption("r|recursive", ref _recursive, "Search directories recursively.");
                    syntax.DefineOption("ext|fileExtension", ref _fileExtension, "File extension to look for.");
                    syntax.DefineOption("c|count", ref _count,
                        "Count of files and methods (at most) to output in the summary."
                      + " (count) improvements and (count) regressions of each will be included."
                      + " (default 20)");
                    syntax.DefineOption("w|warn", ref _warn,
                        "Generate warning output for files/methods that only "
                      + "exists in one dataset or the other (only in base or only in diff).");
                    syntax.DefineOption("m|metric", ref _metric, "Metric to use for diff computations. Available metrics: CodeSize(default), PerfScore, PrologSize, InstrCount, DebugClauseCount, DebugVarCount");
                    syntax.DefineOption("note", ref _note,
                        "Descriptive note to add to summary output");
                    syntax.DefineOption("noreconcile", ref _noreconcile,
                        "Do not reconcile unique methods in base/diff");
                    syntax.DefineOption("json", ref _json,
                        "Dump analysis data to specified file in JSON format.");
                    syntax.DefineOption("tsv", ref _tsv,
                        "Dump analysis data to specified file in tab-separated format.");
                    syntax.DefineOption("filter", ref _filter,
                        "Only consider assembly files whose names match the filter");
                });

                // Run validation code on parsed input to ensure we have a sensible scenario.
                validate();
            }

            private void validate()
            {
                if (_basePath == null)
                {
                    _syntaxResult.ReportError("Base path (--base) is required.");
                }

                if (_diffPath == null)
                {
                    _syntaxResult.ReportError("Diff path (--diff) is required.");
                }

                if (_metric == null)
                {
                    _metric = "CodeSize";
                }

                if (!MetricCollection.ValidateMetric(_metric))
                {
                    _syntaxResult.ReportError($"Unknown metric '{_metric}'. Available metrics: {MetricCollection.ListMetrics()}");
                }
            }

            public string BasePath { get { return _basePath; } }
            public string DiffPath { get { return _diffPath; } }
            public bool Recursive { get { return _recursive; } }
            public string FileExtension { get { return _fileExtension; } }
            public bool Full { get { return _full; } }
            public bool Warn { get { return _warn; } }
            public int Count { get { return _count; } }
            public string TSVFileName { get { return _tsv; } }
            public string JsonFileName { get { return _json; } }
            public bool DoGenerateJson { get { return _json != null; } }
            public bool DoGenerateTSV { get { return _tsv != null; } }
            public bool Reconcile { get { return !_noreconcile; } }
            public string Note { get { return _note; } }

            public string Filter {  get { return _filter; } }

            public string Metric {  get { return _metric; } }
        }

        public class FileInfo
        {
            public string path;
            public IEnumerable<MethodInfo> methodList;
        }

        // Custom comparer for the FileInfo class
        private class FileInfoComparer : IEqualityComparer<FileInfo>
        {
            public bool Equals(FileInfo x, FileInfo y)
            {
                if (Object.ReferenceEquals(x, y)) return true;
                if (Object.ReferenceEquals(x, null) || Object.ReferenceEquals(y, null))
                    return false;
                return x.path == y.path;
            }

            // If Equals() returns true for a pair of objects 
            // then GetHashCode() must return the same value for these objects.

            public int GetHashCode(FileInfo fi)
            {
                if (Object.ReferenceEquals(fi, null)) return 0;
                return fi.path == null ? 0 : fi.path.GetHashCode();
            }
        }

        public abstract class Metric
        {
            public virtual string Name { get; }
            public virtual string DisplayName { get; }
            public virtual string Unit { get; }
            public virtual bool LowerIsBetter { get; }
            public abstract Metric Clone();
            public abstract string ValueString { get; }
            public double Value { get; set; }

            public void Add(Metric m)
            {
                Value += m.Value;
            }

            public void Sub(Metric m)
            {
                Value -= m.Value;
            }

            public void SetValueFrom(Metric m)
            {
                Value = m.Value;
            }
        }

        public class CodeSizeMetric : Metric
        {
            public override string Name => "CodeSize";
            public override string DisplayName => "Code Size";
            public override string Unit => "byte";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new CodeSizeMetric();
            public override string ValueString => $"{Value}";
        }

        public class PrologSizeMetric : Metric
        {
            public override string Name => "PrologSize";
            public override string DisplayName => "Prolog Size";
            public override string Unit => "byte";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new PrologSizeMetric();
            public override string ValueString => $"{Value}";
        }

        public class PerfScoreMetric : Metric
        {
            public override string Name => "PerfScore";
            public override string DisplayName => "Perf Score";
            public override string Unit => "PerfScoreUnit";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new PerfScoreMetric();
            public override string ValueString => $"{Value:F2}";
        }

        public class InstrCountMetric : Metric
        {
            public override string Name => "InstrCount";
            public override string DisplayName => "Instruction Count";
            public override string Unit => "Instruction";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new InstrCountMetric();
            public override string ValueString => $"{Value}";
        }

        public class DebugClauseMetric : Metric
        {
            public override string Name => "DebugClauseCount";
            public override string DisplayName => "Debug Clause Count";
            public override string Unit => "Clause";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new DebugClauseMetric();
            public override string ValueString => $"{Value}";
        }

        public class DebugVarMetric : Metric
        {
            public override string Name => "DebugVarCount";
            public override string DisplayName => "Debug Variable Count";
            public override string Unit => "Variable";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new DebugVarMetric();
            public override string ValueString => $"{Value}";
        }

        public class MetricCollection
        {
            private static Dictionary<string, int> s_metricNameToIndex;
            private static Metric[] s_metrics;

            static MetricCollection()
            {
                s_metrics = new Metric[] { new CodeSizeMetric(), new PrologSizeMetric(), new PerfScoreMetric(), new InstrCountMetric(), new DebugClauseMetric(), new DebugVarMetric() };
                s_metricNameToIndex = new Dictionary<string, int>(s_metrics.Length);

                for (int i = 0; i < s_metrics.Length; i++)
                {
                    Metric m = s_metrics[i];
                    s_metricNameToIndex[m.Name] = i;
                }
            }

            [JsonProperty()]
            private Metric[] metrics;

            public MetricCollection()
            {
                metrics = new Metric[s_metrics.Length];
                for (int i = 0; i < s_metrics.Length; i++)
                {
                    metrics[i] = s_metrics[i].Clone();
                }
            }

            public MetricCollection(MetricCollection other) : this()
            {
                this.SetValueFrom(other);
            }

            public static IEnumerable<Metric> AllMetrics => s_metrics;

            public Metric GetMetric(string metricName)
            {
                int index;
                if (s_metricNameToIndex.TryGetValue(metricName, out index))
                {
                    return metrics[index];
                }
                return null;
            }

            public static bool ValidateMetric(string name)
            {
                return s_metricNameToIndex.TryGetValue(name, out _);
            }

            public static string DisplayName(string metricName)
            {
                int index;
                if (s_metricNameToIndex.TryGetValue(metricName, out index))
                {
                    return s_metrics[index].DisplayName;
                }
                return "Unknown metric";
            }

            public static string ListMetrics()
            {
                StringBuilder sb = new StringBuilder();
                bool isFirst = true;
                foreach (string s in s_metricNameToIndex.Keys)
                {
                    if (!isFirst) sb.Append(", ");
                    sb.Append(s);
                    isFirst = false;
                }
                return sb.ToString();
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                bool isFirst = true;
                foreach (Metric m in metrics)
                {
                    if (!isFirst) sb.Append(", ");
                    sb.Append($"{m.Name} {m.Unit} {m.ValueString}");
                    isFirst = false;
                }
                return sb.ToString();
            }

            public void Add(MetricCollection other)
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    metrics[i].Add(other.metrics[i]);
                }
            }

            public void Add(string metricName, double value)
            {
                Metric m = GetMetric(metricName);
                m.Value += value;
            }

            public void Sub(MetricCollection other)
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    metrics[i].Sub(other.metrics[i]);
                }
            }

            public void SetValueFrom(MetricCollection other)
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    metrics[i].SetValueFrom(other.metrics[i]);
                }
            }

            public bool IsZero()
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    if (metrics[i].Value != 0) return false;
                }
                return true;
            }
        }

        public class MethodInfo
        {
            private MetricCollection metrics;
            public MetricCollection Metrics => metrics;
            public string name;
            public int functionCount;
            public IEnumerable<int> functionOffsets;

            public MethodInfo()
            {
                metrics = new MetricCollection();
            }

            public override string ToString()
            {
                return String.Format(@"name {0}, {1}, function count {2}, offsets {3}",
                    name, metrics, functionCount,
                    String.Join(", ", functionOffsets.ToArray()));
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
            public string basePath;
            public string diffPath;

            public MetricCollection baseMetrics;
            public MetricCollection diffMetrics;
            public MetricCollection deltaMetrics;
            public MetricCollection reconciledBaseMetrics;
            public MetricCollection reconciledDiffMetrics;

            public int reconciledCountBase;
            public int reconciledCountDiff;
            public int methodsInBoth;
            public IEnumerable<MethodInfo> methodsOnlyInBase;
            public IEnumerable<MethodInfo> methodsOnlyInDiff;
            public IEnumerable<MethodDelta> methodDeltaList;

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
            public MetricCollection deltaMetrics
            {
                get
                {
                    MetricCollection result = new MetricCollection(diffMetrics);
                    result.Sub(baseMetrics);
                    return result;
                }
            }
            public IEnumerable<int> baseOffsets;
            public IEnumerable<int> diffOffsets;
        }

        public static IEnumerable<FileInfo> ExtractFileInfo(string path, string filter, string fileExtension, bool recursive)
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
                return Directory.EnumerateFiles(fullRootPath, searchPattern, searchOption)
                         .Select(p => new FileInfo
                         {
                             path = p.Substring(fullRootPath.Length).TrimStart(Path.DirectorySeparatorChar),
                             methodList = ExtractMethodInfo(p)
                         }).ToList();
            }
            else
            {
                // In the single file case just create a list with a single
                // to satisfy the interface.
                return new List<FileInfo>
                { new FileInfo
                    {
                        path = Path.GetFileName(path),
                        methodList = ExtractMethodInfo(path)
                    }
                };
            }
        }

        // Extract lines of the passed in file and create a method info object
        // for each method descript line containing metrics like total bytes, prolog bytes, 
        // and offset in the file.
        //
        // This is the method that knows how to parse jit output and recover the metrics.
        public static IEnumerable<MethodInfo> ExtractMethodInfo(string filePath)
        {
            Regex namePattern = new Regex(@"for method (.*)$");
            Regex codeAndPrologSizePattern = new Regex(@"code ([0-9]{1,}), prolog size ([0-9]{1,})");
            // use new regex for perf score so we can still parse older files that did not have it.
            Regex perfScorePattern = new Regex(@"(PerfScore|perf score) (\d+(\.\d+)?)");
            Regex instrCountPattern = new Regex(@"instruction count ([0-9]{1,})");
            Regex debugInfoPattern = new Regex(@"Variable debug info: ([0-9]{1,}) live range\(s\), ([0-9]{1,}) var\(s\)");

            var result =
             File.ReadLines(filePath)
                             .Select((x, i) => new { line = x, index = i })
                             .Where(l => l.line.StartsWith(@"; Total bytes of code", StringComparison.Ordinal)
                                        || l.line.StartsWith(@"; Assembly listing for method", StringComparison.Ordinal)
                                        || l.line.StartsWith(@"; Variable debug info:", StringComparison.Ordinal))
                             .Select((x) =>
                             {
                                 var nameMatch = namePattern.Match(x.line);
                                 var codeAndPrologSizeMatch = codeAndPrologSizePattern.Match(x.line);
                                 var perfScoreMatch = perfScorePattern.Match(x.line);
                                 var instrCountMatch = instrCountPattern.Match(x.line);
                                 var debugInfoMatch = debugInfoPattern.Match(x.line);
                                 return new
                                 {
                                     name = nameMatch.Groups[1].Value,
                                     // Use matched data or default to 0
                                     totalBytes = codeAndPrologSizeMatch.Success ?
                                        Int32.Parse(codeAndPrologSizeMatch.Groups[1].Value) : 0,
                                     prologBytes = codeAndPrologSizeMatch.Success ?
                                        Int32.Parse(codeAndPrologSizeMatch.Groups[2].Value) : 0,
                                     perfScore = perfScoreMatch.Success ?
                                        Double.Parse(perfScoreMatch.Groups[2].Value) : 0,
                                     instrCount = instrCountMatch.Success ?
                                        Int32.Parse(instrCountMatch.Groups[1].Value) : 0,
                                     debugClauseCount = debugInfoMatch.Success ?
                                        Int32.Parse(debugInfoMatch.Groups[1].Value) : 0,
                                     debugVarCount = debugInfoMatch.Success ?
                                        Int32.Parse(debugInfoMatch.Groups[2].Value) : 0,
                                     // Use function index only from non-data lines (the name line)
                                     functionOffset = codeAndPrologSizeMatch.Success ?
                                        0 : x.index
                                 };
                             })
                             .GroupBy(x => x.name)
                             .Select(x =>
                             {
                                 MethodInfo mi = new MethodInfo
                                 {
                                     name = x.Key,
                                     functionCount = x.Select(z => z).Where(z => z.totalBytes == 0).Count(),
                                     // for all non-zero function offsets create list.
                                     functionOffsets = x.Select(z => z)
                                                    .Where(z => z.functionOffset != 0)
                                                    .Select(z => z.functionOffset).ToList()
                                 };

                                 mi.Metrics.Add("CodeSize", x.Sum(z => z.totalBytes));
                                 mi.Metrics.Add("PrologSize", x.Sum(z => z.prologBytes));
                                 mi.Metrics.Add("PerfScore", x.Sum(z => z.perfScore));
                                 mi.Metrics.Add("InstrCount", x.Sum(z => z.instrCount));
                                 mi.Metrics.Add("DebugClauseCount", x.Sum(z => z.debugClauseCount));
                                 mi.Metrics.Add("DebugVarCount", x.Sum(z => z.debugVarCount));

                                 return mi;
                             }).ToList();

            return result;
        }

        // Compare base and diff file lists and produce a sorted list of method
        // deltas by file.  Delta is computed diffBytes - baseBytes so positive
        // numbers are regressions. (lower is better)
        //
        // Todo: handle metrics where "higher is better"
        public static IEnumerable<FileDelta> Comparator(IEnumerable<FileInfo> baseInfo,
            IEnumerable<FileInfo> diffInfo, Config config)
        {
            MethodInfoComparer methodInfoComparer = new MethodInfoComparer();
            return baseInfo.Join(diffInfo, b => b.path, d => d.path, (b, d) =>
            {
                var jointList = b.methodList.Join(d.methodList,
                        x => x.name, y => y.name, (x, y) => new MethodDelta
                        {
                            name = x.name,
                            baseMetrics = new MetricCollection(x.Metrics),
                            diffMetrics = new MetricCollection(y.Metrics),
                            baseOffsets = x.functionOffsets,
                            diffOffsets = y.functionOffsets
                        })
                        .OrderByDescending(r => r.deltaMetrics.GetMetric(config.Metric).Value);

                FileDelta f = new FileDelta
                {
                    basePath = b.path,
                    diffPath = d.path,
                    baseMetrics = jointList.Sum(x => x.baseMetrics),
                    diffMetrics = jointList.Sum(x => x.diffMetrics),
                    deltaMetrics = jointList.Sum(x => x.deltaMetrics),
                    methodsInBoth = jointList.Count(),
                    methodsOnlyInBase = b.methodList.Except(d.methodList, methodInfoComparer),
                    methodsOnlyInDiff = d.methodList.Except(b.methodList, methodInfoComparer),
                    methodDeltaList = jointList.Where(x => x.deltaMetrics.GetMetric(config.Metric).Value != 0)
                };

                if (config.Reconcile)
                {
                    f.Reconcile();
                }

                return f;
            }).ToList();
        }

        // Summarize differences across all the files.
        // Output:
        //     Total bytes differences
        //     Top files by difference size
        //     Top diffs by size across all files
        //     Top diffs by percentage size across all files
        //
        public static int Summarize(IEnumerable<FileDelta> fileDeltaList, Config config, Dictionary<string, int> diffCounts)
        {
            var totalDeltaMetrics = fileDeltaList.Sum(x => x.deltaMetrics);
            var totalBaseMetrics = fileDeltaList.Sum(x => x.baseMetrics);
            var totalDiffMetrics = fileDeltaList.Sum(x => x.diffMetrics);

            if (config.Note != null)
            {
                Console.WriteLine($"\n{config.Note}");
            }

            Metric totalBaseMetric = totalBaseMetrics.GetMetric(config.Metric);
            Metric totalDiffMetric = totalDiffMetrics.GetMetric(config.Metric);
            Metric totalDeltaMetric = totalDeltaMetrics.GetMetric(config.Metric);
            string unitName = totalBaseMetrics.GetMetric(config.Metric).Unit;
            string metricName = totalBaseMetrics.GetMetric(config.Metric).DisplayName;

            Console.Write($"\nSummary of {metricName} diffs:");
            if (config.Filter != null)
            {
                Console.Write($" (using filter '{config.Filter}')");
            }
            Console.WriteLine("\n({0} is better)\n", totalBaseMetric.LowerIsBetter ? "Lower" : "Higher");

            if (totalBaseMetric.Value != 0)
            {
                Console.WriteLine("Total {0}s of base: {1}", unitName, totalBaseMetric.Value);
                Console.WriteLine("Total {0}s of diff: {1}", unitName, totalDiffMetric.Value);
                Console.WriteLine("Total {0}s of delta: {1} ({2:P} of base)", unitName, totalDeltaMetric.ValueString, totalDeltaMetric.Value / totalBaseMetric.Value);
            }
            else 
            {
                Console.WriteLine("Warning: the base metric is 0, the diff metric is {0}, have you used a release version?", totalDiffMetrics.GetMetric(config.Metric).ValueString);
            }

            if (totalDeltaMetric.Value != 0)
            {
                Console.WriteLine("    diff is {0}", totalDeltaMetric.LowerIsBetter == (totalDeltaMetric.Value < 0) ? "an improvement." : "a regression.");
            }

            if (config.Reconcile)
            {
                // See if base or diff had any unique methods
                var uniqueToBase = fileDeltaList.Sum(x => x.reconciledCountBase);
                var uniqueToDiff = fileDeltaList.Sum(x => x.reconciledCountDiff);

                // Only dump reconciliation stats if there was at least one unique
                if (uniqueToBase + uniqueToDiff > 0)
                {
                    var reconciledBaseMetrics = fileDeltaList.Sum(x => x.reconciledBaseMetrics);
                    var reconciledDiffMetrics = fileDeltaList.Sum(x => x.reconciledDiffMetrics);
                    Metric reconciledBaseMetric = reconciledBaseMetrics.GetMetric(config.Metric);
                    Metric reconciledDiffMetric = reconciledDiffMetrics.GetMetric(config.Metric);

                    Console.WriteLine("\nTotal {0} diff includes {1} {0}s from reconciling methods", unitName,
                        reconciledDiffMetric.Value - reconciledBaseMetric.Value);
                    Console.WriteLine("\tBase had {0,4} unique methods, {1,8} unique {2}s", uniqueToBase, reconciledBaseMetric.ValueString, unitName);
                    Console.WriteLine("\tDiff had {0,4} unique methods, {1,8} unique {2}s", uniqueToDiff, reconciledDiffMetric.ValueString, unitName);
                }
            }

            // Todo: handle higher is better metrics
            int requestedCount = config.Count;
            var sortedFileImprovements = fileDeltaList
                                            .Where(x => x.deltaMetrics.GetMetric(config.Metric).Value < 0)
                                            .OrderBy(d => d.deltaMetrics.GetMetric(config.Metric).Value).ToList();
            var sortedFileRegressions = fileDeltaList
                                            .Where(x => x.deltaMetrics.GetMetric(config.Metric).Value > 0)
                                            .OrderByDescending(d => d.deltaMetrics.GetMetric(config.Metric).Value).ToList();
            int fileImprovementCount = sortedFileImprovements.Count();
            int fileRegressionCount = sortedFileRegressions.Count();
            int sortedFileCount = fileImprovementCount + fileRegressionCount;
            int unchangedFileCount = fileDeltaList.Count() - sortedFileCount;

            void DisplayFileMetric(string headerText, int metricCount, IEnumerable<FileDelta> list)
            {
                if (metricCount > 0)
                {
                    Console.WriteLine($"\n{headerText} ({unitName}s):");
                    foreach (var fileDelta in list.Take(Math.Min(metricCount, requestedCount)))
                    {
                        Console.WriteLine("    {1,8} : {0} ({2:P} of base)", fileDelta.basePath,
                            fileDelta.deltaMetrics.GetMetric(config.Metric).ValueString, 
                            fileDelta.deltaMetrics.GetMetric(config.Metric).Value / fileDelta.baseMetrics.GetMetric(config.Metric).Value);
                    }
                }
            }

            DisplayFileMetric("Top file regressions", fileRegressionCount, sortedFileRegressions);
            DisplayFileMetric("Top file improvements", fileImprovementCount, sortedFileImprovements);

            Console.WriteLine("\n{0} total files with {1} differences ({2} improved, {3} regressed), {4} unchanged.",
                sortedFileCount, metricName, fileImprovementCount, fileRegressionCount, unchangedFileCount);

            var methodDeltaList = fileDeltaList
                                        .SelectMany(fd => fd.methodDeltaList, (fd, md) => new
                                        {
                                            path = fd.basePath,
                                            name = md.name,
                                            deltaMetric = md.deltaMetrics.GetMetric(config.Metric),
                                            baseMetric = md.baseMetrics.GetMetric(config.Metric),
                                            diffMetric = md.diffMetrics.GetMetric(config.Metric),
                                            baseCount = md.baseOffsets == null ? 0 : md.baseOffsets.Count(),
                                            diffCount = md.diffOffsets == null ? 0 : md.diffOffsets.Count()
                                        }).ToList();
            var sortedMethodImprovements = methodDeltaList
                                            .Where(x => x.deltaMetric.Value< 0)
                                            .OrderBy(d => d.deltaMetric.Value).ToList();
            var sortedMethodRegressions = methodDeltaList
                                            .Where(x => x.deltaMetric.Value > 0)
                                            .OrderByDescending(d => d.deltaMetric.Value).ToList();
            int methodImprovementCount = sortedMethodImprovements.Count();
            int methodRegressionCount = sortedMethodRegressions.Count();
            int sortedMethodCount = methodImprovementCount + methodRegressionCount;
            int unchangedMethodCount = fileDeltaList.Sum(x => x.methodsInBoth) - sortedMethodCount;

            var sortedMethodImprovementsByPercentage = methodDeltaList
                                            .Where(x => x.deltaMetric.Value < 0)
                                            .OrderBy(d => d.deltaMetric.Value / d.baseMetric.Value).ToList();
            var sortedMethodRegressionsByPercentage = methodDeltaList
                                            .Where(x => x.deltaMetric.Value > 0)
                                            .OrderByDescending(d => d.deltaMetric.Value / d.baseMetric.Value).ToList();

            void DisplayMethodMetric(string headerText, string subtext, int methodCount, dynamic list)
            {
                if (methodCount > 0)
                {
                    Console.WriteLine($"\n{headerText} ({subtext}s):");
                    foreach (var method in list.GetRange(0, Math.Min(methodCount, requestedCount)))
                    {
                        Console.Write("    {2,8} ({3,6:P} of base) : {0} - {1}", method.path, method.name, method.deltaMetric.ValueString,
                            method.deltaMetric.Value / method.baseMetric.Value);

                        if (method.baseCount == method.diffCount)
                        {
                            if (method.baseCount > 1)
                            {
                                Console.Write(" ({0} methods)", method.baseCount);
                            }
                        }
                        else
                        {
                            Console.Write(" ({0} base, {1} diff methods)", method.baseCount, method.diffCount);
                        }
                        Console.WriteLine();
                    }
                }
            }

            DisplayMethodMetric("Top method regressions", unitName, methodRegressionCount, sortedMethodRegressions);
            DisplayMethodMetric("Top method improvements", unitName, methodImprovementCount, sortedMethodImprovements);
            DisplayMethodMetric("Top method regressions", "percentage", methodRegressionCount, sortedMethodRegressionsByPercentage);
            DisplayMethodMetric("Top method improvements", "percentage", methodImprovementCount, sortedMethodImprovementsByPercentage);

            Console.WriteLine("\n{0} total methods with {1} differences ({2} improved, {3} regressed), {4} unchanged.",
                sortedMethodCount, metricName, methodImprovementCount, methodRegressionCount, unchangedMethodCount);

            // Show files with text diffs but no metric diffs.
            // TODO: resolve diffs to particular methods in the files.
            var zeroDiffFilesWithDiffs = fileDeltaList.Where(x => diffCounts.ContainsKey(x.diffPath) && (x.deltaMetrics.IsZero()))
                .OrderByDescending(x => diffCounts[x.basePath]);

            int zeroDiffFilesWithDiffCount = zeroDiffFilesWithDiffs.Count();
            if (zeroDiffFilesWithDiffCount > 0)
            {
                Console.WriteLine("\n{0} files had text diffs but no metric diffs.", zeroDiffFilesWithDiffCount);
                foreach (var zerofile in zeroDiffFilesWithDiffs.Take(config.Count))
                {
                    Console.WriteLine($"{zerofile.basePath} had {diffCounts[zerofile.basePath]} diffs");
                }
            }

            return Math.Abs(totalDeltaMetric.Value) == 0 ? 0 : -1;
        }

        public static void WarnFiles(IEnumerable<FileInfo> diffList, IEnumerable<FileInfo> baseList)
        {
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
                    Console.WriteLine(file.path);
                }
            }

            var onlyInDiffCount = onlyInDiffList.Count();
            if (onlyInDiffCount > 0)
            {
                Console.WriteLine("Warning: {0} files in diff but not in base.", onlyInDiffCount);
                Console.WriteLine("\nOnly in diff files:");
                foreach (var file in onlyInDiffList)
                {
                    Console.WriteLine(file.path);
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
                    Console.WriteLine("Mismatched methods in {0}", delta.basePath);
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

        public static void GenerateJson(IEnumerable<FileDelta> compareList, string path)
        {
            using (var outputStream = System.IO.File.Create(path))
            {
                using (var outputStreamWriter = new StreamWriter(outputStream))
                {
                    // Serialize file delta to output file.
                    outputStreamWriter.Write(JsonConvert.SerializeObject(compareList.Where(file => !file.deltaMetrics.IsZero()), Formatting.Indented));
                }
            }
        }

        public static void GenerateTSV(IEnumerable<FileDelta> compareList, string path)
        {
            using (var outputStream = System.IO.File.Create(path))
            {
                using (var outputStreamWriter = new StreamWriter(outputStream))
                {
                    outputStreamWriter.Write($"File\tMethod");
                    foreach (Metric metric in MetricCollection.AllMetrics)
                    {
                        outputStreamWriter.Write($"\tBase {metric.Name}\tDiff {metric.Name}\tDelta {metric.Name}\tPercentage {metric.Name}");
                    }
                    outputStreamWriter.WriteLine();

                    foreach (var file in compareList)
                    {
                        foreach (var method in file.methodDeltaList)
                        {
                            // Method names often contain commas, so use tabs as field separators
                            outputStreamWriter.Write($"{file.basePath}\t{method.name}\t");

                            foreach (Metric metric in MetricCollection.AllMetrics)
                            {
                                // Metric Base Value
                                outputStreamWriter.Write($"{method.baseMetrics.GetMetric(metric.Name).Value}\t");

                                // Metric Diff Value
                                outputStreamWriter.Write($"{method.diffMetrics.GetMetric(metric.Name).Value}\t");

                                // Metric Delta Value
                                outputStreamWriter.Write($"{method.deltaMetrics.GetMetric(metric.Name).Value}\t");

                                // Metric Delta Percentage of Base
                                double deltaPercentage = 0.0;
                                if (method.baseMetrics.GetMetric(metric.Name).Value != 0)
                                {
                                    deltaPercentage = method.deltaMetrics.GetMetric(metric.Name).Value / method.baseMetrics.GetMetric(metric.Name).Value;
                                }
                                outputStreamWriter.Write($"{deltaPercentage}\t");
                            }

                            outputStreamWriter.WriteLine();
                        }
                    }
                }
            }
        }

        // There are files with diffs. Build up a dictionary mapping base file name to net text diff count.
        // Use "git diff" to do the analysis for us, then parse that output.
        //
        // "git diff --no-index --exit-code --numstat" output shows added/deleted lines:
        //
        //   <added> <removed> <base-path> => <diff-path>
        //
        // For example:
        // 6       6       "dasmset_8/diff/Vector3Interop_ro/Vector3Interop_ro.dasm" => "dasmset_8/base/Vector3Interop_ro/Vector3Interop_ro.dasm"
        //
        // Note, however, that it can also use a smaller output format, for example:
        //
        // 6       6       dasmset_8/{diff => base}/Vector3Interop_ro/Vector3Interop_ro.dasm
        public static Dictionary<string, int> DiffInText(string diffPath, string basePath)
        {
            // run get diff command to see if we have textual diffs.
            // (use git diff since it's already a dependency and cross platform)
            List<string> commandArgs = new List<string>();
            commandArgs.Add("diff");
            commandArgs.Add("--no-index");
            // only diff files that are present in both base and diff.
            commandArgs.Add("--diff-filter=M");
            commandArgs.Add("--exit-code");
            commandArgs.Add("--numstat");
            commandArgs.Add(diffPath);
            commandArgs.Add(basePath);

            ProcessResult result = Utility.ExecuteProcess("git", commandArgs, true, basePath);
            Dictionary<string, int> fileToTextDiffCount = new Dictionary<string, int>();

            if (result.ExitCode != 0)
            {
                // There are files with diffs. Build up a dictionary mapping base file name to net text diff count.
                //
                // diff --numstat output shows added/deleted lines
                //   added removed base-path => diff-path
                var rawLines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                string fullDiffPath = Path.GetFullPath(diffPath);

                foreach (var line in rawLines)
                {
                    string manipulatedLine = line;
                    string[] fields = null;

                    int numFields = 5;

                    // Example output:
                    // 32\t2\t/coreclr/bin/asm/asm/{diff => base}/101301.dasm
                    //
                    // This should be split into:
                    //
                    // 32\t2\t/coreclr/bin/asm/asm/base/101301.dasm\t/coreclr/bin/asm/asm/diff/101301.dasm
                    Regex gitMergedOutputRegex = new Regex(@"\{(\w+)\s=>\s(\w+)\}");
                    Regex whitespaceRegex = new Regex(@"(\w+)\s+(\w+)\s+(.*)");
                    if (gitMergedOutputRegex.Matches(line).Count > 0)
                    {
                        // Do the first split to remove the integers from the file path.
                        // Then reconstruct both the diff and the base paths.
                        var groups = whitespaceRegex.Match(line).Groups;
                        string[] modifiedLine = new string[] {
                            groups[whitespaceRegex.GroupNameFromNumber(1)].ToString(),
                            groups[whitespaceRegex.GroupNameFromNumber(2)].ToString(),
                            groups[whitespaceRegex.GroupNameFromNumber(3)].ToString()
                        };

                        string[] splitLine = gitMergedOutputRegex.Split(modifiedLine[2]);

                        // Split will output:
                        //
                        // 32\t2\t/coreclr/bin/asm/asm/
                        // {diffFolder}
                        // {baseFolder}
                        // 101301.dasm

                        // Create the base path from the second group
                        // {diff => base}
                        string manipulatedBasePath = String.Join(splitLine[2], new string[] {
                            splitLine[0],
                            splitLine[3]
                        });

                        // Create the diff path from the first 
                        // {diff => base}
                        string manipulatedDiffPath = String.Join(splitLine[1], new string[] {
                            splitLine[0],
                            splitLine[3]
                        });

                        fields = new string[4] {
                            modifiedLine[0],
                            modifiedLine[1],
                            manipulatedBasePath,
                            manipulatedDiffPath
                        };

                        numFields = 4;
                    }
                    else
                    {
                        fields = line.Split(new char[] { ' ', '\t', '"' }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    if (fields.Length != numFields)
                    {
                        Console.WriteLine($"Couldn't parse output '{line}`.");
                        continue;
                    }

                    // store diff-relative path
                    string fullBaseFilePath = Path.GetFullPath(fields[2]);
                    if (!File.Exists(fullBaseFilePath))
                    {
                        Console.WriteLine($"Couldn't parse output '{line}'.");
                        continue;
                    }

                    string baseFilePath = fullBaseFilePath.Substring(fullDiffPath.Length + 1);

                    // Sometimes .dasm is parsed as binary and we don't get numbers, just dashes
                    int addCount = 0;
                    int delCount = 0;
                    Int32.TryParse(fields[0], out addCount);
                    Int32.TryParse(fields[1], out delCount);
                    fileToTextDiffCount[baseFilePath] = addCount + delCount;
                }

                Console.WriteLine($"Found {fileToTextDiffCount.Count()} files with textual diffs.");
            }

            return fileToTextDiffCount;
        }

        public static int Main(string[] args)
        {
            // Parse incoming arguments
            Config config = new Config(args);

            Dictionary<string, int> diffCounts = DiffInText(config.DiffPath, config.BasePath);

            // Early out if no textual diffs found.
            if (diffCounts == null)
            {
                Console.WriteLine("No diffs found.");
                return 0;
            }

            try
            {
                // Extract method info from base and diff directory or file.
                var baseList = ExtractFileInfo(config.BasePath, config.Filter, config.FileExtension, config.Recursive);
                var diffList = ExtractFileInfo(config.DiffPath, config.Filter, config.FileExtension, config.Recursive);

                // Compare the method info for each file and generate a list of
                // non-zero deltas.  The lists that include files in one but not
                // the other are used as the comparator function only compares where it 
                // has both sides.

                var compareList = Comparator(baseList, diffList, config);

                // Generate warning lists if requested.
                if (config.Warn)
                {
                    WarnFiles(diffList, baseList);
                    WarnMethods(compareList);
                }

                if (config.DoGenerateTSV)
                {
                    GenerateTSV(compareList, config.TSVFileName);
                }

                if (config.DoGenerateJson)
                {
                    GenerateJson(compareList, config.JsonFileName);
                }

                return Summarize(compareList, config, diffCounts);

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
