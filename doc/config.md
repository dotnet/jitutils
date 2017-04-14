# Configuring defaults

Several jitutils tools support specifying default command line arguments via a configuration
file. This file is described here.

The tools which currently support this configuration mechanism are: jit-diff, jit-format.

## Overview

When the environment variable `JIT_UTILS_ROOT` is defined, it specifies the directory where a
config.json file can be found that specifies various configuration data that will be used
while running the jitutils tools. It also specifies the root directory where the tools can
create subdirectories to copy various things.

```
$ export JIT_UTILS_ROOT=~/Work/output
$ ls -1 $JIT_UTILS_ROOT
config.json
dasmset_1
dasmset_2
dasmset_3
dasmset_4
dasmset_5
tools
```

The above example shows a populated `JIT_UTILS_ROOT` directory.  The config.json file contains defaults,
the `dasmset_(x)` contain multiple iterations of output from jit-diff, and the tools directory
contains installed tools.

### config.json

A sample config.json file is included in the jitutils repo as an example that can be modified
for a developer's own use.  We will go through the different elements here for added detail.
This file supplies the configuration options for both jit-diff and jit-format. The most interesting
section of the file is the `"default"` section.  Each sub element of `"default"` maps directly to a jit-diff
or jit-format option name.  Setting a default value here will cause the tools to
use the given value on start up and then only override that value if new options are passed
on the command line.

In the jit-diff section, the `"base"` and `"diff"` entries are worth going into
in more detail.  The `"base"` is set to `"checked_osx-1526"`.  Looking down in the `"tools"` section
shows that the tool is installed in the `tools` sub-directory of the directory specified by
`JIT_UTILS_ROOT`.  Any of the tools listed like this can be used in the default section
as a value, but they can also be passed on the command line
as the value for `--base` or `--diff`.

Sample config.json:
```
{
  "format": {
    "default": {
      "arch": "x64",
      "build": "Checked",
      "os": "Windows_NT",
      "coreclr": "C:\\michelm\\coreclr",
      "verbose": "true",
      "fix": "true"
    }
  },
  "asmdiff": {
    "default": {
      "base": "checked_osx-1526",
      "diff": "/Users/russellhadley/Work/dotnet/coreclr/bin/Product/OSX.x64.Checked",
      "frameworks": "true",
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
}
```

### Listing current defaults

The jit-diff command `list` will read the config.json file in the specified `JIT_UTILS_ROOT` path, and list
the results.  Adding `--verbose` will show the associated file system paths for installed tools as well.

For example:
```
$ jit-diff list

Defaults:
	base: checked_osx-1526
	diff: /Users/russellhadley/Work/dotnet/coreclr/bin/Product/OSX.x64.Checked
	output: /Users/russellhadley/Work/dotnet/output
	core_root: /Users/russellhadley/Work/dotnet/jitutils/fx
	frameworks: true

Installed tools:
	checked_osx-1439
	checked_osx-1442
	checked_osx-1443
	checked_osx-1526
```

### Installing new tools

The jit-diff command `install` will download and install a new tool to the default location
and update the config.json so it can be found.

The options to `install` are the same as you would use for the cijobs copy command since jit-diff
uses cijobs to download the appropriate tools.  I.e. the `install` command is just a wrapper over
cijobs to simplify getting tools into the default location correctly.
