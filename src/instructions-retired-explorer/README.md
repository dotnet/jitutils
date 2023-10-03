### Instructions Retired Explorer

Instructions Retired Explorer is a tool to parse ETW files like those produced by BenchmarkDotNet (aka BDN) (via `-p ETW`) or PerfView.

It understands profile, jit, and method events, and can attribute profile or PMU
samples to jitted code.

It also understands BDN's profiling events, and can filter profiles to just those taken when BDN is actively measuring performance (that is, it will ignore the various warmup and overhead phases, as well as time spent within BDN itself).

### Usage

```
dotnet run -- file.etl [-process process-name] [-pid pid] [-show-events] [-show-jit-times] [-benchmark] [-instructions-retired]

-process: defaults to corerun
-pid: choose process to summarize via ID
-benchmark: only count samples made during BechmarkDotNet intervals. Changes default process to dotnet
-show-events: show counts of raw ETL events
-show-jit-times: summarize data on time spent jitting
-show-samples <pattern>: show raw method-relative hits for some methods
-instructions-retired: if ETL has instructions retired events, summarize those instead of profile samples
```

### Sample Output

This shows some output from analyzing a BDN produced file, with `-benchmark`:

```
Samples for corerun: 6830 events for Benchmark Intervals
Jitting           : 01.66% 6.4E+05  samples 1507 methods
  JitInterface    : 00.78% 3E+05    samples
Jit-generated code: 83.95% 3.23E+07 samples
  Jitted code     : 83.95% 3.23E+07 samples
  MinOpts code    : 00.00% 0        samples
  FullOpts code   : 00.81% 3.1E+05  samples
  Tier-0 code     : 00.00% 0        samples
  Tier-1 code     : 83.14% 3.2E+07  samples
  R2R code        : 00.00% 0        samples

02.13%   8.2E+05     ?        Unknown
42.38%   1.629E+07   Tier-1   [System.Private.CoreLib]DateTimeFormat.FormatCustomized(value class System.DateTime,value class System.ReadOnlySpan`1<wchar>,class System.Globalization.DateTimeFormatInfo,value class System.TimeSpan,value class System.Collections.Generic.ValueListBuilder`1<!!0>&)
19.30%   7.42E+06    Tier-1   [System.Private.CoreLib]DateTimeFormat.FormatDigits(value class System.Collections.Generic.ValueListBuilder`1<!!0>&,int32,int32)
11.81%   4.54E+06    native   coreclr.dll
09.26%   3.56E+06    Tier-1   [System.Private.CoreLib]DateTimeFormat.Format(value class System.DateTime,class System.String,class System.IFormatProvider,value class System.TimeSpan)
04.37%   1.68E+06    Tier-1   [System.Private.CoreLib]System.Collections.Generic.ValueListBuilder`1[System.Char].AppendMultiChar(value class System.ReadOnlySpan`1<!0>)
03.23%   1.24E+06    Tier-1   [System.Private.CoreLib]Buffer.Memmove(unsigned int8&,unsigned int8&,unsigned int)
01.61%   6.2E+05     Tier-1   [System.Private.CoreLib]System.ReadOnlySpan`1[System.Char].ToString()
01.27%   4.9E+05     Tier-1   [System.Private.CoreLib]String.Ctor(value class System.ReadOnlySpan`1<wchar>)
01.17%   4.5E+05     Tier-1   [System.Private.CoreLib]DateTimeFormat.ExpandStandardFormatToCustomPattern(wchar,class System.Globalization.DateTimeFormatInfo)
00.88%   3.4E+05     native   clrjit.dll
00.81%   3.1E+05     FullOpt  [d0c2a6e2-c859-4adf-aa32-e1950c899716]Runnable_0.WorkloadActionUnroll(int64)
00.62%   2.4E+05     native   ntoskrnl.exe
00.55%   2.1E+05     Tier-1   [MicroBenchmarks]Perf_DateTime.ToString(class System.String)
00.39%   1.5E+05     native   ntdll.dll
00.13%   5E+04       native   intelppm.sys
00.05%   2E+04       native   KernelBase.dll

Benchmark: found 15 intervals; mean interval 252.122ms
000 3243.972 -- 3506.304 : 262.332
001 3508.081 -- 3766.636 : 258.554
002 3768.304 -- 4027.688 : 259.384
003 4029.104 -- 4275.982 : 246.878
004 4277.706 -- 4529.997 : 252.291
005 4531.510 -- 4781.650 : 250.140
006 4783.191 -- 5032.090 : 248.899
007 5033.857 -- 5283.478 : 249.621
008 5285.356 -- 5538.937 : 253.581
009 5540.676 -- 5791.375 : 250.699
010 5792.768 -- 6044.684 : 251.916
011 6046.395 -- 6295.090 : 248.694
012 6296.746 -- 6547.423 : 250.677
013 6549.081 -- 6796.750 : 247.669
014 6798.383 -- 7048.879 : 250.496
```



