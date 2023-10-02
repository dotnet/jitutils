// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Repeatedly run benchmarks, varying some aspect of jit behavior,
// collecting data for each run.

using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PerformanceExplorer
{
    internal class Program
    {
        static void Main()
        {
            const string ResultsPath = @"d:\bugs\cse\auto-results";
            const string BenchmarksDir = @"C:\repos\performance\src\benchmarks\micro";
            const string CheckedCoreRoot = @"d:\bugs\cse\diff\";
            const string ReleaseCoreRoot = @"d:\bugs\cse\diff-rel\";
            const string InstructionsRetiredExplorerDir = @"C:\repos\InstructionsRetiredExplorer\src";

            using StreamWriter fullReport = File.CreateText(Path.Combine(ResultsPath, "results.csv"));

            bool verbose = true;

            List<BenchmarkInfo> benchmarks = new List<string>()
            {
                 "System.Perf_Convert.ToBase64CharArray(binaryDataSize: 1024, formattingOptions: InsertLineBreaks)",
                 "System.Perf_Convert.ToDateTime_String(value: \"February 26, 2009\")",
                 "System.Tests.Perf_DateTime.ToString(format: \"G\")",
                 "System.Tests.Perf_Double.ToStringWithFormat(value: -1.7976931348623157E+308, format: \"G17\")",
                 "System.Tests.Perf_Guid.Parse",
                 "System.Tests.Perf_HashCode.Combine_6",
                 "System.Tests.Perf_String.Format_OneArg(s: \"Testing {0}, {0:C}, {0:D5}, {0:E} - {0:F4}{0:G}{0:N}  {0:X} !!\", o: 8)",
                 "System.Tests.Perf_Uri.CtorIdnHostPathAndQuery(input: \"http://dot.net/path/with?key=value#fragment\")",
                 "System.Text.Json.Document.Tests.Perf_DocumentParse.Parse(IsDataIndented: False, TestRandomAccess: False, TestCase: BasicJson)",
                 "System.Text.Json.Serialization.Tests.ReadJson<TreeRecord>.DeserializeFromUtf8Bytes(Mode: SourceGen)",
                 "System.Text.Json.Serialization.Tests.WriteJson<ImmutableSortedDictionary<String, String>>.SerializeToStream",
                 @"System.Text.RegularExpressions.Tests.Perf_Regex_Industry_Mariomkas.Count(Pattern: ""[w]+://[^/s?#]+[^s?#]+(?:?[^s#]*)?(?:#[^s]*)?"", Options: None)",
                 "V8.Crypto.Support.Bench",
                 "BenchmarksGame.Fasta_2.RunBench",
                 "BenchmarksGame.PiDigits_3.RunBench(n: 3000, expected: \"8649423196\\t:3000\")",
                 "Benchstone.BenchF.NewtR.Test",
                 "Benchstone.BenchF.Bisect.Test",
                 "CscBench.DatflowTest",
                 "JetStream.Poker.Play",
                 "System.Collections.IterateForEachNonGeneric<String>.ArrayList(Size: 512)",
                 "System.Globalization.Tests.Perf_DateTimeCultureInfo.ToString(culturestring: fr)",
                 "System.Hashing.GetStringHashCode(BytesCount: 100)",
                 "System.Text.Json.Node.Tests.Perf_ParseThenWrite.ParseThenWrite(IsDataIndented: True, TestCase: BroadTree)",
                 "System.Text.Json.Serialization.Tests.ReadJson<ClassRecord>.DeserializeFromStream(Mode: SourceGen)",
                 "System.Linq.Tests.Perf_Enumerable.Where(input: Array)",
                 "System.Numerics.Tests.Perf_BigInteger.Remainder(arguments: 1024,512 bits)",
                 "System.Buffers.Text.Tests.Utf8ParserTests.TryParseDouble(value: 1.7976931348623157e+308)",
                 "Benchstone.BenchI.Pi.Test",
                 "Benchstone.BenchI.Puzzle.Test",
                 "Benchstone.BenchI.XposMatrix.Test",
                 "JetStream.TimeSeriesSegmentation.MaximizeSchwarzCriterion",
                 "BenchmarksGame.ReverseComplement_6.RunBench",
                 "Benchmark.GetChildKeysTests.AddChainedConfigurationNoDelimiter",
                 "CscBench.CompileTest",
                 "ByteMark.BenchEmFloat",
                 "LinqBenchmarks.Where01LinqQueryX",
                 "Microsoft.Extensions.Caching.Memory.Tests.MemoryCacheTests.AddThenRemove_SlidingExpiration",
                 "Microsoft.Extensions.Configuration.Xml.XmlConfigurationProviderBenchmarks.Load(FileName: \"simple.xml\")",
                 "Microsoft.Extensions.DependencyInjection.GetServiceIEnumerable.Scoped",
                 "Struct.FilteredSpanEnumerator.Sum",
                 "System.Buffers.Text.Tests.Base64Tests.ConvertTryFromBase64Chars(NumberOfBytes: 1000)",
                 "System.Buffers.Text.Tests.Utf8FormatterTests.FormatterInt64(value: 9223372036854775807)",
                 "System.Collections.Concurrent.Count<String>.Dictionary(Size: 512)",
                 "System.Collections.ContainsKeyFalse<Int32, Int32>.SortedDictionary(Size: 512)",
                 "System.Collections.CtorFromCollection<Int32>.HashSet(Size: 512)",
                 "System.Collections.IndexerSet<Int32>.Array(Size: 512)",
                 "System.Collections.IterateFor<Int32>.Span(Size: 512)",
                 "System.Collections.Sort<Int32>.LinqOrderByExtension(Size: 512)",
                 "Microsoft.Extensions.Logging.FormattingOverhead.FourArguments_EnumerableArgument",
                 "SciMark2.kernel.benchmarkLU",
                 "System.Collections.CopyTo<Int32>.ReadOnlySpan(Size: 2048)",
                 "System.Collections.Tests.Perf_PriorityQueue<Int32, Int32>.K_Max_Elements(Size: 1000)",
                 "System.Linq.Tests.Perf_Enumerable.SingleWithPredicate_LastElementMatches(input: IEnumerable)",
                 "System.Tests.Perf_DateTimeOffset.ToString(value: 12/30/2017 3:45:22 AM -08:00)",
                 "System.Text.Tests.Perf_StringBuilder.Append_ValueTypes_Interpolated",
                 "V8.Richards.Support.Bench",
                 "System.Xml.Tests.Perf_XmlConvert.DateTime_ToString_Local",
                 @"System.Text.RegularExpressions.Tests.Perf_Regex_Industry_RustLang_Sherlock.Count(Pattern: ""\\s[a-zA-Z]{0,12}ing\\s"", Options: Compiled)",
                 "System.Text.RegularExpressions.Tests.Perf_Regex_Industry_RustLang_Sherlock.Count(Pattern: \"zqj\", Options: Compiled)",
                 "System.Text.RegularExpressions.Tests.Perf_Regex_Industry_RustLang_Sherlock.Count(Pattern: \"Sherlock|Holmes|Watson|Irene|Adler|John|Baker\", Options: Compiled)"
            }.ConvertAll(s => new BenchmarkInfo { Name = s, Ratio = double.NaN });

            StringWriter sw = new();
            sw.WriteLine(CseExperiment.Schema);

            foreach (BenchmarkInfo benchmark in benchmarks)
            {
                var experiments = new Dictionary<uint, CseExperiment>();
                if (verbose)
                {
                    Console.WriteLine(benchmark.Name);
                }
                string cleanPath = Path.Combine(ResultsPath, benchmark.CleanName);

                if (Directory.Exists(cleanPath) && File.Exists(Path.Combine(cleanPath, "error.txt")))
                {
                    if (verbose)
                    {
                        Console.WriteLine("  Skipped (error)");
                    }
                    continue;
                }

                DirectoryInfo dir = Directory.CreateDirectory(cleanPath);

                try
                {
                    List<string> args = new();

                    // Step 1: Get a trace of the benchmark
                    //
                    if (verbose)
                    {
                        Console.WriteLine("  Collect trace");
                    }
                    string outputPath = Path.Combine(dir.FullName, "bdn_collect_etl.txt");
                    string result;
                    if (!File.Exists(outputPath))
                    {
                        args.Clear();
                        args.Add("run");
                        args.Add("-c");
                        args.Add("Release");
                        args.Add("-f");
                        args.Add("net8.0");
                        args.Add("--");
                        args.Add("--filter");
                        args.Add(benchmark.Name);
                        args.Add("--corerun");
                        args.Add($"{ReleaseCoreRoot}corerun.exe");
                        args.Add("--profiler");
                        args.Add("ETW");

                        result = Invoke("dotnet.exe", BenchmarksDir, args.ToArray(), false, outputPath);
                    }
                    else
                    {
                        result = File.ReadAllText(outputPath);
                    }

                    string[] resultLines = result.ReplaceLineEndings().Split(Environment.NewLine);
                    int traceLineIndex = Array.FindIndex(resultLines, l => Regex.IsMatch(l, "Exported \\d+ trace file\\(s\\)\\. Example:"));
                    if (traceLineIndex == -1)
                        throw new Exception("Could not get trace");

                    string path = resultLines[traceLineIndex + 1];

                    // Step 2: Run InstructionRetiredExplorer to find the hot functions in the benchmark
                    //
                    if (verbose)
                    {
                        Console.WriteLine("  Find hot functions");
                    }
                    outputPath = Path.Combine(dir.FullName, "hotfunctions.txt");
                    if (!File.Exists(outputPath))
                    {
                        args.Clear();
                        args.Add("run");
                        args.Add("-c");
                        args.Add("Release");
                        args.Add("--");
                        args.Add(path);
                        args.Add("-benchmark");
                        args.Add("-process");
                        args.Add("corerun");

                        result = Invoke("dotnet.exe", InstructionsRetiredExplorerDir, args.ToArray(), false, outputPath);
                    }
                    else
                    {
                        result = File.ReadAllText(outputPath);
                    }

                    List<HotFunction> hotFunctions = ExtractHotFunctions(result).ToList();
                    HotFunction hf = null;

                    if (verbose)
                    {
                        Console.WriteLine($"  ... {hotFunctions.Count} profiled functions");
                    }

                    // Step 3. Look for a hot function (that is Tier1 and depends on CSE)
                    //
                    // Method execution must be at least 20% of benchmark time to try and ensure
                    // varying CSEs has a measurable effect.
                    //
                    double cutoffPoint = 20;

                    foreach (HotFunction hfv in hotFunctions)
                    {
                        if ((hfv.Fraction >= cutoffPoint) && hfv.CodeType.Equals("Tier-1"))
                        {
                            hf = hfv;
                            break;
                        }
                        else if ((hfv.Fraction < cutoffPoint) && !hfv.CodeType.Equals("?"))
                        {
                            if (verbose)
                            {
                                Console.WriteLine($"  ...stopping at cold method {hfv.Fraction}% {hfv.CodeType} {hfv.Name}");
                            }
                            break;
                        }
                        else
                        {
                            if (verbose)
                            {
                                Console.WriteLine($"  ... skipping hot method {hfv.Fraction}% {hfv.CodeType} {hfv.Name}");
                            }
                        }
                    }

                    if (hf == null)
                    {
                        if (verbose)
                        {
                            Console.WriteLine("  Skipped (cannot use this benchmark for CSE analysis)");
                        }
                        continue;
                    }

                    if (verbose)
                    {
                        Console.WriteLine($"  Exploring {hf.Fraction}% {hf.CodeType} {hf.Name}");
                    }

                    // Step 4: Parse perf results from BDN
                    //
                    string perfJson = Path.Combine(dir.FullName, "default-report.json");

                    if (!File.Exists(perfJson))
                    {
                        int resultLineIndex = Array.FindIndex(resultLines, l => Regex.IsMatch(l, ".*-report-full.json"));
                        if (resultLineIndex == -1)
                            throw new Exception("Could not get perf result");

                        string resultPath = resultLines[resultLineIndex].Trim();
                        File.Copy(resultPath, perfJson);
                    }

                    double perf = BdnParser.GetPerf(perfJson);

                    // Step 5: Generate SPMI collection
                    const string CheckedShimCollector = $"{CheckedCoreRoot}superpmi-shim-collector.dll";
                    const string ReleaseShimCollector = $"{ReleaseCoreRoot}superpmi-shim-collector.dll";
                    if (!File.Exists(ReleaseShimCollector))
                        File.Copy(CheckedShimCollector, ReleaseShimCollector);

                    if (verbose)
                    {
                        Console.WriteLine("  Collect SPMI collection for diff");
                    }
                    string defaultMC = Path.Combine(dir.FullName, "diff.mc");
                    if (!File.Exists(defaultMC))
                    {
                        args.Clear();
                        args.Add("run");
                        args.Add("-c");
                        args.Add("Release");
                        args.Add("-f");
                        args.Add("net8.0");
                        args.Add("--");
                        args.Add("--filter");
                        args.Add(benchmark.Name);
                        args.Add("--corerun");
                        args.Add($"{ReleaseCoreRoot}corerun.exe");
                        args.Add("--envvars");
                        args.Add("DOTNET_JitName:superpmi-shim-collector.dll");
                        args.Add($"SuperPMIShimPath:{CheckedCoreRoot}clrjit.dll");
                        args.Add(@"SuperPMIShimLogPath:" + dir.FullName);
                        outputPath = Path.Combine(dir.FullName, "bdn_collect_diff_mc.txt");
                        result = Invoke("dotnet.exe", BenchmarksDir, args.ToArray(), false, outputPath);
                        File.Move(dir.GetFiles("*.mc").Where(fi => fi.Name != "base.mc").Single().FullName, defaultMC);
                    }

                    // Step 6: Generate disassembly
                    //
                    string dasmFile = CreateDasm();

                    string CreateDasm()
                    {
                        string checkedCoreRoot = CheckedCoreRoot;
                        string baseOrDiff = "diff";
                        if (verbose)
                        {
                            Console.WriteLine($"  Generate {hf.Name} disasm");
                        }
                        string dasmDir = Path.Combine(dir.FullName, baseOrDiff);
                        Directory.CreateDirectory(dasmDir);
                        string dasmFile = Path.Combine(dasmDir, hf.DasmFileName);
                        string tmpFile = Path.GetTempFileName();
                        if (!File.Exists(dasmFile)/* || new FileInfo(dasmFile).Length == 0*/)
                        {
                            args.Clear();
                            args.Add($"{checkedCoreRoot}clrjit.dll");
                            args.Add(defaultMC);
                            args.Add("-jitoption");
                            args.Add("JitDisasm=" + hf.DisasmName);
                            args.Add("-jitoption");
                            args.Add("JitDisasmDiffable=1");
                            args.Add("-jitoption");
                            args.Add("JitStdOutFile=" + tmpFile);
                            args.Add("-jitoption");
                            args.Add("JitMetrics=1");
                            outputPath = Path.Combine(dir.FullName, $"spmi_dasm_{baseOrDiff}.txt");
                            Invoke($"{checkedCoreRoot}superpmi.exe", checkedCoreRoot, args.ToArray(), false, outputPath, code => code == 0 || code == 3);
                            File.Move(tmpFile, dasmFile, true);
                        }

                        return dasmFile;
                    }

                    // Step 7: Check resulting listing for CSEs
                    //
                    if (verbose)
                    {
                        Console.WriteLine("  Checking for CSEs...");
                    }

                    // Parse out the data of interest...
                    //
                    Regex perfScorePattern = new Regex(@"(PerfScore|perf score) (\d+(\.\d+)?)");
                    Regex codeSizePattern = new Regex(@"bytes of code ([0-9]{1,})");
                    Regex numCsePattern = new Regex(@"num cse ([0-9]{1,})");
                    Regex hashPattern = new Regex(@"MethodHash=([A-Fa-f0-9]{1,})");

                    string infoLine = File.ReadLines(dasmFile)
                        .LastOrDefault(l => l.StartsWith(@"; Total bytes of code", StringComparison.Ordinal) && l.EndsWith("(Tier1)", StringComparison.Ordinal));

                    if (infoLine == null)
                    {
                        throw new Exception("Could not find Tier-1 method details from dasm");
                    }

                    var codeSizeMatch = codeSizePattern.Match(infoLine);
                    var perfScoreMatch = perfScorePattern.Match(infoLine);
                    var numCseMatch = numCsePattern.Match(infoLine);
                    var hashMatch = hashPattern.Match(infoLine);

                    uint experimentNumber = 0;
                    CseExperiment defaultExperiment = new CseExperiment
                    {
                        Benchmark = benchmark,
                        Baseline = null,
                        Index = experimentNumber++,
                        Method = hf,
                        Mask = 0xFFFFFFFF,
                        Perf = perf,
                        PerfScore = perfScoreMatch.Success ?
                                       double.Parse(perfScoreMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
                        CodeSize = codeSizeMatch.Success ?
                                       uint.Parse(codeSizeMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                        NumCse = numCseMatch.Success ?
                                       uint.Parse(numCseMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                        Hash = hashMatch.Success ? hashMatch.Groups[1].Value : "?",
                        Explored = true
                    };

                    experiments.Add(defaultExperiment.Mask, defaultExperiment);
                    Console.WriteLine($"{defaultExperiment.Info}");
                    sw.WriteLine($"{defaultExperiment.Info}");

                    if (defaultExperiment.NumCse == 0)
                    {

                        if (verbose)
                        {
                            Console.WriteLine($"   no CSEs in hot method");
                        }
                        continue;
                    }

                    if (verbose)
                    {
                        Console.WriteLine($"   {defaultExperiment.NumCse} to explore");
                    }

                    CseExperiment RunExperiment(uint mask)
                    {
                        string maskOutputPath = Path.Combine(dir.FullName, $"bdn_cse_{mask:x8}.txt");
                        string maskDasmPath = Path.Combine(dir.FullName, $"bdn_cse_{mask:x8}.dasm");
                        string maskResult;
                        if (!File.Exists(maskOutputPath))
                        {
                            args.Clear();
                            args.Add("run");
                            args.Add("-c");
                            args.Add("Release");
                            args.Add("-f");
                            args.Add("net8.0");
                            args.Add("--");
                            args.Add("--filter");
                            args.Add(benchmark.Name);
                            args.Add("--corerun");
                            args.Add($"{CheckedCoreRoot}corerun.exe");
                            args.Add("--envVars");
                            args.Add($"DOTNET_JitCseMask:{mask:x8}");
                            args.Add($"DOTNET_JitCseHash:{defaultExperiment.Hash}");
                            args.Add($"DOTNET_JitStdOutFile:{maskDasmPath}");
                            args.Add($"DOTNET_JitDisasm:{hf.DisasmName}");
                            args.Add($"DOTNET_JitMetrics:1");
                            maskResult = Invoke("dotnet.exe", BenchmarksDir, args.ToArray(), false, maskOutputPath);
                        }
                        else
                        {
                            maskResult = File.ReadAllText(maskOutputPath);
                        }

                        // Extract Perf report from BDN json file

                        string maskPerfJson = Path.Combine(dir.FullName, $"{mask:x8}-report.json");

                        if (!File.Exists(maskPerfJson))
                        {
                            string[] maskResultLines = maskResult.ReplaceLineEndings().Split(Environment.NewLine);
                            int maskResultIndex = Array.FindIndex(maskResultLines, l => Regex.IsMatch(l, ".*-report-full.json"));
                            if (maskResultIndex == -1)
                                throw new Exception("Could not get perf result");
                            string maskResultPath = maskResultLines[maskResultIndex].Trim();
                            File.Copy(maskResultPath, maskPerfJson);
                        }

                        double maskPerf = BdnParser.GetPerf(maskPerfJson);

                        // parse method info from disasm
                        // LastOrDefault here helps with restart cases where we might have multiple dasms in a file

                        string maskInfoLine = File.ReadLines(maskDasmPath)
                            .LastOrDefault(l => l.StartsWith(@"; Total bytes of code", StringComparison.Ordinal) && l.EndsWith("(Tier1)", StringComparison.Ordinal));

                        if (maskInfoLine == null)
                        {
                            throw new Exception("Could not get data from dasm {maskDasmPath} result");
                        }

                        codeSizeMatch = codeSizePattern.Match(maskInfoLine);
                        perfScoreMatch = perfScorePattern.Match(maskInfoLine);
                        numCseMatch = numCsePattern.Match(maskInfoLine);
                        CseExperiment maskExperiment = new CseExperiment
                        {
                            Benchmark = benchmark,
                            Baseline = defaultExperiment,
                            Index = experimentNumber++,
                            Method = hf,
                            Mask = mask,
                            Perf = maskPerf,
                            PerfScore = perfScoreMatch.Success ?
                                           double.Parse(perfScoreMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
                            CodeSize = codeSizeMatch.Success ?
                                           uint.Parse(codeSizeMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                            NumCse = numCseMatch.Success ?
                                           uint.Parse(numCseMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                            Hash = defaultExperiment.Hash
                        };

                        experiments.Add(maskExperiment.Mask, maskExperiment);
                        return maskExperiment;
                    }

                    // Step 8: Run initial experiment (no CSEs)
                    //
                    CseExperiment baselineExperiment = RunExperiment(0);
                    Console.WriteLine($"{baselineExperiment.Info}");
                    sw.WriteLine($"{baselineExperiment.Info}");

                    // Steps 9 - ...? Explore.
                    //
                    uint numDataPoints = 2;
                    uint maxDataPoints = 257;
                    double bestPerf = 1e9;
                    bool keepExploring = true;

                    while (keepExploring)
                    {
                        var rankedExperiments = experiments.Values.Where(x => !x.Explored).OrderBy(x => x.Perf);

                        if (rankedExperiments.Count() == 0)
                        {
                            if (verbose)
                            {
                                Console.WriteLine("nothing left to explore");
                            }
                            break;
                        }

                        foreach (var e in rankedExperiments)
                        {
                            e.Explored = true;

                            for (uint nCSE = 1; nCSE <= defaultExperiment.NumCse; nCSE++)
                            {
                                uint mask = e.Mask | (uint)1 << (int)(nCSE - 1);
                                if (experiments.ContainsKey(mask)) continue;

                                CseExperiment maskExperiment = RunExperiment(mask);

                                if (maskExperiment.Perf < bestPerf)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    bestPerf = maskExperiment.Perf;
                                }
                                else if (maskExperiment.IsImprovement)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                }
                                Console.WriteLine($"{maskExperiment.Info}");
                                Console.ResetColor();
                                sw.WriteLine($"{maskExperiment.Info}");

                                numDataPoints++;

                                if (numDataPoints >= maxDataPoints)
                                {
                                    keepExploring = false;
                                    break;
                                }
                            }
                            break;
                        }
                    }

                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  Failure");
                    File.WriteAllText(Path.Combine(dir.FullName, "error.txt"), ex.ToString());
                }

                Console.WriteLine();
            }
            fullReport.Write(sw.GetStringBuilder());
        }

        private static IEnumerable<HotFunction> ExtractHotFunctions(string output)
        {
            string[] resultLines = output.ReplaceLineEndings(Environment.NewLine).Split(Environment.NewLine);
            int tableLineIndex = Array.FindIndex(resultLines, l => l.StartsWith("Benchmark: found"));
            tableLineIndex -= 2;
            while (!string.IsNullOrWhiteSpace(resultLines[tableLineIndex]))
                tableLineIndex--;

            tableLineIndex++;
            while (!string.IsNullOrWhiteSpace(resultLines[tableLineIndex]))
            {
                Match match = Regex.Match(resultLines[tableLineIndex], "([0-9.]+)%\\s+[0-9.E+]+\\s+(MinOpt|FullOpt|Tier-0|Tier-1|OSR|native|jit \\?\\?\\?|\\?)\\s+(.*)");
                if (!match.Success)
                    throw new Exception("Regex failure");
                double fraction = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                string codeType = match.Groups[2].Value;
                string funcName = match.Groups[3].Value;

                yield return new HotFunction { Fraction = fraction, CodeType = codeType, Name = funcName };
                tableLineIndex++;
            }
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

        private static string Invoke(string fileName, string workingDir, string[] args, bool printOutput, string outputPath, Func<int, bool> checkExitCode = null)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                FileName = fileName,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in args)
                psi.ArgumentList.Add(a);

            string command = fileName + " " + string.Join(" ", args.Select(a => "\"" + a + "\""));

            using Process p = Process.Start(psi);
            if (p == null)
                throw new Exception("Could not start child process " + fileName);

            StringBuilder stdout = new();
            StringBuilder stderr = new();
            p.OutputDataReceived += (sender, args) =>
            {
                if (printOutput)
                {
                    Console.WriteLine(args.Data);
                }
                stdout.AppendLine(args.Data);
            };
            p.ErrorDataReceived += (sender, args) =>
            {
                if (printOutput)
                {
                    Console.Error.WriteLine(args.Data);
                }
                stderr.AppendLine(args.Data);
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();

            string all = command + Environment.NewLine + Environment.NewLine + "STDOUT:" + Environment.NewLine + stdout + Environment.NewLine + Environment.NewLine + "STDERR:" + Environment.NewLine + stderr;
            File.AppendAllText(outputPath, all);

            if (checkExitCode == null ? p.ExitCode != 0 : !checkExitCode(p.ExitCode))
            {
                throw new Exception(
                    $@"
Child process '{fileName}' exited with error code {p.ExitCode}
stdout:
{stdout.ToString().Trim()}

stderr:
{stderr}".Trim());
            }


            return stdout.ToString();
        }


    }
}

