# jit-dasm - Managed CodeGen Dasm Generator

jit-dasm is a utility to drive the dotnet crossgen tool to produce
binary disassembly from the JIT compiler.  This can be used to create
diffs to check ongoing development.

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Do 'dotnet restore' to create lock file and 
  pull down required packages.
* Issue a 'dotnet build' command.  This will create a jit-dasm.exe in the bin
  directory that you can use to drive creation of diffs.
* jit-dasm can be installed by running the project build script in the root of this repo 
via

``` 
    $ ./build.{cmd|sh} -p
```