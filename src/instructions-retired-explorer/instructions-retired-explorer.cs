// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// tracelog.exe -profilesources Help
//
//Id Name                        Interval  Min      Max
//--------------------------------------------------------------
//  0 Timer                          10000  1221    1000000
//  2 TotalIssues                    65536  4096 2147483647
//  6 BranchInstructions             65536  4096 2147483647
// 10 CacheMisses                    65536  4096 2147483647
// 11 BranchMispredictions           65536  4096 2147483647
// 19 TotalCycles                    65536  4096 2147483647
// 25 UnhaltedCoreCycles             65536  4096 2147483647
// 26 InstructionRetired           1000000  4096 2147483647
// 27 UnhaltedReferenceCycles        65536  4096 2147483647
// 28 LLCReference                   65536  4096 2147483647
// 29 LLCMisses                      65536  4096 2147483647
// 30 BranchInstructionRetired       65536  4096 2147483647
// 31 BranchMispredictsRetired       65536  4096 2147483647
// 32 LbrInserts                     65536  4096 2147483647
// 33 InstructionsRetiredFixed       65536  4096 2147483647
// 34 UnhaltedCoreCyclesFixed        65536  4096 2147483647
// 35 UnhaltedReferenceCyclesFixed      65536  4096 2147483647
// 36 TimerFixed                     10000  1221    1000000

namespace CoreClrInstRetired
{
    public class ImageInfo
    {
        public readonly string Name;
        public readonly ulong BaseAddress;
        public readonly int Size;
        public ulong SampleCount;
        public ulong EndAddress;
        public bool IsJitGeneratedCode;
        public bool IsJittedCode;
        public bool IsBackupImage;
        public long AssemblyId;
        public OptimizationTier Tier;

        public ImageInfo(string name, ulong baseAddress, int size)
        {
            Name = name;
            BaseAddress = baseAddress;
            Size = size;
            EndAddress = baseAddress + (uint)Size;
            SampleCount = 0;
            IsJitGeneratedCode = false;
            IsBackupImage = false;
            AssemblyId = -1;
        }

        public static int LowerAddress(ImageInfo x, ImageInfo y)
        {
            if (x.BaseAddress < y.BaseAddress)
                return -1;
            else if (x.BaseAddress > y.BaseAddress)
                return 1;
            else if (x.EndAddress > y.EndAddress)
                return -1;
            else if (x.EndAddress > y.EndAddress)
                return 1;
            else
                return 0;
        }

        public static int MoreSamples(ImageInfo x, ImageInfo y)
        {
            if (x.SampleCount > y.SampleCount)
                return -1;
            else if (x.SampleCount < y.SampleCount)
                return 1;
            else
                return LowerAddress(x, y);
        }

        public bool ContainsAddress(ulong address)
        {
            return (address >= BaseAddress && address < EndAddress);
        }
    }

    public class AssemblyInfo
    {
        public string Name;
        public long Id;
        public AssemblyFlags Flags;
        public int NumberJitted;
        public int NumberPrejitted;
        public string CodegenKind;
        public double TimeJitting;

        public int MethodCount => NumberJitted + NumberPrejitted;

        public static int MoreMethods(AssemblyInfo x, AssemblyInfo y)
        {
            return x.MethodCount - y.MethodCount;
        }
    }

    public class ModuleInfo
    {
        public long Id;
        public long AssemblyId;
    }

    public class JitInvocation
    {
        public int ThreadId;
        public long MethodId;
        public ulong InitialThreadCount;
        public ulong FinalThreadCount;
        public string MethodName;
        public JitInvocation PriorJitInvocation;
        public double InitialTimestamp;
        public double FinalTimestamp;
        public long AssemblyId;

        public static int MoreJitInstructions(JitInvocation x, JitInvocation y)
        {
            ulong samplesX = x.JitInstrs();
            ulong samplesY = y.JitInstrs();
            if (samplesX < samplesY)
                return 1;
            else if (samplesY < samplesX)
                return -1;
            else return x.MethodId.CompareTo(y.MethodId);
        }

        public static int MoreJitTime(JitInvocation x, JitInvocation y)
        {
            double timeX = x.JitTime();
            double timeY = y.JitTime(); ;
            if (timeX < timeY)
                return 1;
            else if (timeY < timeX)
                return -1;
            else return x.MethodId.CompareTo(y.MethodId);
        }

        public double JitTime()
        {
            if (FinalTimestamp < InitialTimestamp) return 0;
            return (FinalTimestamp - InitialTimestamp);
        }

        public ulong JitInstrs()
        {
            if (FinalThreadCount < InitialThreadCount) return 0;
            return (FinalThreadCount - InitialThreadCount);
        }
    }

    public class BenchmarkInterval
    {
        public double startTimestamp;
        public double endTimestamp;
    }

