# Quick start guide to running diffs in a CoreCLR tree across multiple platforms.

## Assumptions

This guide assumes that you have built a CoreCLR and have produced a crossgen 
executable and mscorlib.dll assembly. See the [CoreCLR](https://github.com/dotnet/coreclr) 
GitHub repo for directions on building.

## Dependencies

* dotnet cli - All the utilities in the repo rely on dotnet cli for packages and building.  
  The `dotnet` tool needs to be on the path. You can find information on installing dotnet cli
  on the official product page at http://dotnet.github.io/ or in the [dotnet cli GitHub repo](https://github.com/dotnet/cli)
  (where you can install more recent, "daily" builds). If installing from the GitHub repo packages,
  be sure to install the ".NET Core SDK" package, which includes the command-line tool.
  Note: jitutils require a dotnet cli version after the pre-V1 RC1 build (version?)
  since that build does not include all the required features.
* git - The jit-analyze tool uses `git diff` to check for textual differences since this is
  consistent across platforms, and fast.

## Tools included in the repo

* jit-dasm - Produce `*.dasm` from baseline/diff tools for an assembly or set of assemblies.
* cijobs - List builds by job from the Jenkins CI for dotnet coreclr and enable downloads of
  build artifacts from the cloud.  These tools can be used in base/diff comparisons so 
  developers can avoid replicating these builds on local machines.
* jit-analyze - Compare and analyze `*.dasm` files from baseline/diff.  Produces a report on diffs,
  total size regression/improvement, and size regression/improvement by file and method.
* jit-diff - Driver tool that implements a common dev flow.  Includes a configuration file 
  for common defaults, and implements directory scheme for "installing" tools for use later.

## Build the tools

A `bootstrap.{cmd,sh}` script is provided in the root which will validate all tool depenedencies, 
build the repo, pubish the resulting bins, and place them on the path.  This can be run to setup 
the developer in one shot.

To build jitutils using the build script in the root of the repo: `build.{cmd,sh}`. By 
default the script just builds the tools and does not publish them in a separate directory. 
To publish the utilities add the '-p' flag which publishes each utility as a standalone app 
in a directory under ./bin in the root of the repo.  Additionally, to download the default set 
of framework assemblies that can be used for generating asm diffs, add '-f'.

```
 $ ./build.sh -h

build.sh [-b <BUILD TYPE>] [-f] [-h] [-p]

    -b <BUILD TYPE> : Build type, can be Debug or Release.
    -h              : Show this message.
    -f              : Install default framework directory in <script_root>/fx.
    -p              : Publish utilities.
```

## 50,000 foot view

In this repo there is a tool to produce dasm output from the JIT (jit-dasm), one to analyze results 
(jit-analyze), one to find and copy down tool drops from the CI, and a driver to pull them all together 
to make a diff run.  With this base functionality most common dev flows can be implemented.  These 
will enable you to evaluate and describe the effect of your changes across significant inputs.
The first, jit-dasm, is the tool that knows how to generate assembly code into 
a `*.dasm` file.  It's intended to be simple.  It takes a base and/or diff crossgen and drives it to 
produce a `*.dasm` file on the specified output path.  jit-dasm doesn't have any internal knowledge 
of frameworks, file names or directory names, rather it is a low level tool for generating 
disassembly output.  jit-diff, the driver, on the other hand knows about interesting frameworks 
to generate output for, understands the structure of the built test tree in CoreCLR, knows where 
the different toolsets are kept, and generally holds the "how", or the policy part, of a diff run.  
With this context, jit-diff drives the jit-dasm tool to make an output a particular directory 
structure for coreclr.  With this in mind what follows is an outline of a few ways to generate 
diffs for CoreCLR using the jitutils. This is a tactical approach and it tries to avoid extraneous 
discussion of internals.
A note on defaults: jit-diff understands an environment variable, `JIT_DASM_ROOT`, that refers to a 
directory that contains a default config file in the json format.  This location, and the defaults in 
the config file can be used to simplify the command lines that are outlined below.  For the purposes 
of introduction the full command lines are shown below, but in the configuration section there is a 
discussion of how to include the defaults to simplify the command lines and the overall dev flow.

Basic jit-diff commands:
```
$ jit-diff --help
usage: jit-diff <command> [<args>]

    diff       Run asm diff of base/diff.
    list       List defaults and available tools asmdiff.json.
    install    Install tool in config.

```

In the following scenarios we will be focusing on the diff command.  The list and install commands are 
outlined in the configuration section.

## Producing diffs for CoreCLR

Today there are two scenarios within CoreCLR depending on platform.  This is largely a function 
of building the tests and Windows is further ahead here.  Today you have to consume the results 
of a Windows test build on Linux and OSX to run tests and the set up can be involved.  (See 
CoreCLR repo unix test instructions 
[here](https://github.com/dotnet/coreclr/blob/master/Documentation/building/unix-test-instructions.md)) 
This leads to the following two scenarios.

### Scenario 1 - Running the mscorlib and frameworks diffs using just the assemblies made available by jitutils.

Running the build script as mentioned above with '-f' produces a standalone './fx' directory in 
the root of the repo.  This can be used as inputs to the diff tool and gives the developer a 
simplified flow if 1) a platform builds CoreCLR/mscorlib and 2) the diff utilities build.

Steps:
* Build a baseline CoreCLR by following build directions in coreclr repo 
  [build doc directory](https://github.com/dotnet/coreclr/tree/master/Documentation/building).
* Build a diff CoreCLR by following the same directions above either in a seperate repo, or in the
  same repo after saving off the baseline Product directory.
* Ensure jit-diff, jit-analyze, and jit-dasm are on the path.
* Create an empty output directory.
* Invoke command
``` 
$ jit-diff diff --analyze --frameworksonly --base <base_coreclr_repo>/bin/Product/<platform>/crossgen --diff <diff_coreclr_repo>/bin/Product/<platform>/crossgen --output <output_directory> --core_root <jitutils_repo>/fx
```
* View summary output produced by jit-diff via jit-analyze.  Report returned on stdout.
* Check output directory
```
$ ls <output_directory>/*
```
The output directory will contain both a `base` and `diff` directory that in turn contains a set of `*.dasm`
files produced by the code generator. These are what are diff'ed.

### Scenario 2 - Running mscorlib, frameworks, and test assets diffs using the resources generated for a CoreCLR test run.

In this scenario follow the steps outlined in CoreCLR to set up the tests for a given platform. This 
will create a "core_root" directory in the built test assets that has all the platform frameworks 
as well as test dependencies.  This should be used as the 'core_root' for the test run in addition 
to providing the test assemblies.

Steps:
* Build a baseline CoreCLR by following build directions in coreclr repo 
  [build doc directory](https://github.com/dotnet/coreclr/tree/master/Documentation/building).
* Build a diff CoreCLR by following the same directions above either in a separate repo, or in the
  same repo after saving off the baseline Product directory.
* Ensure jit-diff, analyze, and jit-dasm are on the path.
* Create an empty output directory.
* Invoke command
```
$ jit-diff diff --analyze --base <coreclr_repo>/bin/Product/<platform>/crossgen --diff <diff_coreclr_repo>/bin/Product/<platform>/crossgen --output <output_directory> --core_root <test_root>/core_root --test_root <test_root>
```
* View summary output produced by jit-diff via jit-analyze.  Report returned on stdout.
* Check output directory
```
$ ls <output_directory>/*
```
The base and diff output directories should contain a tree that mirrors the test tree containing a `*.dasm` for
each assembly it found.

This scenario will take a fair bit longer than the first since it traverses and identifies test 
assembles in addition to the mscorlib/frameworks `*.dasm`.

### Notes on tags

jit-diff allows a user supplied '--tag' on the command-line.  This tag can be used to label different 
directories of `*.dasm` in the output directory so multiple (more than two) runs can be done.
This supports a scenario like the following:

* Build base CoreCLR
* Produce baseline diffs by invoking the tool with '--base'
* Make changes to CoreCLR JIT subdirectory to fix a bug.
* Produce tagged output by invoking jit-diff --diff ...  --tag "bugfix1"
* Make changes to CoreCLR JIT subdirectory to address review feedback/throughput issue.
* Produce tagged output by invoking jit-diff --diff ... --tag "reviewed1"
* Address more review feedback in CoreCLR JIT.
* Produce tagged output by invoking jit-diff --diff ... --tag "reviewed_final"
* ...

The above scenario should show that there is some flexibility in the work flow.

## Analyzing diffs

The jitutils suite includes the jit-analyze tool for analyzing diffs produced by jit-diff/jit-dasms 
utilities. In the example above the `--analyze` switch to jit-diff caused the tool to be invoked 
on the diff directories created by jit-diff. Analyze cracks the `*.dasm` files produced in the
earlier steps and extracts the bytes difference between the two based on the output produced 
by the JIT.  This data is keyed by file and method name - for instance two files with 
different names will not diff even if passed as the base and diff since the tool is looking 
to identify files missing from the base dataset vs the diff dataset.

Here is the help output:
```
$ jit-analyze --help
usage: jit-analyze [-b <arg>] [-d <arg>] [-r] [-c <arg>] [-w] [--reconcile]
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
    --reconcile          If there are methods that exist only in base or
                         diff, create zero-sized counterparts in diff,
                         and vice-versa. Update size deltas accordingly.
    --json <arg>         Dump analysis data to specified file in JSON
                         format.
    --tsv <arg>          Dump analysis data to specified file in
                         tab-separated format.
```

For the simplest case just point the tool at a base and diff dir produce by jit-diff and it 
will outline byte diff across the whole diff. This is what the jit-diff command lines in 
the previous section do. 

On a significant set of diffs it will produce output like the following:

```
$ jit-analyze --base ~/Work/dotnet/output/base --diff ~/Work/dotnet/output/diff
Found files with textual diffs.

Summary:
(Note: Lower is better)

Total bytes of diff: -4124
    diff is an improvement.

Top file regressions by size (bytes):
    193 : Microsoft.CodeAnalysis.dasm
    154 : System.Dynamic.Runtime.dasm
    60 : System.IO.Compression.dasm
    43 : System.Net.Security.dasm
    43 : System.Xml.ReaderWriter.dasm

Top file improvements by size (bytes):
    -1804 : mscorlib.dasm
    -1532 : Microsoft.CodeAnalysis.CSharp.dasm
    -726 : System.Xml.XmlDocument.dasm
    -284 : System.Linq.Expressions.dasm
    -239 : System.Net.Http.dasm

21 total files with size differences.

Top method regessions by size (bytes):
    328 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.DocumentationCommentXmlTokens:.cctor()
    266 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.MethodTypeInferrer:Fix(int,byref):bool:this
    194 : mscorlib.dasm - System.DefaultBinder:BindToMethod(int,ref,byref,ref,ref,ref,byref):ref:this
    187 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.LanguageParser:ParseModifiers(ref):this
    163 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Symbols.SourceAssemblySymbol:DecodeWellKnownAttribute(byref,int,bool):this

Top method improvements by size (bytes):
    -160 : System.Xml.XmlDocument.dasm - System.Xml.XmlTextWriter:AutoComplete(int):this
    -124 : System.Xml.XmlDocument.dasm - System.Xml.XmlTextWriter:WriteEndStartTag(bool):this
    -110 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.MemberSemanticModel:GetEnclosingBinder(ref,int):ref:this
    -95 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.CSharpDataFlowAnalysis:AnalyzeReadWrite():this
    -85 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.LanguageParser:ParseForStatement():ref:this

3762 total methods with size differences.
```

If `--tsv <file_name>` or `--json <file_name>` is passed, all the diff data extracted and analyzed 
will be written out for further analysis.


## Leveraging cloud resources

CoreCLR uses a Jenkins CI instance to implement the quality bar checkins and inflight development. 
This system defines a number of build jobs that produce artifacts that are cached for a peroid. 
The cijobs tool allows for developers to easily access these artifacts to avoid rebuilding them 
locally.

The cijobs functionality is split into two different commands, list and copy. The first, `list` 
allows for command line querying of job output.  The second, `copy`, copies built artifacts from 
particular jobs from the cloud to the local machine.

Here's the base help output:

```
$ cijobs --help
usage: cijobs <command> [<args>]

    list    List jobs on dotnet-ci.cloudapp.net for dotnet_coreclr.
    copy    Copies job artifacts from dotnet-ci.cloudapp.net. Currently
            hardcoded to dotnet_coreclr, this command copies a zip of
            the artifacts under the Product sub-directory that is the
            result of a build.
``` 

A common question for a developer might be "what is the last successful OSX checked build?"

```
$ cijobs list --match "osx"

job checked_osx
job checked_osx_flow
job checked_osx_flow_prtest
job checked_osx_prtest
job checked_osx_tst
job checked_osx_tst_prtest
job debug_osx
job debug_osx_flow
job debug_osx_flow_prtest
job debug_osx_prtest
job debug_osx_tst
job debug_osx_tst_prtest
job release_osx
job release_osx_flow
...
``` 

The previous example shows searching for job names that match "osx".  The checked_osx jobs is the 
one the developer wants.  (Some familiarity with the jobs running on the server is helpful. 
Visit dotnet-ci.cloudapp.net to familarize yourself with what's available.)

Further querying the `checked_osx` job for the last successful build can be done with this command 
line.

```
$ cijobs list --job checked_osx --last_successful

Last successful build:
build 1609 - SUCCESS : commit 74798b5b95aca1b27050038202034448a523c9f9
```

With this in hand two things can be acomplished.  First, new development for an feature could be 
started based on the commit hash returned, second, the tools generated by this job can be downloaded 
for use locally.

```
$ cijobs copy --job checked_osx --last_successful --output ../output/mytools --unzip

Downloading: job/dotnet_coreclr/job/master/job/checked_osx/1609/artifact/bin/Product/*zip*/Product.zip
```

Results are unzipped in the output after they are downloaded.

```
$ ls ../output/mytools/
Product		Product.zip

```

One comment on the underlying Jenkins feature.  The artifacts are kept and managed by a Jenkins plug-in 
used by the system.  This plug-in will zip on demand at any point in the defined artifacts output tree. 
Today we only use the Product sub-directory but this could be extended in the future.

## Configuring defaults

The command lines for many of the tools in the repo are large and can get involved.  Because of this, 
and to help speed up the typical dev flow implemented by jit-diff, a default config file and output 
location can be defined in the environment.  JIT_DASM_ROOT, when defined in the environment, tells 
jit-diff where to generate output by default as well as where the asmdiff.json containing default 
values and installed tools can be found.

```
$ export JIT_DASM_ROOT=~/Work/output
$ ls -1 $JIT_DASM_ROOT
asmdiff.json
dasmset_1
dasmset_2
dasmset_3
dasmset_4
dasmset_5
tools
```

The above example shows a populated JIT_DASM_ROOT.  The asmdiff.json file contains defaults, 
the `dasmset_(x)` contain multiple iterations of output from jit-diff, and the tools directory 
contains installed tools.

### asmdiff.json

A sample [asmdiff.json](TODO) is included in the jitutils repo as an example that can be modified 
for a developers own context.  We will go through the different elements here for added detail. 
The most interesting section of the file is the `"default"` section.  Each sub element of default 
maps directly to jit-diff option name.  Setting a default value here for any one of them will 
cause jit-diff to set them to the listed value on start up and then only override that value if 
new options are passed on the command line.  The `"base"` and `"diff"` entries are worth going 
into in more detail.  The `"base"` is set to `"checked_osx-1526"`.  Looking down in the `"tools"` 
section shows that the tool is installed in the `tools` sub-directory of JIT_DASM_ROOT.  Any 
of the so entered tools can be used in the default section as a value, but they can also be 
passed on the command line as the value for `--base` or `--diff`.

```
{
  "default": {
    "base": "checked_osx-1526",
    "diff": "/Users/russellhadley/Work/dotnet/coreclr/bin/Product/OSX.x64.Checked",
    "analyze": "true",
    "frameworksonly": "true",
    "output": "/Users/russellhadley/Work/dotnet/output",
    "core_root": "/Users/russellhadley/Work/dotnet/jitutils/fx"
  },
  "tools": [
    {
      "tag": "checked_osx-1439",
      "path": "/Users/russellhadley/Work/dotnet/output/tools/checked_osx-1439/Product/OSX.x64.Checked"
    },
    {
      "tag": "checked_osx-1442",
      "path": "/Users/russellhadley/Work/dotnet/output/tools/checked_osx-1442/Product/OSX.x64.Checked"
    },
    {
      "tag": "checked_osx-1443",
      "path": "/Users/russellhadley/Work/dotnet/output/tools/checked_osx-1443/Product/OSX.x64.Checked"
    },
    {
      "tag": "checked_osx-1526",
      "path": "/Users/russellhadley/Work/dotnet/output/tools/checked_osx-1526/Product/OSX.x64.Checked"
    }
  ]
}
```

### Listing current defaults

The jit-diff command `list` will read the current JIT_DASM_ROOT path, open asmdiff.json, and list 
the results.  Adding `--verbose` will show the associated file system paths for installed tools as well.

```
$ jit-diff list

Defaults:
	base: checked_osx-1526
	diff: /Users/russellhadley/Work/dotnet/coreclr/bin/Product/OSX.x64.Checked
	output: /Users/russellhadley/Work/dotnet/output
	core_root: /Users/russellhadley/Work/dotnet/jitutils/fx
	analyze: true
	frameworksonly: true

Installed tools:
	checked_osx-1439
	checked_osx-1442
	checked_osx-1443
	checked_osx-1526
``` 

### Installing new tools

The jit-diff command `install` will download and install a new tool to the default location 
and update the asmdiff.json so it can be found.

```
$ jit-diff install --help
usage: jit-diff install [-j <arg>] [-n <arg>] [-l] [-b <arg>]

    -j, --job <arg>          Name of the job.
    -n, --number <arg>       Job number.
    -l, --last_successful    Last successful build.
    -b, --branch <arg>       Name of branch.

```

The options to `install` are the same as you would use for the cijobs copy command since jit-diff 
uses cijobs to download the appropriate tools.  I.e. the `install` command is just a wrapper over 
cijobs to simplify getting tools into the default location correctly.