# Assembly diffs

A useful technique when developing the JIT is generating assembly code output with a
baseline compiler as well as with a compiler that has changes -- the "diff compiler"
-- and examining the generated code differences between the two. The tools here
automate that process.

## jit-diff

jit-diff is a tool used to generate asm diffs, specifically targeting CoreCLR.
It has a prebaked list of interesting assemblies to use for generating assembly
diffs and understands enough of the structure of CoreCLR and the dotnet/coreclr
repro to make assembly diff generation streamlined.

jit-diff uses the jit-dasm tool to produce the generated assembly files.

jit-diff has three top-level commands, as shown by the help message:
```
    $ jit-diff --help
    usage: jit-diff <command> [<args>]

        diff       Run asm diff of base/diff.
        list       List defaults and available tools config.json.
        install    Install tool in config.
```

The "jit-diff diff" command has the following help message:
```
    $ jit-diff diff --help
    usage: jit-diff diff [-b <arg>] [-d <arg>] [--crossgen <arg>] [-o <arg>]
                    [-a] [-s] [-t <arg>] [-c] [-f] [--benchmarksonly] [-v]
                    [--core_root <arg>] [--test_root <arg>]

        -b, --base <arg>        The base compiler directory or tag. Will use
                                crossgen or clrjit from this directory,
                                depending on whether --crossgen is
                                specified.
        -d, --diff <arg>        The diff compiler directory or tag. Will use
                                crossgen or clrjit from this directory,
                                depending on whether --crossgen is
                                specified.
        --crossgen <arg>        The crossgen compiler exe. When this is
                                specified, will use clrjit from the --base
                                and --diff directories with this crossgen
        -o, --output <arg>      The output path.
        -a, --analyze           Analyze resulting base, diff dasm
                                directories.
        -s, --sequential        Run sequentially; don't do parallel
                                compiles.
        -t, --tag <arg>         Name of root in output directory.  Allows
                                for many sets of output.
        -c, --corlibonly        Disasm *CorLib only
        -f, --frameworksonly    Disasm frameworks only
        --benchmarksonly        Disasm core benchmarks only
        -v, --verbose           Enable verbose output
        --core_root <arg>       Path to test CORE_ROOT.
        --test_root <arg>       Path to test tree.
```

The "jit-diff list" command has this help message:
```
    $ jit-diff diff --help
    usage: jit-diff list [-v]

        -v, --verbose    Enable verbose output
```

The "jit-diff install" command has this help message:
```
    $ jit-diff install --help
    usage: jit-diff install [-j <arg>] [-n <arg>] [-l] [-b <arg>] [-v]

        -j, --job <arg>          Name of the job.
        -n, --number <arg>       Job number.
        -l, --last_successful    Last successful build.
        -b, --branch <arg>       Name of branch.
        -v, --verbose            Enable verbose output
```

Sample usage, to create diffs:
```
    c:\gh\coreclr> jit-diff diff -o c:\diffs -t 5 -a -f --core_root c:\gh\coreclr\bin\tests\Windows_NT.x86.checked\Tests\Core_Root -b e:\gh\coreclr2\bin\Product\Windows_NT.x86.checked -d c:\gh\coreclr\bin\Product\Windows_NT.x86.checked
```

Explanation:
1. `-o c:\diffs` -- specify the root directory where diffs will be placed.
2. `-t 5` -- give the diffs a "tag". Thus, the actual root directory will be `c:\diffs\5`.
3. `-a` -- run jit-analyze on the resultant diffs.
4. `-f` -- generate diffs over the framework assemblies (a well-known list built in to jit-diff).
5. `--core_root` -- specify the `CORE_ROOT` directory (the "test layout").
6. `-b` -- specify the directory in which a baseline crossgen.exe can be found.
7. `-d` -- specify the directory in which a diff (experimental) crossgen.exe can be found.

Note: you create the `CORE_ROOT` directory "layout" by running the runtest script.
On Windows, this can be created by running
```
    tests\runtest.cmd
```
or
```
    tests\runtest.cmd GenerateLayoutOnly
```
in the dotnet/coreclr repo. On non-Windows, consult the test instructions
[here](https://github.com/dotnet/coreclr/blob/master/Documentation/building/unix-test-instructions.md).
Note that you can pass `--testDir=NONE` to runtest.sh to get the
same effect as passing `GenerateLayoutOnly` to runtest.cmd on Windows.

## jit-analyze

The jit-analyze tool understands the format of the `*.dasm` files in a diff and can extract
this data to produce a summary and/or dump the info to a data file (in CSV or JSON format).
The common usage of the tool is to extract interesting diffs, if any, from a diff run as
part of the development progresses.

This tool can be invoked automatically after generating diffs using the `-a` option to `jit-diff diff`.

Sample help command line:
```
    $ jit-analyze --help
    usage: jit-analyze [-b <arg>] [-d <arg>] [-r] [-c <arg>] [-w]
                       [--json <arg>] [--tsv <arg>]

        -b, --base <arg>     Base file or directory.
        -d, --diff <arg>     Diff file or directory.
        -r, --recursive      Search directories recursively.
        -c, --count <arg>    Count of files and methods (at most) to output
                             in the summary. (count) improvements and
                             (count) regressions of each will be included.
                             (default 5)
        -w, --warn           Generate warning output for files/methods that
                             only exists in one dataset or the other (only
                             in base or only in diff).
        --json <arg>         Dump analysis data to specified file in JSON
                             format.
        --tsv <arg>          Dump analysis data to specified file in
                             tab-separated format.
```

## jit-dasm

This is a general tool to produce assembly output for compiled MSIL assemblies.
The tool relies on crossgen.exe as input, either by using a
prebuilt base from the CI builds, or building it locally.

Sample help command line:
```
    $ jit-dasm --help
    usage: jit-dasm [-c <arg>] [-j <arg>] [-o <arg>] [-t <arg>] [-f <arg>]
                    [--gcinfo] [-v] [-r] [-p <arg>...] [--] <assembly>...

        -c, --crossgen <arg>       The crossgen compiler exe.
        -j, --jit <arg>            The full path to the jit library.
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

## packages

This is a skeleton project that exists to pull down a predictable set of framework 
assemblies and publish them in the root in the subdirectory './fx'.  Today this is 
set to the NetCoreApp1.0 frameworks.  When this package is installed 
via the `build.{cmd|sh}` script this set can be used on any supported platform for 
diffing.  Note: The mscorlib.dll is removed, as this assembly should be updated from 
the selected base runtime that is under test, for consistency. To add particular packages 
to the set you diff, add their dependencies to the project.json in this project and 
they will be pulled in and published in the standalone directory './fx'.