    public class Program
    {
        public static SortedDictionary<ulong, ulong> SampleCountMap = new SortedDictionary<ulong, ulong>();
        public static Dictionary<int, ulong> ThreadCountMap = new Dictionary<int, ulong>();
        public static Dictionary<string, ImageInfo> ImageMap = new Dictionary<string, ImageInfo>();
        public static Dictionary<int, JitInvocation> ActiveJitInvocations = new Dictionary<int, JitInvocation>();
        public static Dictionary<long, ModuleInfo> moduleInfo = new Dictionary<long, ModuleInfo>();
        public static Dictionary<long, AssemblyInfo> assemblyInfo = new Dictionary<long, AssemblyInfo>();
        public static List<JitInvocation> AllJitInvocations = new List<JitInvocation>();
        public static List<BenchmarkInterval> BenchmarkIntervals = new List<BenchmarkInterval>();
        public static ulong JitSampleCount = 0;
        public static ulong TotalSampleCount = 0;

        // unknown code
        public static ulong UnknownImageSampleCount = 0;

        // all managed code (jit-generated)
        public static ulong JitGeneratedCodeSampleCount = 0;

        // all jitted code
        public static ulong JittedCodeSampleCount = 0;

        // all pre-jitted code
        public static ulong PreJittedCodeSampleCount = 0;

        // categories of jitted code
        public static ulong JitMinOptSampleCount = 0;
        public static ulong JitFullOptSampleCount = 0;
        public static ulong JitTier0SampleCount = 0;
        public static ulong JitTier1SampleCount = 0;
        public static ulong JitTier1OSRSampleCount = 0;
        public static ulong JitTier0InstrSampleCount = 0;
        public static ulong JitTier1InstrSampleCount = 0;
        public static ulong JitMysterySampleCount = 0;


        public static ulong JittedCodeSize = 0;
        public static ulong ManagedMethodCount = 0;
        public static ulong PMCInterval = 65536;
        public static string jitDllKey;
        public static double ProcessStart;
        public static double ProcessEnd;

        public static string samplePattern;

        static void UpdateSampleCountMap(ulong address, ulong count)
        {
            if (!SampleCountMap.ContainsKey(address))
            {
                SampleCountMap[address] = 0;
            }
            SampleCountMap[address] += count;
            TotalSampleCount += count;
        }

        static void UpdateThreadCountMap(int threadId, ulong count)
        {
            if (!ThreadCountMap.ContainsKey(threadId))
            {
                ThreadCountMap[threadId] = 0;
            }
            ThreadCountMap[threadId] += count;
        }

        static void AttributeSampleCounts()
        {
            // Sort images by starting address.
            ImageInfo[] imageArray = new ImageInfo[ImageMap.Count];
            ImageMap.Values.CopyTo(imageArray, 0);
            Array.Sort(imageArray, ImageInfo.LowerAddress);
            int index = 0;
            int backupIndex = 0;

            foreach (ulong address in SampleCountMap.Keys)
            {
                ImageInfo image = null;

                // See if any non-backup image can claim this address.
                for (int i = index; i < imageArray.Length; i++)
                {
                    if (!imageArray[i].IsBackupImage && imageArray[i].ContainsAddress(address))
                    {
                        image = imageArray[i];
                        index = i;
                        break;
                    }
                }

                // If that fails, see if any backup image can claim this address
                if (image == null)
                {
                    for (int i = backupIndex; i < imageArray.Length; i++)
                    {
                        if (imageArray[i].IsBackupImage && imageArray[i].ContainsAddress(address))
                        {
                            image = imageArray[i];
                            backupIndex = i;
                            break;
                        }
                    }
                }

                ulong counts = SampleCountMap[address];

                if (image == null)
                {
                    bool significant = ((double)counts / TotalSampleCount) > 0.001;
                    if (significant)
                    {
                        Console.WriteLine("Can't map address {0:X} -- {1} counts", address, SampleCountMap[address]);
                    }
                    UnknownImageSampleCount += counts;
                    continue;
                }

                // Console.WriteLine($"{counts} counts for image {image.Name} at {address:X16}");

                image.SampleCount += counts;

                if (image.IsJitGeneratedCode)
                {
                    JitGeneratedCodeSampleCount += counts;

                    if (image.IsJittedCode)
                    {
                        JittedCodeSampleCount += counts;

                        switch (image.Tier)
                        {
                            case OptimizationTier.MinOptJitted: JitMinOptSampleCount += counts; break;
                            case OptimizationTier.Optimized: JitFullOptSampleCount += counts; break;
                            case OptimizationTier.QuickJitted: JitTier0SampleCount += counts; break;
                            case OptimizationTier.OptimizedTier1: JitTier1SampleCount += counts; break;
                            case OptimizationTier.OptimizedTier1OSR: JitTier1OSRSampleCount += counts; break;
                            case OptimizationTier.QuickJittedInstrumented: JitTier0InstrSampleCount += counts; break;
                            case OptimizationTier.OptimizedTier1Instrumented: JitTier1InstrSampleCount += counts; break;
                            default: JitMysterySampleCount += counts; break;
                        }
                    }
                    else
                    {
                        PreJittedCodeSampleCount += counts;
                    }
                }
                continue;
            }
        }

