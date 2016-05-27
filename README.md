# Dotnet JIT code gen utilities - jitutils

This repo holds a collection of utilities used by RyuJIT developers to 
automate tasks when working on CoreCLR.  Initial utilities are around 
producing diffs of codegen changes.

## Summary

Copy down the appropraite `bootstrap.{cmd|sh}` for your platform and run it.  The script 
will enlist in the jitutils project and perform the basics listed in the details section 
following.

### The details

To prepare the tools for use, from the root of the cloned jitutils repo, do the following: 
 1. Download required package dependencies: `dotnet restore`
    * NOTE: On Mac, you need to first use `ulimit -n 2048` or the `dotnet restore` will fail.
 2. Build and publish the tools: `build.{cmd|sh} -f -p`

This will create the following directories in the repo root:
 1. `bin` - contains one directory for each built tool.
 2. `fx` - contains a set of frameworks assemblies that can be used for asm diffs.

Add each tool subdirectory to your path so you can easily invoke them, e.g.:
```
set jitutils=<path to root of jitutils clone>
set PATH=%PATH%;%jitutils%\bin\jit-dasm;%jitutils%\bin\jit-diff;%jitutils%\bin\jit-analyze
```

For a more complete introduction look at the [getting started guide](doc/getstarted.md).

## jit-dasm

This is a general tool to produce diffs for compiled MSIL assemblies.  The 
tool relies on a base and diff crossgen.exe as inputs, either by using a
prebuilt base from the CI builds and a local experimental build, or 
building both a base and diff locally.

Sample help commandline:
```
    $ jit-dasm --help
    usage: jit-dasm [-b <arg>] [-d <arg>] [-o <arg>] [-t <arg>] [-f <arg>]
                    [--gcinfo] [-v] [-r] [-p <arg>...] [--] <assembly>...

        -b, --base <arg>           The base compiler exe.
        -d, --diff <arg>           The diff compiler exe.
        -o, --output <arg>         The output path.
        -t, --tag <arg>            Name of root in output directory.  Allows
                                   for many sets of output.
        -f, --file <arg>           Name of file to take list of assemblies
                                   from. Both a file and assembly list can
                                   be used.
        --gcinfo                   Add GC info to the disasm output.
        -v, --verbose              Enable verbose output.
        -r, --recursive            Scan directories recursively.
        -p, --platform <arg>...    Path to platform assemblies
        <assembly>...              The list of assemblies or directories to
                                   scan for assemblies.
```

## jit-diff

jit-diff is a tool specifically targeting CoreCLR.  It has a prebaked list of interesting
assemblies to use for generating assembly diffs and understands enough of the structure
to make it more streamlined.  jit-diff uses jit-dasm under the covers to produce the diffs so 
for other projects a new utility could be produced that works in a similar way.

Sample help commandlines:
```
    $ jit-diff --help
    usage: jit-diff <command> [<args>]

        diff       Run asm diff of base/diff.
        list       List defaults and available tools asmdiff.json.
        install    Install tool in config.
```
```
    $ jit-diff diff --help
    usage: jit-diff diff [-b <arg>] [-d <arg>] [-o <arg>] [-a] [-t <arg>]
                    [-m] [-f] [-v] [--core_root <arg>] [--test_root <arg>]

        -b, --base <arg>        The base compiler exe or tag.
        -d, --diff <arg>        The diff compiler exe or tag.
        -o, --output <arg>      The output path.
        -a, --analyze           Analyze resulting base, diff dasm
                                directories.
        -t, --tag <arg>         Name of root in output directory.  Allows
                                for many sets of output.
        -c, --corlibonly        Disasm *corlib only
        -f, --frameworksonly    Disasm frameworks only
        -v, --verbose           Enable verbose output
        --core_root <arg>       Path to test CORE_ROOT.
        --test_root <arg>       Path to test tree.
```

## jit-analyze

The jit-analyze tool understand the format of the *.dasm files in a diff and can extract
this data to produce a summary and/or dump the info to a unified data file (CSV, JSON)
The common usage of the tool is to extract interesting diffs, if any, from a diff run as
part of the development progresses.

Sample help commandline:
```
    $ jit-analyze --help
    usage: jit-analyze [-b <arg>] [-d <arg>] [-r] [-c <arg>] [-w]
                    [--reconcile] [--json <arg>] [--tsv <arg>]

        -b, --base <arg>    Base file or directory.
        -d, --diff <arg>    Diff file or directory.
        -r, --recursive     Search directories recursively.
        -c, --count <arg>   Count of files and methods (at most) to output
                            in the summary. (count) improvements and
                            (count) regressions of each will be included.
                            (default 5)
        -w, --warn           Generate warning output for files/methods that
                            only exists in one dataset or the other (only
                            in base or only in diff).
        --reconcile         If there are methods that exist only in base or
                            diff, create zero-sized counterparts in diff,
                            and vice-versa. Update size deltas accordingly.
        --json <arg>        Dump analysis data to specified file in JSON
                            format.
        --tsv <arg>         Dump analysis data to specified file in
                            tab-separated format.
```

## packages

This is a skeleton project that exists to pull down a predictable set of framework 
assemblies and publish them in the root in the subdirectory './fx'.  Today this is 
set to the RC2 version of the NetCoreApp1.0 frameworks.  When this package is installed 
via the `build.{cmd|sh}` script this set can be used on any supported platform for 
diffing.  Note: The RC2 mscorlib.dll is removed, as this assembly should be updated from 
the selected base runtime that is under test for consistency. To add particular packages 
to the set you diff, add their dependencies to the project.json in this project and 
they will be pulled in and published in the standalone directory './fx'.
