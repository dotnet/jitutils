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
using System.Globalization;
using System.Reflection;

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

        private const string DETAILS_MARKER = "SUMMARY_MARKER";
        private static string METRIC_SEP = new string('-', 80);
        private const string DETAILS_TEXT =
@"```
<details>

<summary>Detail diffs</summary>

```
";

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
            private string _md;
            private bool _noreconcile = false;
            private string _note;
            private string _filter;
            private List<string> _metrics;
            private bool _skipTextDiff = false;
            private bool _retainOnlyTopFiles = false;
            private double? _overrideTotalBaseMetric;
            private double? _overrideTotalDiffMetric;
            private bool _isDiffsOnly = false;
            private bool _isSubsetOfDiffs = false;

            public Config(string[] args)
            {
                static double? ParseDouble(string val)
                {
                    if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                        return dblVal;

                    return null;
                }

                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    syntax.DefineOption("b|base", ref _basePath, "Base file or directory.");
                    syntax.DefineOption("d|diff", ref _diffPath, "Diff file or directory.");
                    syntax.DefineOption("r|recursive", ref _recursive, "Search directories recursively.");
                    syntax.DefineOption("ext|fileExtension", ref _fileExtension, "File extension to look for. By default, .dasm");
                    syntax.DefineOption("c|count", ref _count,
                        "Count of files and methods (at most) to output in the summary."
                      + " (count) improvements and (count) regressions of each will be included."
                      + " (default 20)");
                    syntax.DefineOption("w|warn", ref _warn,
                        "Generate warning output for files/methods that only "
                      + "exists in one dataset or the other (only in base or only in diff).");
                    syntax.DefineOption("m|metrics", ref _metrics, (value) => value.Split(",").ToList(), $"Comma-separated metric to use for diff computations. Available metrics: {MetricCollection.ListMetrics()}.");
                    syntax.DefineOption("note", ref _note,
                        "Descriptive note to add to summary output");
                    syntax.DefineOption("noreconcile", ref _noreconcile,
                        "Do not reconcile unique methods in base/diff");
                    syntax.DefineOption("json", ref _json,
                        "Dump analysis data to specified file in JSON format.");
                    syntax.DefineOption("tsv", ref _tsv,
                        "Dump analysis data to specified file in tab-separated format.");
                    syntax.DefineOption("md", ref _md,
                        "Dump analysis data to specified file in markdown format.");
                    syntax.DefineOption("filter", ref _filter,
                        "Only consider assembly files whose names match the filter");
                    syntax.DefineOption("skiptextdiff", ref _skipTextDiff,
                        "Skip analysis that checks for files that have textual diffs but no metric diffs.");
                    syntax.DefineOption("retainOnlyTopFiles ", ref _retainOnlyTopFiles,
                        "Retain only the top 'count' improvements/regressions .dasm files. Delete other files. Useful in CI scenario to reduce the upload size.");
                    syntax.DefineOption("override-total-base-metric", ref _overrideTotalBaseMetric, ParseDouble,
                        "Override the total base metric shown in the output with this value. Useful when only changed .dasm files are present and these values are known.");
                    syntax.DefineOption("override-total-diff-metric", ref _overrideTotalDiffMetric, ParseDouble,
                        "Override the total diff metric shown in the output with this value. Useful when only changed .dasm files are present and these values are known.");
                    syntax.DefineOption("is-diffs-only", ref _isDiffsOnly,
                        "Specify that the disassembly files are only produced for contexts with diffs, so avoid producing output making assumptions about the number of contexts.");
                    syntax.DefineOption("is-subset-of-diffs", ref _isSubsetOfDiffs,
                        "Specify that the disassembly files are only a subset of the contexts with diffs, so avoid producing output making assumptions about the remaining diffs.");
                });

                // Run validation code on parsed input to ensure we have a sensible scenario.
                Validate();
            }

            private void Validate()
            {
                if (_basePath == null)
                {
                    _syntaxResult.ReportError("Base path (--base) is required.");
                }

                if (_diffPath == null)
                {
                    _syntaxResult.ReportError("Diff path (--diff) is required.");
                }

                if (_metrics == null)
                {
                    _metrics = new List<string> { "CodeSize" };
                }

                foreach (string metricName in _metrics)
                {
                    if (!MetricCollection.ValidateMetric(metricName))
                    {
                        _syntaxResult.ReportError($"Unknown metric '{metricName}'. Available metrics: {MetricCollection.ListMetrics()}");
                    }
                }

                if (OverrideTotalBaseMetric.HasValue != OverrideTotalDiffMetric.HasValue)
                {
                    _syntaxResult.ReportError("override-total-base-metric and override-total-diff-metric must either both be specified or both not be specified");
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
            public string MarkdownFileName { get { return _md;  } }
            public bool DoGenerateJson { get { return _json != null; } }
            public bool DoGenerateTSV { get { return _tsv != null; } }
            public bool DoGenerateMarkdown { get { return _md != null; } }
            public bool Reconcile { get { return !_noreconcile; } }
            public string Note { get { return _note; } }
            public double? OverrideTotalBaseMetric => _overrideTotalBaseMetric;
            public double? OverrideTotalDiffMetric => _overrideTotalDiffMetric;
            public bool IsDiffsOnly => _isDiffsOnly;
            public bool IsSubsetOfDiffs => _isSubsetOfDiffs;

            public string Filter {  get { return _filter; } }

            public List<string> Metrics {  get { return _metrics; } }
            public bool SkipTextDiff { get { return _skipTextDiff;  } }
            public bool RetainOnlyTopFiles { get { return _retainOnlyTopFiles; } }
        }

        public class FileInfo
        {
            public string path;
            public IEnumerable<MethodInfo> methodList;
            public override string ToString()
            {
                return path;
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

            public void Rel(Metric m)
            {
                Value = (Value - m.Value) / m.Value;
            }

            public void SetValueFrom(Metric m)
            {
                Value = m.Value;
            }

            public override string ToString()
            {
                return Name;
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

        public class AllocSizeMetric : Metric
        {
            public override string Name => "AllocSize";
            public override string DisplayName => "Allocation Size";
            public override string Unit => "byte";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new AllocSizeMetric();
            public override string ValueString => $"{Value}";
        }

        public class ExtraAllocBytesMetric : Metric
        {
            public override string Name => "ExtraAllocBytes";
            public override string DisplayName => "Extra Allocation Size";
            public override string Unit => "byte";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new ExtraAllocBytesMetric();
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

        /* LSRA specific */
        public class SpillCountMetric : Metric
        {
            public override string Name => "SpillCount";
            public override string DisplayName => "Spill Count";
            public override string Unit => "Count";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new SpillCountMetric();
            public override string ValueString => $"{Value}";
        }

        public class SpillWeightMetric : Metric
        {
            public override string Name => "SpillWeight";
            public override string DisplayName => "Spill Weighted";
            public override string Unit => "Count";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new SpillWeightMetric();
            public override string ValueString => $"{Value}";
        }

        public class ResolutionCountMetric : Metric
        {
            public override string Name => "ResolutionCount";
            public override string DisplayName => "Resolution Count";
            public override string Unit => "Count";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new ResolutionCountMetric();
            public override string ValueString => $"{Value}";
        }

        public class ResolutionWeightMetric : Metric
        {
            public override string Name => "ResolutionWeight";
            public override string DisplayName => "Resolution Weighted";
            public override string Unit => "Count";
            public override bool LowerIsBetter => true;
            public override Metric Clone() => new ResolutionWeightMetric();
            public override string ValueString => $"{Value}";
        }

        public class MetricCollection
        {
            private static Dictionary<string, int> s_metricNameToIndex;
            private static Metric[] s_metrics;

            static MetricCollection()
            {
                var derivedType = typeof(Metric);
                var currentAssembly = Assembly.GetAssembly(derivedType);
                s_metrics = currentAssembly.GetTypes()
                    .Where(t => t != derivedType && derivedType.IsAssignableFrom(t))
                    .Select(t => currentAssembly.CreateInstance(t.FullName)).Cast<Metric>().ToArray();

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

            public void Rel(MetricCollection other)
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    metrics[i].Rel(other.metrics[i]);
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
                         .AsParallel().Select(p => new FileInfo
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
            Regex allocSizePattern = new Regex(@"allocated bytes for code ([0-9]{1,})");
            Regex debugInfoPattern = new Regex(@"Variable debug info: ([0-9]{1,}) live range\(s\), ([0-9]{1,}) var\(s\)");
            Regex spillInfoPattern = new Regex(@"SpillCount (\d+) SpillCountWt (\d+\.\d+)");
            Regex resolutionInfoPattern = new Regex(@"ResolutionMovs (\d+) ResolutionMovsWt (\d+\.\d+)");

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
                                 var allocSizeMatch = allocSizePattern.Match(x.line);
                                 var debugInfoMatch = debugInfoPattern.Match(x.line);
                                 var spillInfoMatch = spillInfoPattern.Match(x.line);
                                 var resolutionInfoMatch = resolutionInfoPattern.Match(x.line);
                                 return new
                                 {
                                     name = nameMatch.Groups[1].Value,
                                     // Use matched data or default to 0
                                     totalBytes = codeAndPrologSizeMatch.Success ?
                                        int.Parse(codeAndPrologSizeMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                                     prologBytes = codeAndPrologSizeMatch.Success ?
                                        int.Parse(codeAndPrologSizeMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
                                     perfScore = perfScoreMatch.Success ?
                                        double.Parse(perfScoreMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
                                     instrCount = instrCountMatch.Success ?
                                        int.Parse(instrCountMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                                     allocSize = allocSizeMatch.Success ?
                                        int.Parse(allocSizeMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                                     debugClauseCount = debugInfoMatch.Success ?
                                        int.Parse(debugInfoMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                                     debugVarCount = debugInfoMatch.Success ?
                                        int.Parse(debugInfoMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
                                     spillCount = spillInfoMatch.Success ?
                                        int.Parse(spillInfoMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                                     spillWeight = spillInfoMatch.Success ?
                                        double.Parse(spillInfoMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
                                     resolutionCount = resolutionInfoMatch.Success ?
                                        int.Parse(resolutionInfoMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                                     resolutionWeight = resolutionInfoMatch.Success ?
                                        double.Parse(resolutionInfoMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
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

                                 int totalCodeSize = x.Sum(z => z.totalBytes);
                                 int totalAllocSize = x.Sum(z => z.allocSize);
                                 Debug.Assert(totalCodeSize <= totalAllocSize);

                                 mi.Metrics.Add("CodeSize", totalCodeSize);
                                 mi.Metrics.Add("PrologSize", x.Sum(z => z.prologBytes));
                                 mi.Metrics.Add("PerfScore", x.Sum(z => z.perfScore));
                                 mi.Metrics.Add("InstrCount", x.Sum(z => z.instrCount));
                                 mi.Metrics.Add("AllocSize", totalAllocSize);
                                 mi.Metrics.Add("ExtraAllocBytes", totalAllocSize - totalCodeSize);
                                 mi.Metrics.Add("DebugClauseCount", x.Sum(z => z.debugClauseCount));
                                 mi.Metrics.Add("DebugVarCount", x.Sum(z => z.debugVarCount));
                                 mi.Metrics.Add("SpillCount", x.Sum(z => z.spillCount));
                                 mi.Metrics.Add("SpillWeight", x.Sum(z => z.spillWeight));
                                 mi.Metrics.Add("ResolutionCount", x.Sum(z => z.resolutionCount));
                                 mi.Metrics.Add("ResolutionWeight", x.Sum(z => z.resolutionWeight));
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
            IEnumerable<FileInfo> diffInfo, Config config, string metricName)
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
                        .OrderByDescending(r => r.deltaMetrics.GetMetric(metricName).Value);

                FileDelta f = new FileDelta
                {
                    basePath = b.path,
                    diffPath = d.path,
                    baseMetrics = jointList.Sum(x => x.baseMetrics),
                    diffMetrics = jointList.Sum(x => x.diffMetrics),
                    deltaMetrics = jointList.Sum(x => x.deltaMetrics),
                    relDeltaMetrics = jointList.Sum(x => x.relDeltaMetrics),
                    methodsInBoth = jointList.Count(),
                    methodsOnlyInBase = b.methodList.Except(d.methodList, methodInfoComparer),
                    methodsOnlyInDiff = d.methodList.Except(b.methodList, methodInfoComparer),
                    methodDeltaList = jointList.Where(x => x.deltaMetrics.GetMetric(metricName).Value != 0)
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
        public static (int, string) Summarize(IEnumerable<FileDelta> fileDeltaList, Config config, string metricName)
        {
            StringBuilder summaryContents = new StringBuilder();

            var totalRelDeltaMetrics = fileDeltaList.Sum(x => x.relDeltaMetrics);
            var totalDeltaMetrics = fileDeltaList.Sum(x => x.deltaMetrics);
            var totalBaseMetrics = fileDeltaList.Sum(x => x.baseMetrics);
            var totalDiffMetrics = fileDeltaList.Sum(x => x.diffMetrics);

            if (config.Note != null)
            {
                summaryContents.AppendLine($"\n{config.Note}");
            }

            Metric totalBaseMetric = totalBaseMetrics.GetMetric(metricName);
            Metric totalDiffMetric = totalDiffMetrics.GetMetric(metricName);
            Metric totalDeltaMetric = totalDeltaMetrics.GetMetric(metricName);
            Metric totalRelDeltaMetric = totalRelDeltaMetrics.GetMetric(metricName);
            string unitName = totalBaseMetrics.GetMetric(metricName).Unit;
            string metricDisplayName = totalBaseMetrics.GetMetric(metricName).DisplayName;

            summaryContents.Append($"\nSummary of {metricDisplayName} diffs:");
            if (config.Filter != null)
            {
                summaryContents.Append($" (using filter '{config.Filter}')");
            }
            summaryContents.AppendLine(string.Format("\n({0} is better)\n", totalBaseMetric.LowerIsBetter ? "Lower" : "Higher"));

            if (config.OverrideTotalBaseMetric.HasValue)
            {
                Debug.Assert(config.OverrideTotalDiffMetric.HasValue);
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of base: {1} (overridden on cmd)", unitName, config.OverrideTotalBaseMetric.Value));
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of diff: {1} (overridden on cmd)", unitName, config.OverrideTotalDiffMetric.Value));
                double delta = config.OverrideTotalDiffMetric.Value - config.OverrideTotalBaseMetric.Value;
                summaryContents.AppendLine(string.Format(CultureInfo.InvariantCulture, "Total {0}s of delta: {1} ({2:P} of base)", unitName, delta, delta / config.OverrideTotalBaseMetric.Value));
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
                    Metric reconciledBaseMetric = reconciledBaseMetrics.GetMetric(metricName);
                    Metric reconciledDiffMetric = reconciledDiffMetrics.GetMetric(metricName);

                    summaryContents.AppendLine(string.Format("\nTotal {0} diff includes {1} {0}s from reconciling methods", unitName,
                        reconciledDiffMetric.Value - reconciledBaseMetric.Value));
                    summaryContents.AppendLine(string.Format("\tBase had {0,4} unique methods, {1,8} unique {2}s", uniqueToBase, reconciledBaseMetric.ValueString, unitName));
                    summaryContents.AppendLine(string.Format("\tDiff had {0,4} unique methods, {1,8} unique {2}s", uniqueToDiff, reconciledDiffMetric.ValueString, unitName));
                }
            }

            int requestedCount = config.Count;
            if (requestedCount == 0)
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

            void DisplayFileMetric(string headerText, int metricCount, IEnumerable<FileDelta> list)
            {
                if (metricCount > 0)
                {
                    summaryContents.AppendLine($"\n{headerText} ({unitName}s):");
                    foreach (var fileDelta in list.Take(Math.Min(metricCount, requestedCount)))
                    {
                        summaryContents.AppendLine(string.Format("    {1,8} : {0} ({2:P} of base)", fileDelta.basePath,
                            fileDelta.deltaMetrics.GetMetric(metricName).ValueString,
                            fileDelta.deltaMetrics.GetMetric(metricName).Value / fileDelta.baseMetrics.GetMetric(metricName).Value));

                        fileDelta.RetainFile = config.RetainOnlyTopFiles;
                    }
                }
            }

            DisplayFileMetric("Top file regressions", fileRegressionCount, sortedFileRegressions);
            DisplayFileMetric("Top file improvements", fileImprovementCount, sortedFileImprovements);

            summaryContents.AppendLine($"\n{sortedFileCount} total files with {metricDisplayName} differences ({fileImprovementCount} improved, {fileRegressionCount} regressed), {unchangedFileCount} unchanged.");

            var methodDeltaList = fileDeltaList
                                        .SelectMany(fd => fd.methodDeltaList, (fd, md) => new
                                        {
                                            path = fd.basePath,
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
                    foreach (var method in list.GetRange(0, Math.Min(methodCount, requestedCount)))
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

            if (config.IsSubsetOfDiffs)
            {
            }
            else if (config.IsDiffsOnly)
            {
                summaryContents.AppendLine($"\n{sortedMethodCount} total methods with {metricDisplayName} differences ({methodImprovementCount} improved, {methodRegressionCount} regressed).");
            }
            else
            {
                summaryContents.AppendLine($"\n{sortedMethodCount} total methods with {metricDisplayName} differences ({methodImprovementCount} improved, {methodRegressionCount} regressed), {unchangedMethodCount} unchanged.");
            }

            if (!config.SkipTextDiff)
            {
                // Show files with text diffs but no metric diffs.

                Dictionary<string, int> diffCounts = DiffInText(config.DiffPath, config.BasePath);

                // TODO: resolve diffs to particular methods in the files.
                var zeroDiffFilesWithDiffs = fileDeltaList.Where(x => diffCounts.ContainsKey(x.diffPath) && (x.deltaMetrics.IsZero()))
                    .OrderByDescending(x => diffCounts[x.basePath]);

                int zeroDiffFilesWithDiffCount = zeroDiffFilesWithDiffs.Count();
                if (zeroDiffFilesWithDiffCount > 0)
                {
                    summaryContents.AppendLine($"\n{zeroDiffFilesWithDiffCount} files had text diffs but no metric diffs.");
                    foreach (var zerofile in zeroDiffFilesWithDiffs.Take(config.Count))
                    {
                        summaryContents.AppendLine($"{zerofile.basePath} had {diffCounts[zerofile.basePath]} diffs");
                    }
                }
            }

            if (config.RetainOnlyTopFiles)
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
                            DeleteFile(Path.Combine(config.BasePath, fileToDelete.basePath));
                            DeleteFile(Path.Combine(config.DiffPath, fileToDelete.diffPath));
                            filesDeleted += 2;
                        }
                    }

                    Console.WriteLine($"Deleted {filesDeleted} .dasm files.");

                }
            }

            return (Math.Abs(totalDeltaMetric.Value) == 0 ? 0 : -1, summaryContents.ToString());
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

            fileContents.AppendLine(JsonConvert.SerializeObject(compareList.Where(file => !file.deltaMetrics.IsZero()), Formatting.Indented));

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
                    fileContents.Append($"{file.basePath}\t{method.name}\t");

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

        public static int Main(string[] args)
        {
            // Parse incoming arguments
            Config config = new Config(args);
            int retCode = 0;
            try
            {
                // Extract method info from base and diff directory or file.
                var baseList = ExtractFileInfo(config.BasePath, config.Filter, config.FileExtension, config.Recursive);
                var diffList = ExtractFileInfo(config.DiffPath, config.Filter, config.FileExtension, config.Recursive);

                // Compare the method info for each file and generate a list of
                // non-zero deltas.  The lists that include files in one but not
                // the other are used as the comparator function only compares where it 
                // has both sides.

                IEnumerable<FileDelta> compareList = null;
                StringBuilder tsvContents = new StringBuilder();
                StringBuilder jsonContents = new StringBuilder();
                StringBuilder markdownContents = new StringBuilder();

                foreach (var metricName in config.Metrics)
                {
                    compareList = Comparator(baseList, diffList, config, metricName);

                    if (config.DoGenerateTSV)
                    {
                        tsvContents.Append(GenerateTSV(compareList));
                    }

                    if (config.DoGenerateJson)
                    {
                        jsonContents.Append(GenerateJson(compareList));
                    }

                    var summarizedReport = Summarize(compareList, config, metricName);

                    if (config.DoGenerateMarkdown)
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
                if (config.Warn)
                {
                    WarnFiles(diffList, baseList);
                    WarnMethods(compareList);
                }

                if (tsvContents.Length > 0)
                {
                    File.WriteAllText(config.TSVFileName, tsvContents.ToString());
                }

                if (jsonContents.Length > 0)
                {
                    File.WriteAllText(config.JsonFileName, jsonContents.ToString());
                }

                if (markdownContents.Length > 0)
                {
                    File.WriteAllText(config.MarkdownFileName, markdownContents.ToString());
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
