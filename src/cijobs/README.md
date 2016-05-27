# cijobs - List or copy job info/artifacts from the CI to a local machine 

Continuous integration build jobs tool enables the listing of
jobs built in the CI system as well as downloading their artifacts. This
functionality allows for the speed up of some common dev tasks but taking
advantage of work being done in the cloud.

###Scenario 1: Start new work. 
When beginning a new set of changes, listing 
job status can help you find a commit to start your work from. The tool
answers questions like "are the CentOS build jobs passing?" and "what was
the commit hash for the last successful tizen arm32 build?"

###Scenario 2: Copy artifacts to speed up development flow. The tool enables
developers to download builds from the cloud so that developers can avoid 
rebuilding baseline tools on a local machine.  Need the crossgen tool for 
the baseline commit for OSX diffs?  Cijobs makes this easy to copy to your
system.

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Do 'dotnet restore' to create lock file and 
  pull down required packages.
* Issue a 'dotnet build' command.  Build artifacts will be generated under 
  a local 'bin' dir.
* cijobs is included in the `build.{cmd|sh}` in the root. Building the whole 
  repo will install the tool in addition to the other diff utilities in a 
  bin directory.
