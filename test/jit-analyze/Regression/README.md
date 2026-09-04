# jit-analyze regression tests

Run from the repository root with the .NET SDK required by `src/Directory.Build.props`:

```sh
dotnet run --project test/jit-analyze/Regression -c Release
```

This small console test links the real analyzer sources, including its command-line
parser, and reuses its existing build properties/dependency. It adds no test packages
or production test hooks. Failures return a nonzero exit code. Generated tiny fixtures
live under the test output directory and are removed even on failure.

Optionally compare all CLI stdout, TSV and exit codes against an older executable:

```sh
dotnet run --project test/jit-analyze/Regression -c Release -- /path/to/baseline/jit-analyze
```

Only CRLF output endings are normalized; numeric formatting is invariant. Normal
runs use explicit assertions, not a baseline executable or the historical
`../baseline*.out` goldens. Baseline comparisons disable textual git diffs to isolate
metric analysis; separate tests exercise Git and the text-only report.

Coverage includes all 12 metrics; repeated/Unicode method names; absent optional
metrics; both perf-score spellings; zero-byte records; debug info; concatenated-file
offsets; LF, CRLF and CR; missing final newlines; empty files; long selected and
ignored lines; and UTF-8/CRLF around 64 KiB boundaries. CLI tests exercise reconciliation,
warnings, filtering, multiple metrics, TSV, concatenation and unequal single filenames.
Text-diff tests cover unchanged files, binary files, long files, nested paths, spaces,
Unix tabs/newlines in paths, dangling links and directory links without traversal.

Intentionally preserved behavior:

* Offsets are zero-based across all input files, but offset zero is omitted.
* Zero-byte summaries count as functions as well as assembly listings.
* Debug records without method names aggregate into the empty-name group and
  contribute offsets/function counts.
* Extra allocation bytes are computed after grouping. Missing allocation means zero
  extra bytes; integer-only spill/resolution weights do not match.
* Explicit files compare despite different names. Unmatched directory files are
  warned about, not reconciled; concatenation instead treats them as one logical file.
* Each selected metric appends a complete TSV header and its own selected method
  rows. Reconciled rows appear even when that selected metric is zero. TSV uses the
  base filename, fractional percentages (zero for zero bases), and a trailing tab.
* Each metric with a nonzero total delta contributes `-1` to the exit code.
