# JIT source code formatting

JIT source code is automatically formatted by the jit-format tool.
The idea is to automatically enforce the
[CLR JIT Coding Conventions](https://github.com/dotnet/coreclr/blob/master/Documentation/coding-guidelines/clr-jit-coding-conventions.md)
where possible, although the tool by its nature ends up defining the
coding conventions by the formatting it enforces. The tool invokes clang-format and clang-tidy
to do its work.

## jit-format

The jit-format tool runs over all jit source, or specific files if they are specified.
It can either tell the user that code needs to be reformatted or it
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

## Using jit-format

A common task for developers is to format their changes before submitting a GitHub pull request.
A developer can run the tool using the tests/scripts/format.py script in the coreclr repo:

```
python tests\scripts\format.py --coreclr C:\gh\coreclr --arch x64 --os Windows_NT
```

This will run all build flavors and all projects for the user. This should be done on both
Windows and Linux.

A developer can also run the tool manually:

```
jit-format -c c:\gh\coreclr -f
```

or, specifically passing architecture, build type, and operating system:

```
jit-format -a x64 -b Debug -o Windows -c C:\gh\coreclr -f
```

This will run both clang-tidy and clang-format on all of the jit code and fix all the
formatting to match the rules.

Often a developer is only interested in a few files. For example,
you can invoke jit-format as follows to just examine lower.cpp and codegenxarch.cpp:

```
jit-format -a x64 -b Debug -o Windows -c C:\gh\coreclr -v lower.cpp codegenxarch.cpp

Formatting jit directory.
Formatting dll project.
Building compile_commands.json.
Using compile_commands.json found at C:\gh\coreclr\bin\obj\Windows_NT.x64.Checked\compile_commands.json
Running clang-tidy.
Running:
        clang-tidy   -checks=-*,readability-braces*,modernize-use-nullptr -header-filter=.* -p C:\gh\coreclr\bin\obj\Windows_NT.x64.Checked\compile_commands.json C:\gh\coreclr\src\jit\codegenxarch.cpp

Running:
        clang-tidy   -checks=-*,readability-braces*,modernize-use-nullptr -header-filter=.* -p C:\gh\coreclr\bin\obj\Windows_NT.x64.Checked\compile_commands.json C:\gh\coreclr\src\jit\lower.cpp

Running: clang-format   C:\gh\coreclr\src\jit\codegenxarch.cpp
Running: clang-format   C:\gh\coreclr\src\jit\lower.cpp
```

jit-format runs over only those files. The verbose flag shows the user what
clang-tidy and clang-format invocations are being executed.

When the developer is ready to check in, they will want to make sure they fix any formatting
errors that clang-tidy and clang-format identified. This can be done with the --fix flag:

```
jit-format -a x64 -b Debug -o Windows -c C:\gh\coreclr -v --fix lower.cpp codegenxarch.cpp

Formatting jit directory.
Formatting dll project.
Building compile_commands.json.
Using compile_commands.json found at C:\gh\coreclr\bin\obj\Windows_NT.x64.Checked\compile_commands.json
Running clang-tidy.
Running:
        clang-tidy -fix  -checks=-*,readability-braces*,modernize-use-nullptr -header-filter=.* -p C:\gh\coreclr\bin\obj\Windows_NT.x64.Checked\compile_commands.json C:\gh\coreclr\src\jit\codegenxarch.cpp

Running:
        clang-tidy -fix  -checks=-*,readability-braces*,modernize-use-nullptr -header-filter=.* -p C:\gh\coreclr\bin\obj\Windows_NT.x64.Checked\compile_commands.json C:\gh\coreclr\src\jit\lower.cpp

Running: clang-format -i  C:\gh\coreclr\src\jit\codegenxarch.cpp
Running: clang-format -i  C:\gh\coreclr\src\jit\lower.cpp
```

The developer may also only be interested in only running clang-tidy or clang-format without
running the other tool. This can be done by using the --noformat (no clang-format) or
--untidy (no clang-tidy) flags.

Finally, the developer can pass their own compile_commands.json database to jit-format
if they already have one built:

```
jit-format.cmd --fix --noformat --compile-commands C:\gh\jitutils\test\jit-format\compile_commands.json --coreclr C:\gh\jitutils\test\jit-format C:\gh\jitutils\test\jit-format\test.cpp
Formatting jit directory.
Formatting dll project.
Using compile_commands.json found at C:\gh\jitutils\test\jit-format\compile_commands.json
Running clang-tidy.
Running:
        clang-tidy -fix  -checks=-*,readability-braces*,modernize-use-nullptr -header-filter=.* -p C:\gh\jitutils\test\jit-format\compile_commands.json C:\gh\jitutils\test\jit-format\test.cpp
```

## clang-format and clang-tidy

jit-format uses clang-format and clang-tidy to do its work.

Currently, clang-tidy will run the `modernize-use-nullptr` and `readability-braces`
checks. Clang-format will use the `.clang-format` specification found in the jit directory.
A summary of what each of the options does can be found
[here](http://llvm.org/releases/3.8.0/tools/clang/docs/ClangFormatStyleOptions.html).

Because jit-format will build a `compile_commands.json` database from the build log on Windows,
developers must do a full build of coreclr before running jit-format.

## Limitations

The clang-format and clang-tidy tools cannot enforce all the rules specified in the CLR JIT Coding Conventions.
Developers still need to be aware of these conventions and follow the parts that can't be automatically
enforced.

Clang-format and clang-tidy have bugs and limitations to their enforcement. One particular example: a comment
line followed by an `#ifdef`, typically placed at the leftmost column, will cause the comment line to be also
aligned to the leftmost column. To avoid this, the JIT sources introduced the `CLANG_FORMAT_COMMENT_ANCHOR`
macro to prevent this from happening, used as follows:

```
        // Some kind of comment
        CLANG_FORMAT_COMMENT_ANCHOR;

#ifdef SOME_KIND_OF_IFDEF
```

## Configuring defaults

See the document [configuring defaults](config.md) for details on setting up a set of default configurations.
