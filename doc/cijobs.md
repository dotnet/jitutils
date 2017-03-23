# CI jobs information

The .NET team maintains a "continuous integration" (CI) system for testing .NET, in particular,
for testing each source code change submitted for consideration. The "cijobs" command-line tool
allows for querying the CI for per-job information, and for downloading archived per-job artifacts.
These artifacts can be used for generating assembly code output, instead of building your own
baseline JIT, for example.

## cijobs

cijobs has two commands: (1) list, and (2) copy.

cijobs help message:
```
    $ cijobs --help
    usage: cijobs <command> [<args>]

        list    List jobs on dotnet-ci.cloudapp.net for the repo.
        copy    Copies job artifacts from dotnet-ci.cloudapp.net. This
                command copies a zip of artifacts from a repo (defaulted to
                dotnet_coreclr). The default location of the zips is the
                Product sub-directory, though that can be changed using the
                ContentPath(p) parameter
```

The "cijobs list" command has the following help message:
```
    $ cijobs list --help
    usage: cijobs list [-j <arg>] [-b <arg>] [-r <arg>] [-m <arg>]
                  [-n <arg>] [-l] [-c <arg>] [-a]

        -j, --job <arg>          Name of the job.
        -b, --branch <arg>       Name of the branch (default is master).
        -r, --repo <arg>         Name of the repo (e.g. dotnet_corefx or
                                 dotnet_coreclr). Default is dotnet_coreclr
        -m, --match <arg>        Regex pattern used to select jobs output.
        -n, --number <arg>       Job number.
        -l, --last_successful    List last successful build.
        -c, --commit <arg>       List build at this commit.
        -a, --artifacts          List job artifacts on server.
```

The "cijobs copy" command has the following help message:
```
    usage: cijobs copy [-j <arg>] [-n <arg>] [-l] [-c <arg>] [-b <arg>]
                  [-r <arg>] [-o <arg>] [-u] [-p <arg>]

        -j, --job <arg>            Name of the job.
        -n, --number <arg>         Job number.
        -l, --last_successful      Copy last successful build.
        -c, --commit <arg>         Copy this commit.
        -b, --branch <arg>         Name of the branch (default is master).
        -r, --repo <arg>           Name of the repo (e.g. dotnet_corefx or
                                   dotnet_coreclr). Default is
                                   dotnet_coreclr
        -o, --output <arg>         Output path.
        -u, --unzip                Unzip copied artifacts
        -p, --ContentPath <arg>    Relative product zip path. Default is
                                   artifact/bin/Product/*zip*/Product.zip
```
