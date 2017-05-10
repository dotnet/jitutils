# Dotnet JIT code gen utilities - jitutils

This repo holds a collection of utilities used by RyuJIT developers to 
automate tasks when working on CoreCLR.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/)
or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Summary

Current tools include:

1. [Assembly diffs](doc/diffs.md): jit-diff, jit-dasm, jit-analyze.
2. [CI jobs information](doc/cijobs.md): cijobs.
2. [JIT source code formatting](doc/formatting.md): jit-format.

## Getting started

1. Clone the jitutils repo:
```
    git clone https://github.com/dotnet/jitutils
```

2. Install the .NET Core SDK (including the `dotnet` command-line interface, or CLI) from [here](https://dot.net).

3. Build the tools:
```
    cd jitutils
    bootstrap.cmd
```
(on non-Windows, run bootstrap.sh. NOTE: On Mac, you need to first use `ulimit -n 2048` or the `dotnet restore` part of the build will fail.)

4. Optionally, add the built tools directory to your path, e.g.:
```
    set PATH=%PATH%;<root>\jitutils\bin
```
