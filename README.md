# Dotnet JIT code gen utilities - jitutils

This repo holds a collection of utilities used by RyuJIT developers to 
automate tasks when working on CoreCLR.

## Summary

Current tools include:

1. [Assembly diffs](doc/diffs.md): jit-diff, jit-dasm, jit-dasm-pmi, jit-analyze, jit-tp-analyze.
2. [CI jobs information](doc/cijobs.md): cijobs.
3. [JIT source code formatting](doc/formatting.md): jit-format.
4. [General tools](doc/tools.md): pmi
5. [Experimental tools](src/performance-explorer/README.md): performance-explorer
6. [BenchmarkDotNet Analysis](src/instructions-retired-explorer/README.md)


## Getting started

1. Clone the jitutils repo:
```
    git clone https://github.com/dotnet/jitutils
```

2. Install a recent .NET Core SDK (including the `dotnet` command-line interface, or CLI) from [here](https://dot.net).

3. Build the tools:
```
    cd jitutils
    bootstrap.cmd
```
(on non-Windows, run bootstrap.sh.
**macOS note:** On macOS versions **prior to 14 (Sonoma)** you must first run `ulimit -n 2048`, otherwise the `dotnet restore` part of the build may fail due to the lower default file descriptor limit.)

4. Optionally, add the built tools directory to your path, e.g.:
```
    set PATH=%PATH%;<root>\jitutils\bin
```
