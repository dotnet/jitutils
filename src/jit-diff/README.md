# jit-diff - Diff CoreCLR tree

jit-diff is a utility to produce diffs from a CoreCLR test layout via
the jit-dasm tool.

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Do 'dotnet restore' to create lock file and 
  pull down required packages.
* Issue a 'dotnet build' command.  This will create a mcgdiff.dll in the bin
  directory that you can use to drive creation of diffs.
* Ensure that jit-dasm is on your path.  (See jit-dasm README.md for details
  on how to build)
* invoke jit-diff.exe --frameworks --base `<base crossgen>` --diff `<diff crossgen>` 
  --coreroot `<path to core_root>` --testroot `<path to test_root>`
* jit-diff can be installed by running the project build script in the root of this repo 
via

``` 
    $ ./build.{cmd|sh} -p
```
