#!/usr/bin/env bash

## Build and optionally publish sub projects
##
## This script will by default build release versions of the tools.
## If publish (-p) is requested it will create standalone versions of the
## tools in <script_root>/bin.

function usage
{
    echo ""
    echo "build.sh [-b <BUILD TYPE>] [-h] [-p]"
    echo ""
    echo "    -b <BUILD TYPE> : Build type, can be Debug or Release."
    echo "    -h              : Show this message."
    echo "    -p              : Publish utilities."
    echo ""
}

# defaults
__ErrMsgPrefix="ERROR: "
final_exit_code=0
buildType="Release"
publish=0
scriptDir="$(cd "$(dirname "$0")" || exit; pwd -P)"
# default install in 'bin' dir at script location
appInstallDir="$scriptDir/bin"
rid=$(dotnet --info | grep RID:)
rid=${rid##*RID:* }

# process for '-h', '-p', '-b <arg>'
while getopts "hpb:" opt; do
    case "$opt" in
    h)
        usage
        exit 0
        ;;
    b)  
        buildType=$OPTARG
        ;;
    p)  
        publish=1
        ;;
    *)  echo "ERROR: unknown argument $opt"
        exit 1
        ;;
    esac
done

# declare the array of projects   
declare -a projects=(jit-dasm jit-diff jit-analyze jit-format pmi jit-dasm-pmi jit-decisions-analyze performance-explorer)

# for each project either build or publish
for proj in "${projects[@]}"
do
    if [ "$publish" = 1 ]; then
        case "$proj" in
            # Publish src/pmi project without single-file, so it can be executed with a custom build of the runtime/JIT
            pmi) dotnet publish -c "$buildType" -o "$appInstallDir" ./src/"$proj" ;;
            *)   dotnet publish -c "$buildType" -o "$appInstallDir" ./src/"$proj" --self-contained -r $rid -p:PublishSingleFile=true ;;
        esac
        exit_code=$?
        if [ $exit_code != 0 ]; then
            echo "${__ErrMsgPrefix}dotnet publish of ./src/${proj} failed."
            final_exit_code=1
        fi
    else
        dotnet build -c "$buildType" ./src/"$proj"
        exit_code=$?
        if [ $exit_code != 0 ]; then
            echo "${__ErrMsgPrefix}dotnet build  of ./src/${proj} failed."
            final_exit_code=1
        fi
    fi
done

exit "$final_exit_code"
