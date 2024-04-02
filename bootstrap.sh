#!/usr/bin/env bash

# Bootstrap the jitutils tools:
# 1. If this script is run not from within the jitutils directory (e.g., you've downloaded
#    a copy of this file directly), then "git clone" the jitutils project first. Otherwise,
#    if we can tell we're being run from within an existing "git clone", don't do that.
# 2. Build the jitutils tools.
# 3. Download (if necessary) clang-format and clang-tidy (used by the jit-format tool).

function get_host_os {
    # Use uname to determine what the OS is.
    OSName=$(uname -s)
    case $OSName in
        Linux)
            __HostOS=Linux
            ;;

        Darwin)
            __HostOS=OSX
            ;;

        FreeBSD)
            __HostOS=FreeBSD
            ;;

        OpenBSD)
            __HostOS=OpenBSD
            ;;

        NetBSD)
            __HostOS=NetBSD
            ;;

        SunOS)
            __HostOS=SunOS
            ;;

        *)
            echo "Unsupported OS $OSName detected, configuring as if for Linux"
            __HostOS=Linux
            ;;
    esac
}

_machineHasCurl=

function validate_url {
    if (( _machineHasCurl == 1 )); then
        status="$(curl -sLIo /dev/null "$1" -w '%{http_code}\n')"
    else
        response=($(wget -S --spider "$1" 2>&1 | grep "HTTP/"))
        status="${response[1]}"
    fi

    if (( status >= 200 || status < 400 )); then
        return 0;
    else
        return 1;
    fi
}

function download_tools {

    # Do we have wget or curl?

    if command -v curl 2>/dev/null; then
        _machineHasCurl=1
    elif ! command -v wget 2>/dev/null; then
        echo "Error: curl or wget not found; not downloading clang-format and clang-tidy."
        return 1
    fi

    # Figure out which version to download. The "RID:" value from "dotnet --info" looks
    # like this:
    #      RID:         osx.10.12-x64

    info=$(dotnet --info |grep RID:)
    info=${info##*RID:* }
    echo "dotnet RID: ${info}"

    # override common RIDs with compatible version so we don't need to upload binaries for each RID
    case $info in
        osx-x64)
        info=osx.10.15-x64
        ;;
        osx.*-x64)
        info=osx.10.15-x64
        ;;
        linux-x64)
        info=ubuntu.18.04-x64
        ;;
        ubuntu.*-x64)
        info=ubuntu.18.04-x64
        ;;
    esac

    clangFormatUrl=https://clrjit.blob.core.windows.net/clang-tools/${info}/clang-format

    if validate_url "$clangFormatUrl" > /dev/null; then
        echo "Downloading clang-format from ${clangFormatUrl} to bin directory"
        # download appropriate version of clang-format
        if (( _machineHasCurl == 1 )); then
            curl --retry 4 --progress-bar --location --fail "$clangFormatUrl" -o bin/clang-format
        else
            wget --tries 4 --progress=dot:giga "$clangFormatUrl" -O bin/clang-format
        fi
        chmod 751 bin/clang-format
    else
        echo "clang-format not found here: $clangFormatUrl"
    fi

    clangTidyUrl=https://clrjit.blob.core.windows.net/clang-tools/${info}/clang-tidy

    if validate_url "$clangTidyUrl" > /dev/null; then
        echo "Downloading clang-tidy from ${clangTidyUrl} to bin directory"
        # download appropriate version of clang-tidy
        if (( _machineHasCurl == 1 )); then
            curl --retry 4 --progress-bar --location --fail "$clangTidyUrl" -o bin/clang-tidy
        else
            wget --tries 4 --progress=dot:giga "$clangTidyUrl" -O bin/clang-tidy
        fi
        chmod 751 bin/clang-tidy
    else
        echo "clang-tidy not found here: $clangTidyUrl"
    fi

    if [ ! -f bin/clang-format -o ! -f bin/clang-tidy ]; then
        echo "Either clang-tidy or clang-format was not installed. Please install and put them on the PATH to use jit-format."
        echo "Tools can be found at https://llvm.org/releases/download.html#3.8.0"
        return 1
    fi

    return 0
}

# Start the non-functions.

__ErrMsgPrefix="ERROR: "

get_host_os

# Check if our required tools exist.

if ! hash dotnet 2>/dev/null; then
    echo "${__ErrMsgPrefix}Can't find dotnet! Please install from https://dot.net and add to PATH."
    exit 1
fi

if ! hash git 2>/dev/null; then
    echo "${__ErrMsgPrefix}Can't find git! Please add to PATH."
    exit 1
fi

# Are we already in the dotnet/jitutils repo? Or do we need to clone it? We look for build.cmd
# in the directory this script was invoked from.

# Obtain the location of the bash script.
__root="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Check if the bootstrap script is in the root of the jitutils repo.
# By default, we're going to clone the repo.
__clone_repo=1
if [ -e ${__root}/build.cmd ]; then
    # We found build.cmd. But make sure it's the root of the repo.
    __root="$( cd ${__root} && git rev-parse --show-toplevel )"
    pushd ${__root} >/dev/null
    git remote -v | grep "/jitutils" >/dev/null
    if [ $? == 0 ]; then
        # We've proven that we're at the root of the jitutils repo.
        __clone_repo=0
    fi
    popd >/dev/null
fi

if [ ${__clone_repo} == 1 ]; then
    git clone https://github.com/dotnet/jitutils.git
    exit_code=$?
    if [ $exit_code != 0 ]; then
        echo "${__ErrMsgPrefix}git clone failed."
        exit $exit_code
    fi
    if [ ! -d ${__root}/jitutils ]; then
        echo "${__ErrMsgPrefix}can't find ${__root}/jitutils."
        exit 1
    fi
    pushd ${__root}/jitutils >/dev/null
else
    pushd . >/dev/null
fi

# Pull in needed packages.  This works globally (due to jitutils.sln).

dotnet restore
exit_code=$?
if [ $exit_code != 0 ]; then
    echo "${__ErrMsgPrefix}Failed to restore packages."
    exit $exit_code
fi

# Build and publish all the utilities

./build.sh -p
exit_code=$?
if [ $exit_code != 0 ]; then
    echo "${__ErrMsgPrefix}Build failed."
    exit $exit_code
fi

exit_code=0
if ! hash clang-format 2>/dev/null || ! hash clang-tidy 2>/dev/null; then
    download_tools
    exit_code=$?
else
    if ! clang-format --version | grep -q 3.8 || ! clang-tidy --version | grep -q 3.8; then
        echo "jit-format requires clang-format and clang-tidy version 3.8.*. Currently installed: "
        clang-format --version
        clang-tidy --version

        echo "Installing version 3.8 of clang tools"
        download_tools
        exit_code=$?
    fi
fi
if [ $exit_code != 0 ]; then
    echo "${__ErrMsgPrefix}Failed to download clang-format and clang-tidy."
    exit $exit_code
fi

popd >/dev/null

echo "Adding ${__root}/jitutils/bin to PATH"
export PATH=$PATH:${__root}/jitutils/bin

echo "Done setting up!"
exit 0
