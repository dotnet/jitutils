# jit-analyze - Managed CodeGen difference analysis tool

jit-analyze is a utility to provide feedback on generated disassembly.
The tool will produce the total bytes of difference and list of files 
and methods sorted by contribution (size in bytes of regression/improvement)

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Follow publish directions for the jitutils repo in the root.  This will 
  put the tools on your path.
* Generate corediff disasm run.  See [Getting Started](../../doc/getstarted.md) 
  for directions how.
* Run analyze --base `<base path>` --diff `<diff path>` to produce a summary of the 
  differences.

## Large disassembly sets

Directory analysis parses and compares independent file pairs using PLINQ's default
parallelism, based on the available processor count. Each worker
releases unchanged method data after comparing a pair, and reuses parsed methods
across requested metrics. Byte-identical pairs only need one parse. Instruction
lines are scanned using a reusable, reader-owned buffer rather than allocated as
individual strings. The buffer starts at 32K characters and grows with `Array.Resize`
when needed for longer lines.

Textual diff analysis remains enabled by default. It uses the same default
parallelism and reuses counts across metrics. Git checks each matched pair directly,
without a redundant byte comparison before invoking it.
It requests detailed line counts only for files eligible for the text-only report;
other files use Git's difference-only query and still contribute to the changed-file count.
Git's added/deleted line counts are retained, including binary-file handling.
The text-only summary lists files whose text changed but whose metrics did not.
Directory symlinks are compared as links rather than followed.

To measure the complete analysis on Linux, build Release and run, for example:

```sh
dotnet build src/jit-analyze -c Release
/usr/bin/time -v src/jit-analyze/bin/Release/net10.0/jit-analyze \
    --base /path/to/main --diff /path/to/pr --recursive --count 100
```

The analyzer returns a nonzero exit code when metrics differ; this does not mean
the run failed. Compare repeated runs on the same files and machine, and retain
textual diffs when measuring end-to-end performance. `--skip-text-diff` can isolate
metric analysis, but measures a different workload. GNU time reports peak resident
memory for a process, not the simultaneous sum of all Git worker processes.

### Reference benchmark

The assembly artifacts from [MihuBot/runtime-utils#2148](https://github.com/MihuBot/runtime-utils/issues/2148)
contain 760 `.dasm` files per side, totaling 25,501,489,811 bytes. The files were
flattened by filename, matching the runner's combined assembly directories, and
analyzed with `-b main -d pr -r -c 100`, with textual diffs enabled.

On a 16-logical-processor Linux machine with 31 GiB RAM, using a Release build
with .NET SDK 10.0.111 / runtime 10.0.11:

| Version | Wall time, three runs | Median wall time | Median peak RSS |
| --- | --- | --- | --- |
| Original (`e718415`) | 229.39, 229.38, 247.92 s | 229.39 s | 13.30 GiB |
| First optimization series (`f261f89`) | 35.33, 31.87, 32.28 s | 32.28 s | 1.46 GiB |
| Demand-driven text counts (`e80f9a4`) | 15.16, 13.77, 13.55 s | 13.77 s | 0.79 GiB |

This is a **16.7x median speedup** and **94% lower peak RSS**. The independently
measured optimization steps were:

| Commit | Change | Full-run wall time |
| --- | --- | --- |
| `27ea778` | Materialize comparisons instead of rebuilding deferred queries | 177.20 s |
| `b973865` | Parse and compare bounded parallel file pairs | 132.67 s |
| `bc47d72` | Scan instruction lines with pooled buffers | 111.59 s |
| `bcb13f8` | Aggregate metrics while parsing with generated regexes | 104.14 s |
| `c882924` | Store compact metric values and eliminate copies | 71.34 s |
| `bb910ad` | Parallelize textual diffs and skip identical inputs | 41.65 s |
| `a816f75` | Reuse parsed methods for identical file pairs | 32.74 s |
| `e80f9a4` | Request numstat only for files eligible for the text-only report | 15.16 s |

Per-file profiling found that Git spent about 20 seconds producing unused numstat
data for `KubernetesClient.dasm`, while its difference-only query took less than
0.01 seconds. Avoiding unused counts retains Git's change detection and the complete
report, without replacing line counts that are actually displayed.

The metric reports agree with the original analyzer and the job's published
totals. The optimized report additionally displays 106 text-only files that the
original omitted because it looked up relative names in an absolute-path dictionary.
The displayed line counts agree with a directory-level Git comparison.

### Default-parallelism comparison

After removing the eight-worker cap from both analysis phases, fresh runs compared
the capped executable against PLINQ's default parallelism on the same
16-logical-processor machine and full artifact workload. One warm-up run per
variant was excluded; the three measured runs were interleaved with alternating order.

| Parallelism | Wall time, three runs | Median wall time | Median peak RSS |
| --- | --- | --- | --- |
| Eight-worker cap (`e80f9a4`) | 15.43, 13.93, 13.91 s | 13.93 s | 0.86 GiB |
| PLINQ default | 14.34, 14.01, 16.51 s | 14.34 s | 1.12 GiB |

Default parallelism did not improve this workload in these runs: the median was
about 3% slower and peak RSS was about 31% higher. The run ranges overlap, so this
small timing difference should not be interpreted as a precisely established cost.
Both phases now use PLINQ's default rather than an application-specific cap.

The output of analyze looks like the following:
```
$ jit-analyze --base ~/Work/output/base --diff ~/Work/output/diff

(Note: Lower is better)

Total bytes of diff: -4124
    diff is an improvement.

Top file regressions by size (bytes):
    193 : Microsoft.CodeAnalysis.dasm
    154 : System.Dynamic.Runtime.dasm
    60 : System.IO.Compression.dasm
    43 : System.Net.Security.dasm
    43 : System.Xml.ReaderWriter.dasm

Top file improvements by size (bytes):
    -1804 : mscorlib.dasm
    -1532 : Microsoft.CodeAnalysis.CSharp.dasm
    -726 : System.Xml.XmlDocument.dasm
    -284 : System.Linq.Expressions.dasm
    -239 : System.Net.Http.dasm

21 total files with diffs.

Top method regressions by size (bytes):
    328 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.DocumentationCommentXmlTokens:.cctor()
    266 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.MethodTypeInferrer:Fix(int,byref):bool:this
    194 : mscorlib.dasm - System.DefaultBinder:BindToMethod(int,ref,byref,ref,ref,ref,byref):ref:this
    187 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.LanguageParser:ParseModifiers(ref):this
    163 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Symbols.SourceAssemblySymbol:DecodeWellKnownAttribute(byref,int,bool):this

Top method improvements by size (bytes):
    -160 : System.Xml.XmlDocument.dasm - System.Xml.XmlTextWriter:AutoComplete(int):this
    -124 : System.Xml.XmlDocument.dasm - System.Xml.XmlTextWriter:WriteEndStartTag(bool):this
    -110 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.MemberSemanticModel:GetEnclosingBinder(ref,int):ref:this
    -95 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.CSharpDataFlowAnalysis:AnalyzeReadWrite():this
    -85 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.LanguageParser:ParseForStatement():ref:this

3762 total methods with diffs
```