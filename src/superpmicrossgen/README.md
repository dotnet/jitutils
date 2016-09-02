# superpmi - Managed compilation collection and replay.

Superpmi is a utility to drive the dotnet crossgen tool to produce
a replay-able database of JIT/Runtime interface queries from a base JIT
compilation.  This database can be used to quickly redo compilation for a
set of methods that avoids the overhead of the runtime.

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Do 'dotnet restore' to create lock file and 
  pull down required packages.
* Issue a 'dotnet build' command.  This will create a superpmi.dll in the bin
  directory that you can use to collect or replay compilation.
* superpmi can be installed by running the project build script in the root of this repo 
via

``` 
    $ ./build.{cmd|sh} -p
```