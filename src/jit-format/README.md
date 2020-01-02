# jit-format - Code Formatting Tool for JIT Source

jit-format is a utility to maintain formatting standards in jit source.
The tool will analyze the code for formatting errors using clang-tidy
and clang-format, and potentially fix errors.

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Do 'dotnet restore' to create lock file and 
  pull down required packages.
* Issue a 'dotnet build' command.  This will create a jit-format in the bin
  directory that you can use to check the formatting of your changes.
* Invoke jit-format -a `<arch>` -b `<build>` -p `<platform>` 
  --coreclr `<path to runtime root>`
* jit-format can be installed by running the project build script in the root of this repo 
via

``` 
    $ ./build.{cmd|sh} -p
```

