#!/usr/bin/env bash

## Build and optionally publish sub projects
##
## This script will by default build release versions of the tools.
## If publish (-p) is requested it will create standalone versions of the
## tools in <script_root>/bin.
## 
## If frameworks (-f) are requested the script will create a scratch empty 'app'
## publish that contains the default frameworks.

function usage
{
    echo ""
    echo "build.sh [-b <BUILD TYPE>] [-f] [-h] [-p]"
    echo ""
    echo "    -b <BUILD TYPE> : Build type, can be Debug or Release."
    echo "    -h              : Show this message."
    echo "    -f              : Install default framework directory in <script_root>/fx."
    echo "    -p              : Publish utilities."
    echo ""
}

# defaults
__ErrMsgPrefix="ERROR: "
final_exit_code=0
buildType="Release"
publish=false
workingDir="$PWD"
cd "`dirname \"$0\"`"
scriptDir="$PWD"
cd $workingDir
platform="`dotnet --info | awk '/RID/ {print $2}'`"
# default install in 'bin' dir at script location
appInstallDir="$scriptDir/bin"
fxInstallDir="$scriptDir/fx"

# process for '-h', '-p', 'f', '-b <arg>'
while getopts "hpfb:" opt; do
    case "$opt" in
    h)
        usage
        exit 0
        ;;
    b)  
        buildType=$OPTARG
        ;;
    p)  
        publish=true
        ;;
    f)  
        fx=true
        ;;
    esac
done

# declare the array of projects   
declare -a projects=(jit-dasm jit-diff jit-analyze jit-format pmi jit-dasm-pmi jit-decisions-analyze)

# for each project either build or publish
for proj in "${projects[@]}"
do
    if [ "$publish" == true ]; then
        dotnet publish -c $buildType -o $appInstallDir ./src/$proj
        exit_code=$?
        if [ $exit_code != 0 ]; then
            echo "${__ErrMsgPrefix}dotnet publish of ./src/${proj} failed."
            final_exit_code=1
        fi
    else
        dotnet build -c $buildType ./src/$proj
        exit_code=$?
        if [ $exit_code != 0 ]; then
            echo "${__ErrMsgPrefix}dotnet build  of ./src/${proj} failed."
            final_exit_code=1
        fi
    fi
done

# set up fx if requested.

if [ "$fx" == true ]; then
    # Need to explicitly restore 'packages' project for host runtime in order
    # for subsequent publish to be able to accept --runtime parameter to publish
    # it as standalone.
    dotnet restore --runtime $platform ./src/packages
    exit_code=$?
    if [ $exit_code != 0 ]; then
        echo "${__ErrMsgPrefix}dotnet restore of ./src/packages failed."
        final_exit_code=1
    fi

    dotnet publish -c $buildType -o $fxInstallDir --runtime $platform ./src/packages
    exit_code=$?
    if [ $exit_code != 0 ]; then
        echo "${__ErrMsgPrefix}dotnet publish of ./src/packages failed."
        final_exit_code=1
    fi

    # remove package version of mscorlib* - refer to core root version for diff testing.
    rm -f $fxInstallDir/mscorlib*
fi

exit $final_exit_code
