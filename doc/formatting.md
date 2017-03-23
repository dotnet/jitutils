# JIT source code formatting

JIT source code is automatically formatted by the jit-format tool, which drives invocation of the
clang-format and clang-tidy tools.

## jit-format

The jit-format tool runs clang-format and clang-tidy over all jit source, or specific files
if they are specified. It can either tell the user that code needs to be reformatted or it
can fix the source itself.

Sample help command line
```
    $ jit-format --help
    usage: jit-format [-a <arg>] [-o <arg>] [-b <arg>] [-c <arg>]
                      [--compile-commands <arg>] [-v] [--untidy]
                      [--noformat] [-f] [-i] [--projects <arg>...] [--]
                      <filenames>...

        -a, --arch <arg>            The architecture of the build (options:
                                    x64, x86)
        -o, --os <arg>              The operating system of the build
                                    (options: Windows, OSX, Ubuntu, Fedora,
                                    etc.)
        -b, --build <arg>           The build type of the build (options:
                                    Release, Checked, Debug)
        -c, --coreclr <arg>         Full path to base coreclr directory
        --compile-commands <arg>    Full path to compile_commands.json
        -v, --verbose               Enable verbose output.
        --untidy                    Do not run clang-tidy
        --noformat                  Do not run clang-format
        -f, --fix                   Fix formatting errors discovered by
                                    clang-format and clang-tidy.
        -i, --ignore-errors         Ignore clang-tidy errors
        --projects <arg>...         List of build projects clang-tidy should
                                    consider (e.g. dll, standalone,
                                    protojit, etc.). Default: dll
        <filenames>...              Optional list of files that should be
                                    formatted.
```

Sample usage:
```
   c:\gh\coreclr> jit-format -c c:\gh\coreclr -f
```