        private static string GetName(MethodLoadUnloadVerboseTraceData data, string assembly)
        {
            // Prepare sig (strip return value)
            var sig = "";
            var sigWithRet = data.MethodSignature;
            var parenIdx = sigWithRet.IndexOf('(');
            if (0 <= parenIdx)
                sig = sigWithRet.Substring(parenIdx);

            // prepare class name (strip namespace)
            var className = data.MethodNamespace;
            var lastDot = className.LastIndexOf('.');
            var firstBox = className.IndexOf('[');
            if (0 <= lastDot && (firstBox < 0 || lastDot < firstBox))
                className = className.Substring(lastDot + 1);
            var sep = ".";
            if (className.Length == 0)
                sep = "";

            return assembly + className + sep + data.MethodName + sig;
        }

        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Usage();
                return;
            }

            string traceFile = null;
            string benchmarkName = null;
            bool showEvents = false;
            bool showJitTimes = false;
            bool filterToBenchmark = false;
            bool instructionsRetired = false;
            BenchmarkInterval benchmarkInterval = null;
            int targetPid = -2;
            int benchmarkPid = -2;
            bool isPartialProcess = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-process":
                        {
                            if (i + 1 == args.Length)
                            {
                                Console.WriteLine($"Missing process name after '{args[i]}'");
                            }
                            benchmarkName = args[i + 1];
                            i++;
                        }
                        break;
                    case "-pid":
                        {
                            if (i + 1 == args.Length)
                            {
                                Console.WriteLine($"Missing pid value after '{args[i]}'");
                            }
                            bool parsed = Int32.TryParse(args[i + 1], out targetPid);
                            if (!parsed)
                            {
                                Console.WriteLine($"Can't parse `{args[i + 1]}` as pid value");
                            }
                            i++;
                        }
                        break;
                    case "-show-samples":
                        {
                            if (i + 1 == args.Length)
                            {
                                Console.WriteLine($"Missing pattern value after '{args[i]}'");
                            }
                            samplePattern = args[i + 1];
                            Console.WriteLine($"Will show samples for methods matching `{samplePattern}`");
                            i++;
                        }
                        break;
                    case "-show-events":
                        showEvents = true;
                        break;
                    case "-show-jit-times":
                        showJitTimes = true;
                        break;
                    case "-instructions-retired":
                        instructionsRetired = true;
                        break;
                    case "-benchmark":
                        if (benchmarkName == null)
                        {
                            benchmarkName = "dotnet";
                        }
                        filterToBenchmark = true;
                        break;
                    default:
                        if (args[i].StartsWith("-"))
                        {
                            Usage();
                            return;
                        }
                        traceFile = args[i];
                        break;
                }
            }

            if (traceFile == null)
            {
                Console.WriteLine($"Must specify trace file");
                return;
            }

            if (!File.Exists(traceFile))
            {
                Console.WriteLine($"Can't find trace file '{traceFile}'");
                return;
            }

            if (benchmarkName == null)
            {
                benchmarkName = "corerun";
            }

            Console.WriteLine($"Mining ETL from {traceFile} for process {benchmarkName}");

            Dictionary<string, uint> allEventCounts = new Dictionary<string, uint>();
            Dictionary<string, uint> eventCounts = new Dictionary<string, uint>();
            Dictionary<string, uint> processCounts = new Dictionary<string, uint>();

            Dictionary<long, string> assemblyNames = new Dictionary<long, string>();

            using (var source = new ETWTraceEventSource(traceFile))
            {
                source.Dynamic.All += delegate (TraceEvent data)
                {
                    if (allEventCounts.ContainsKey(data.EventName))
                    {
                        allEventCounts[data.EventName]++;
                    }
                    else
                    {
                        allEventCounts[data.EventName] = 1;
                    }

                    switch (data.EventName)
                    {
                        case "WorkloadActual/Start":
                            {
                                if (benchmarkInterval != null)
                                {
                                    Console.WriteLine($"Eh? benchmark intervals overlap at {data.TimeStampRelativeMSec}ms");
                                }
                                else
                                {
                                    benchmarkInterval = new BenchmarkInterval();
                                    benchmarkInterval.startTimestamp = data.TimeStampRelativeMSec;
                                }
                            }
                            break;
                        case "WorkloadActual/Stop":
                            {
                                if (benchmarkInterval == null)
                                {
                                    Console.WriteLine($"Eh? benchmark intervals overlap at {data.TimeStampRelativeMSec}ms");
                                }
                                else
                                {
                                    benchmarkInterval.endTimestamp = data.TimeStampRelativeMSec;
                                    BenchmarkIntervals.Add(benchmarkInterval);
                                    benchmarkInterval = null;
                                }

                            }
                            break;
                    }
                };

                source.Kernel.All += delegate (TraceEvent data)
                {
                    if (allEventCounts.ContainsKey(data.EventName))
                    {
                        allEventCounts[data.EventName]++;
                    }
                    else
                    {
                        allEventCounts[data.EventName] = 1;
                    }

                    if (data.ProcessID == benchmarkPid)
                    {
                        if (eventCounts.ContainsKey(data.EventName))
                        {
                            eventCounts[data.EventName]++;
                        }
                        else
                        {
                            eventCounts[data.EventName] = 1;
                        }
                    }

                    switch (data.EventName)
                    {
                        case "Process/Start":
                        case "Process/DCStart":
                            {
                                // Process was running when tracing started (DCStart)
                                // or started when tracing was running (Start)
                                ProcessTraceData pdata = (ProcessTraceData)data;

                                if (benchmarkPid == -2)
                                {
                                    if (pdata.ProcessID == targetPid || String.Equals(pdata.ProcessName, benchmarkName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Console.WriteLine("Found process [{0}] {1}: {2}", pdata.ProcessID, pdata.ProcessName, pdata.CommandLine);

                                        if (filterToBenchmark)
                                        {
                                            if (pdata.CommandLine.Contains("--benchmarkName"))
                                            {
                                                benchmarkPid = pdata.ProcessID;
                                                ProcessStart = pdata.TimeStampRelativeMSec;
                                                isPartialProcess = data.EventName.Equals("Process/DCStart");
                                                Console.WriteLine();
                                                Console.WriteLine($"==> benchmark process is [{benchmarkPid}]");
                                                Console.WriteLine();
                                            }
                                        }
                                        else
                                        {
                                            benchmarkPid = pdata.ProcessID;
                                            ProcessStart = pdata.TimeStampRelativeMSec;
                                            benchmarkName = pdata.ProcessName;
                                            isPartialProcess = data.EventName.Equals("Process/DCStart");

                                            Console.WriteLine();
                                            Console.WriteLine($"==> benchmark process is [{benchmarkPid}]");
                                            Console.WriteLine();
                                        }
                                    }
                                }
                                else if (String.Equals(pdata.ProcessName, benchmarkName, StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine("Ignoring events from process [{0}] {1}: {2}", pdata.ProcessID, pdata.ProcessName, pdata.CommandLine);
                                }
                                break;
                            }

                        case "Image/DCStart":
                            {
                                ImageLoadTraceData imageLoadTraceData = (ImageLoadTraceData)data;

                                if (data.ProcessID == 0 || data.ProcessID == benchmarkPid)
                                {
                                    string fileName = imageLoadTraceData.FileName;
                                    ulong imageBase = imageLoadTraceData.ImageBase;
                                    int imageSize = imageLoadTraceData.ImageSize;

                                    string fullName = fileName + "@" + imageBase.ToString();

                                    if (!ImageMap.ContainsKey(fullName))
                                    {
                                        ImageInfo imageInfo = new ImageInfo(Path.GetFileName(fileName), imageBase, imageSize);
                                        ImageMap.Add(fullName, imageInfo);
                                    }
                                }

                                break;
                            }

                        case "Image/Load":
                            {
                                ImageLoadTraceData imageLoadTraceData = (ImageLoadTraceData)data;

                                if (imageLoadTraceData.ProcessID == benchmarkPid)
                                {
                                    string fileName = imageLoadTraceData.FileName;
                                    ulong imageBase = imageLoadTraceData.ImageBase;
                                    int imageSize = imageLoadTraceData.ImageSize;

                                    // Hackily suppress ngen images here, otherwise we lose visibility
                                    // into ngen methods...

                                    // Console.WriteLine($"Image 0x{imageBase:X16} for 0x{imageSize:X} bytes: {fileName}");

                                    string fullName = fileName + "@" + imageBase.ToString();

                                    if (!ImageMap.ContainsKey(fullName))
                                    {
                                        ImageInfo imageInfo = new ImageInfo(Path.GetFileName(fileName), imageBase, imageSize);

                                        if (fileName.Contains("Microsoft.") || fileName.Contains("System.") || fileName.Contains("Newtonsoft."))
                                        {
                                            imageInfo.IsBackupImage = true;
                                        }

                                        ImageMap.Add(fullName, imageInfo);

                                        if (fileName.Contains("clrjit.dll"))
                                        {
                                            jitDllKey = fullName;
                                        }
                                    }
                                }

                                break;
                            }

                        case "PerfInfo/Sample":
                            {
                                SampledProfileTraceData traceData = (SampledProfileTraceData)data;
                                if ((traceData.ProcessID == benchmarkPid) && !instructionsRetired)
                                {
                                    if (!filterToBenchmark || (benchmarkInterval != null))
                                    {
                                        ulong instructionPointer = traceData.InstructionPointer;
                                        ulong count = PMCInterval;
                                        UpdateSampleCountMap(instructionPointer, count);
                                        UpdateThreadCountMap(traceData.ThreadID, count);
                                    }
                                }
                                break;
                            }

                        case "PerfInfo/PMCSample":
                            {
                                // Per above, sample ID 26 is instructions retired
                                //
                                PMCCounterProfTraceData traceData = (PMCCounterProfTraceData)data;
                                if (instructionsRetired && (traceData.ProcessID == benchmarkPid) && (traceData.ProfileSource == 26))
                                {
                                    if (!filterToBenchmark || (benchmarkInterval != null))
                                    {
                                        ulong instructionPointer = traceData.InstructionPointer;
                                        ulong count = PMCInterval;
                                        UpdateSampleCountMap(instructionPointer, count);
                                        UpdateThreadCountMap(traceData.ThreadID, count);
                                    }
                                }
                                break;
                            }

                        case "PerfInfo/CollectionStart":
                            SampledProfileIntervalTraceData sampleData = (SampledProfileIntervalTraceData)data;
                            PMCInterval = (ulong)sampleData.NewInterval;
                            Console.WriteLine($"PMC interval now {PMCInterval}");
                            break;
                    }
                };

                source.Clr.All += delegate (TraceEvent data)
                {
                    if (allEventCounts.ContainsKey(data.EventName))
                    {
                        allEventCounts[data.EventName]++;
                    }
                    else
                    {
                        allEventCounts[data.EventName] = 1;
                    }

                    if (data.ProcessID == benchmarkPid)
                    {
                        if (eventCounts.ContainsKey(data.EventName))
                        {
                            eventCounts[data.EventName]++;
                        }
                        else
                        {
                            eventCounts[data.EventName] = 1;
                        }

                        switch (data.EventName)
                        {
                            case "Loader/AssemblyLoad":
                                {
                                    AssemblyLoadUnloadTraceData assemblyData = (AssemblyLoadUnloadTraceData)data;
                                    string assemblyName = assemblyData.FullyQualifiedAssemblyName;
                                    int cpos = assemblyName.IndexOf(',');
                                    string shortAssemblyName = '[' + assemblyName.Substring(0, cpos) + ']';
                                    AssemblyInfo info = new AssemblyInfo();
                                    info.Name = assemblyName.Substring(0, cpos);
                                    info.Id = assemblyData.AssemblyID;
                                    info.Flags = assemblyData.AssemblyFlags;
                                    assemblyNames[assemblyData.AssemblyID] = shortAssemblyName;
                                    assemblyInfo[assemblyData.AssemblyID] = info;
                                    // Console.WriteLine($"Assembly {shortAssemblyName} at 0x{assemblyData.AssemblyID:X}");
                                    break;
                                }
                            case "Loader/ModuleLoad":
                                {
                                    ModuleLoadUnloadTraceData moduleData = (ModuleLoadUnloadTraceData)data;
                                    ModuleInfo info = new ModuleInfo();
                                    info.Id = moduleData.ModuleID;
                                    info.AssemblyId = moduleData.AssemblyID;
                                    moduleInfo[moduleData.ModuleID] = info;
                                    // Console.WriteLine($"Module {moduleData.ModuleILFileName} for assembly 0x{info.AssemblyId}");
                                    break;
                                }
                            case "Method/JittingStarted":
                                {
                                    MethodJittingStartedTraceData jitStartData = (MethodJittingStartedTraceData)data;
                                    JitInvocation jitInvocation = new JitInvocation();
                                    jitInvocation.ThreadId = jitStartData.ThreadID;
                                    jitInvocation.MethodId = jitStartData.MethodID;
                                    jitInvocation.InitialTimestamp = jitStartData.TimeStampRelativeMSec;
                                    if (moduleInfo.ContainsKey(jitStartData.ModuleID))
                                    {
                                        jitInvocation.AssemblyId = moduleInfo[jitStartData.ModuleID].AssemblyId;
                                    }
                                    UpdateThreadCountMap(jitInvocation.ThreadId, 0); // hack
                                    jitInvocation.InitialThreadCount = ThreadCountMap[jitInvocation.ThreadId];
                                    if (ActiveJitInvocations.ContainsKey(jitInvocation.ThreadId))
                                    {
                                        jitInvocation.PriorJitInvocation = ActiveJitInvocations[jitInvocation.ThreadId];
                                        ActiveJitInvocations.Remove(jitInvocation.ThreadId);
                                    }
                                    ActiveJitInvocations.Add(jitInvocation.ThreadId, jitInvocation);
                                    AllJitInvocations.Add(jitInvocation);
                                    break;
                                }
                            case "Method/LoadVerbose":
                                {
                                    MethodLoadUnloadVerboseTraceData loadUnloadData = (MethodLoadUnloadVerboseTraceData)data;

                                    JitInvocation j = null;

                                    if (ActiveJitInvocations.ContainsKey(loadUnloadData.ThreadID))
                                    {
                                        j = ActiveJitInvocations[loadUnloadData.ThreadID];
                                        ActiveJitInvocations.Remove(j.ThreadId);
                                        if (j.PriorJitInvocation != null)
                                        {
                                            ActiveJitInvocations.Add(j.ThreadId, j.PriorJitInvocation);
                                        }
                                        j.FinalThreadCount = ThreadCountMap[j.ThreadId];
                                        j.FinalTimestamp = loadUnloadData.TimeStampRelativeMSec;
                                        JitSampleCount += j.JitInstrs();
                                        ManagedMethodCount++;
                                        JittedCodeSize += (ulong)loadUnloadData.MethodExtent;
                                    }
                                    else
                                    {
                                        // Console.WriteLine("eh? no active jit for load verbose?");                                   
                                    }

                                    // Pretend this is an "image"

                                    long assemblyId = -1;
                                    string assemblyName = "<unknown>";

                                    if (moduleInfo.ContainsKey(loadUnloadData.ModuleID))
                                    {
                                        assemblyId = moduleInfo[loadUnloadData.ModuleID].AssemblyId;
                                        if (assemblyNames.ContainsKey(assemblyId))
                                        {
                                            assemblyName = assemblyNames[assemblyId];
                                        }
                                    }

                                    string fullName = GetName(loadUnloadData, assemblyName);
                                    if (j != null) j.MethodName = fullName;
                                    
                                    // Console.WriteLine($"Method 0x{loadUnloadData.MethodStartAddress:X16} for 0x{loadUnloadData.MethodSize:X} bytes: {fullName}");
                                    
                                    // string key = fullName + "@" + loadUnloadData.MethodID.ToString("X");
                                    string key = loadUnloadData.MethodID.ToString("X") + loadUnloadData.ReJITID;
                                    if (!ImageMap.ContainsKey(key))
                                    {
                                        ImageInfo methodInfo = new ImageInfo(fullName, loadUnloadData.MethodStartAddress,
                                            loadUnloadData.MethodSize);
                                        
                                        methodInfo.IsJitGeneratedCode = true;
                                        methodInfo.IsJittedCode = loadUnloadData.IsJitted;
                                        methodInfo.Tier = (OptimizationTier) loadUnloadData.PayloadByName("OptimizationTier");
                                        methodInfo.AssemblyId = assemblyId;
                                        
                                        ImageMap.Add(key, methodInfo);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"eh? reloading method {fullName}");
                                    }

                                    break;
                                }
                            case "Method/UnloadVerbose":
                                {
                                    // Pretend this is an "image"
                                    MethodLoadUnloadVerboseTraceData loadUnloadData = (MethodLoadUnloadVerboseTraceData)data;

                                    string assemblyName = "<unknown>";
                                    long assemblyId = -1;
                                    if (moduleInfo.ContainsKey(loadUnloadData.ModuleID))
                                    {
                                        assemblyId = moduleInfo[loadUnloadData.ModuleID].AssemblyId;
                                        
                                        if (assemblyNames.ContainsKey(assemblyId))
                                        {
                                            assemblyName = assemblyNames[assemblyId];
                                        }
                                    }
                                    string fullName = GetName(loadUnloadData, assemblyName);

                                    // Console.WriteLine($"Unload @ {loadUnloadData.MethodStartAddress:X16}: {fullName}");
                                    // string key = fullName + "@" + loadUnloadData.MethodID.ToString("X");
                                    string key = loadUnloadData.MethodID.ToString("X") + loadUnloadData.ReJITID;

                                    if (!ImageMap.ContainsKey(key))
                                    {
                                        // Pretend this is an "image"
                                        ImageInfo methodInfo = new ImageInfo(fullName, loadUnloadData.MethodStartAddress,
                                            loadUnloadData.MethodSize);

                                        methodInfo.IsJitGeneratedCode = true;
                                        methodInfo.IsJittedCode = loadUnloadData.IsJitted;
                                        methodInfo.Tier = (OptimizationTier) loadUnloadData.PayloadByName("OptimizationTier");
                                        methodInfo.AssemblyId = assemblyId;

                                        ImageMap.Add(key, methodInfo);
                                    }
                                    else
                                    {
                                        // Console.WriteLine($"eh? see method {fullName} again in rundown");
                                    }
                                }
                                break;
                        }
                    }
                };

                source.Process();
            };

            AttributeSampleCounts();

            if (showEvents)
            {
                Console.WriteLine("Event Breakdown");

                foreach (var e in eventCounts)
                {
                    Console.WriteLine("Event {0} occurred {1} times", e.Key, e.Value);
                }
            }

            string eventToSummarize = instructionsRetired ? "PerfInfo/PMCSample" : "PerfInfo/Sample";
            string eventName = instructionsRetired ? "Instructions" : "Samples";
            string summaryType = filterToBenchmark ? "Benchmark Intervals" : "Process";

            if (isPartialProcess) summaryType += " (partial)";

            if (!eventCounts.ContainsKey(eventToSummarize))
            {
                Console.WriteLine($"No {eventName} events seen for {benchmarkName}.");
            }
            else
            {
                ulong CountsPerEvent = 1; // review
                ulong eventCount = eventCounts[eventToSummarize];
                ulong JitDllSampleCount = jitDllKey == null ? 0 : ImageMap[jitDllKey].SampleCount;
                ulong JitInterfaceCount = JitSampleCount - JitDllSampleCount;

                Console.WriteLine($"{eventName} for {benchmarkName}: {eventCount} events for {summaryType}");

                Console.WriteLine("Jitting           : {0:00.00%} {1,-8:G3} samples {2} methods",
                    (double)JitSampleCount / TotalSampleCount, JitSampleCount * CountsPerEvent, AllJitInvocations.Count);
                Console.WriteLine("  JitInterface    : {0:00.00%} {1,-8:G3} samples",
                    (double)JitInterfaceCount / TotalSampleCount, JitInterfaceCount * CountsPerEvent);
                Console.WriteLine("Jit-generated code: {0:00.00%} {1,-8:G3} samples",
                    (double)JitGeneratedCodeSampleCount / TotalSampleCount, JitGeneratedCodeSampleCount * CountsPerEvent);
                Console.WriteLine("  Jitted code     : {0:00.00%} {1,-8:G3} samples",
                    (double)JittedCodeSampleCount / TotalSampleCount, JittedCodeSampleCount * CountsPerEvent);
                Console.WriteLine("  MinOpts code    : {0:00.00%} {1,-8:G3} samples",
                    (double)JitMinOptSampleCount / TotalSampleCount, JitMinOptSampleCount * CountsPerEvent);
                Console.WriteLine("  FullOpts code   : {0:00.00%} {1,-8:G3} samples",
                    (double)JitFullOptSampleCount / TotalSampleCount, JitFullOptSampleCount * CountsPerEvent);
                Console.WriteLine("  Tier-0 code     : {0:00.00%} {1,-8:G3} samples",
                    (double)JitTier0SampleCount / TotalSampleCount, JitTier0SampleCount * CountsPerEvent);
                Console.WriteLine("  Tier-1 code     : {0:00.00%} {1,-8:G3} samples",
                    (double)JitTier1SampleCount / TotalSampleCount, JitTier1SampleCount * CountsPerEvent);
                Console.WriteLine("  Tier-0 inst code: {0:00.00%} {1,-8:G3} samples",
                    (double)JitTier0InstrSampleCount / TotalSampleCount, JitTier0InstrSampleCount * CountsPerEvent);
                Console.WriteLine("  Tier-1 inst code: {0:00.00%} {1,-8:G3} samples",
                    (double)JitTier1InstrSampleCount / TotalSampleCount, JitTier1InstrSampleCount * CountsPerEvent);
                Console.WriteLine("  R2R code        : {0:00.00%} {1,-8:G3} samples",
                    (double)PreJittedCodeSampleCount / TotalSampleCount, PreJittedCodeSampleCount * CountsPerEvent);

                if (JitMysterySampleCount > 0)
                {
                    Console.WriteLine("  ???     code    : {0:00.00%} {1,-8:G3} samples",
                        (double)JitMysterySampleCount / TotalSampleCount, JitMysterySampleCount * CountsPerEvent);
                }
                Console.WriteLine();

                double ufrac = (double)UnknownImageSampleCount / TotalSampleCount;
                if (ufrac > 0.002)
                {
                    Console.WriteLine("{0:00.00%}   {1,-8:G3}    {2} {3}",
                        ufrac,
                        UnknownImageSampleCount * CountsPerEvent,
                        "?       ",
                        "Unknown");
                }

                // Collect up significant counts
                List<ImageInfo> significantInfos = new List<ImageInfo>();

                foreach (var i in ImageMap)
                {
                    double frac = (double)i.Value.SampleCount / TotalSampleCount;
                    if (frac > 0.0005)
                    {
                        significantInfos.Add(i.Value);
                    }
                }

                significantInfos.Sort(ImageInfo.MoreSamples);

                foreach (var i in significantInfos)
                {
                    string codeDesc = "native ";

                    if (i.IsJitGeneratedCode)
                    {
                        if (i.IsJittedCode)
                        {
                            switch (i.Tier)
                            {
                                case OptimizationTier.MinOptJitted:               codeDesc = "MinOpt "; break;
                                case OptimizationTier.Optimized:                  codeDesc = "FullOpt"; break;
                                case OptimizationTier.QuickJitted:                codeDesc = "Tier-0 "; break;
                                case OptimizationTier.OptimizedTier1:             codeDesc = "Tier-1 "; break;
                                case OptimizationTier.OptimizedTier1OSR:          codeDesc = "OSR    "; break;
                                case OptimizationTier.QuickJittedInstrumented:    codeDesc = "Tier-0i"; break;
                                case OptimizationTier.OptimizedTier1Instrumented: codeDesc = "Tier-1i"; break;
                                default: codeDesc = "???"; break;
                            }
                        }
                        else
                        {
                            codeDesc = "R2R";
                        }
                    }
                    Console.WriteLine("{0:00.00%}   {1,-9:G4}   {2}  {3}",
                        (double)i.SampleCount / TotalSampleCount,
                        i.SampleCount * CountsPerEvent, codeDesc,
                        i.Name);
                }

                if (showJitTimes)
                {
                    // Show significant jit invocations (samples)
                    AllJitInvocations.Sort(JitInvocation.MoreJitInstructions);
                    bool printed = false;
                    ulong signficantCount = (5 * JitSampleCount) / 1000;
                    foreach (var j in AllJitInvocations)
                    {
                        ulong totalCount = j.JitInstrs();
                        if (totalCount > signficantCount)
                        {
                            if (!printed)
                            {
                                Console.WriteLine();
                                Console.WriteLine("Slow jitting methods (anything taking more than 0.5% of total samples)");
                                printed = true;
                            }
                            Console.WriteLine("{0:00.00%}    {1,-9:G4} {2}", (double)totalCount / TotalSampleCount, totalCount * CountsPerEvent, j.MethodName);
                        }
                    }

                    Console.WriteLine();
                    double totalJitTime = AllJitInvocations.Sum(j => j.JitTime());
                    Console.WriteLine($"Total jit time: {totalJitTime:F2}ms {AllJitInvocations.Count} methods {totalJitTime / AllJitInvocations.Count:F2}ms avg");

                    // Show 10 slowest jit invocations (time, ms)
                    AllJitInvocations.Sort(JitInvocation.MoreJitTime);
                    Console.WriteLine();
                    Console.WriteLine($"Slow jitting methods (time)");
                    int kLimit = 10;
                    for (int k = 0; k < kLimit; k++)
                    {
                        if (k < AllJitInvocations.Count)
                        {
                            JitInvocation j = AllJitInvocations[k];
                            Console.WriteLine($"{j.JitTime(),6:F2} {j.MethodName} starting at {j.InitialTimestamp,6:F2}");
                        }
                    }

                    // Show data on cumulative distribution of jit times.
                    if (AllJitInvocations.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Jit time percentiles");
                        for (int percentile = 10; percentile <= 100; percentile += 10)
                        {
                            int pIndex = (AllJitInvocations.Count * (100 - percentile)) / 100;
                            JitInvocation p = AllJitInvocations[pIndex];
                            Console.WriteLine($"{percentile,3:D}%ile jit time is {p.JitTime():F3}ms");
                        }
                    }


                    // Show assembly inventory
                    // Would be nice to have counts of jitted/prejitted per assembly, order by total number of methods somehow
                    Console.WriteLine();
                    Console.WriteLine("Per Assembly Jitting Details");
                    int totalJitted = 0;
                    foreach (var assemblyId in assemblyInfo.Keys)
                    {
                        AssemblyInfo info = assemblyInfo[assemblyId];

                        bool isNative = (info.Flags & AssemblyFlags.Native) != 0;
                        bool isDynamic = (info.Flags & AssemblyFlags.Dynamic) != 0;

                        info.NumberPrejitted = ImageMap.Where(x => x.Value.AssemblyId == assemblyId && x.Value.IsJitGeneratedCode && !x.Value.IsJittedCode).Count();
                        info.NumberJitted = ImageMap.Where(x => x.Value.AssemblyId == assemblyId && x.Value.IsJitGeneratedCode && x.Value.IsJittedCode).Count();

                        if (info.NumberJitted > 0)
                        {
                            info.TimeJitting = AllJitInvocations.Where(x => x.AssemblyId == assemblyId).Sum(x => x.JitTime());
                        }
                        info.CodegenKind = isNative ? " [NGEN]" : info.NumberPrejitted > 0 ? " [R2R]" : isDynamic ? " [DYNAMIC]" : " [JITTED]";

                        totalJitted += info.NumberJitted;
                    }

                    foreach (var x in assemblyInfo.Values.OrderByDescending(x => x.TimeJitting))
                    {
                        if (x.NumberJitted > 0)
                        {
                            Console.WriteLine($"{x.CodegenKind,10} {x.NumberPrejitted,5} prejitted {x.NumberJitted,5} jitted in {x.TimeJitting,8:F3}ms {100 * x.TimeJitting / totalJitTime,6:F2}% {x.Name} ");
                        }
                    }

                    Console.WriteLine($"{totalJitted,32} jitted in {totalJitTime,8:F3}ms 100.00% --- TOTAL ---");
                }

                if (samplePattern != null)
                {
                    foreach (var i in ImageMap)
                    {
                        if (i.Key.Contains(samplePattern))
                        {
                            ImageInfo info = i.Value;
                            Console.WriteLine("Raw samples for {info.Name} at 0x{info.BaseAddress:X16} -- 0x{info.EndAddress:X16} (length 0x{info.EndAddress - info.BaseAddress}:4X)");

                            foreach (ulong address in SampleCountMap.Keys)
                            {
                                if ((address >= info.BaseAddress) && (address <= info.EndAddress))
                                {
                                    Console.WriteLine("0x{address - info.BaseAddress:4X} : {SampleCountMap[address]}");
                                }
                            }
                        }
                    }
                }
            }

            // Show BenchmarkIntervals
            if (filterToBenchmark && (BenchmarkIntervals.Count > 0))
            {
                double meanInterval = BenchmarkIntervals.Select(x => x.endTimestamp - x.startTimestamp).Average();
                Console.WriteLine();
                Console.WriteLine($"Benchmark: found {BenchmarkIntervals.Count} intervals; mean interval {meanInterval:F3}ms");
                bool showIntervals = false;
                if (showIntervals)
                {
                    int rr = 0;
                    foreach (var x in BenchmarkIntervals)
                    {
                        Console.WriteLine($"{rr++:D3} {x.startTimestamp:F3} -- {x.endTimestamp:F3} : {x.endTimestamp - x.startTimestamp:F3}");
                    }
                }
            }
        }

        static void Usage()
        {
            Console.WriteLine("Summarize profile sample data in an ETL file");
            Console.WriteLine();
            Console.WriteLine("Usage: -- file.etl [-process process-name] [-pid pid] [-show-events] [-show-jit-times] [-benchmark] [-instructions-retired]");
            Console.WriteLine("   -process: defaults to corerun");
            Console.WriteLine("   -pid: choose process to summarize via ID");
            Console.WriteLine("   -benchmark: only count samples made during BechmarkDotNet intervals. Changes default process to dotnet");
            Console.WriteLine("   -show-events: show counts of raw ETL events");
            Console.WriteLine("   -show-jit-times: summarize data on time spent jitting");
            Console.WriteLine("   -show-samples <pattern>: show raw method-relative hits for some methods");
            Console.WriteLine("   -instructions-retired: if ETL has instructions retired events, summarize those instead of profile samples");
        }
    }
}
