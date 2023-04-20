#!/usr/bin/env bash
#
# Build the Linux/Mac llvm-tblgen tool. Note that this will be run during the
# Linux/Mac coredistools build. So, we only build the versions that we'll use
# during those builds. Thus, we need a linux-x64 version (used for
# linux-x64, linux-arm, linux-arm64 builds) and osx-x64 version (used for
# osx-x64 and osx-arm64 builds).
#
# The linux-x64 build is itself a cross-build, using CBL-Mariner container to build.

TargetOSArchitecture=$1
CrossRootfsDirectory=$2

EnsureCrossRootfsDirectoryExists () {
    if [ ! -d "$CrossRootfsDirectory" ]; then
        echo "Invalid or unspecified CrossRootfsDirectory: $CrossRootfsDirectory"
        exit 1
    fi
}

CMakeOSXArchitectures=
LLVMTargetsToBuild="AArch64;ARM;X86"

case "$TargetOSArchitecture" in
    linux-x64)
        CMakeCrossCompiling=ON
        LLVMHostTriple=x86_64-linux-gnu
        EnsureCrossRootfsDirectoryExists
        ;;

    linux-loongarch64)
        CMakeCrossCompiling=OFF
        LLVMHostTriple=loongarch64-linux-gnu
        LLVMTargetsToBuild="LoongArch"
        ;;

    osx-x64)
        CMakeCrossCompiling=OFF
        CMakeOSXArchitectures=x86_64
        LLVMHostTriple=x86_64-apple-darwin
        ;;

    *)
        echo "Unknown target OS and architecture: $TargetOSArchitecture"
        exit 1
esac

RootDirectory="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
SourcesDirectory=$RootDirectory/src
BinariesDirectory=$RootDirectory/obj
StagingDirectory=$RootDirectory/bin

command -v cmake >/dev/null 2>&1

if [ "$?" -ne 0 ]; then
    echo "ERROR: cmake is not found in the PATH"
    exit 1
fi

if [ ! -d $BinariesDirectory ]; then
    mkdir -p $BinariesDirectory
fi

pushd "$BinariesDirectory"

if [ -z "$CrossRootfsDirectory" ]; then
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=$(command -v clang) \
        -DCMAKE_CXX_COMPILER=$(command -v clang++) \
        -DCMAKE_INSTALL_PREFIX=$RootDirectory \
        -DCMAKE_OSX_ARCHITECTURES=$CMakeOSXArchitectures \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        $SourcesDirectory/llvm-project/llvm
else
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX=$RootDirectory \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=$(command -v clang) \
        -DCMAKE_C_FLAGS="--sysroot=$CrossRootfsDirectory" \
        -DCMAKE_CXX_COMPILER=$(command -v clang++) \
        -DCMAKE_CXX_FLAGS="--sysroot=$CrossRootfsDirectory" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
        -DCMAKE_INSTALL_PREFIX=$RootDirectory \
        -DCMAKE_LIBRARY_PATH=$CrossRootfsDirectory/usr/lib/$LLVMHostTriple \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        $SourcesDirectory/llvm-project/llvm
fi

popd

if [ "$?" -ne 0 ]; then
    echo "ERROR: cmake exited with code $1"
    exit 1
fi

cmake \
    --build $BinariesDirectory \
    --target llvm-tblgen \
    --config Release

if [ "$?" -ne 0 ]; then
    echo "ERROR: cmake exited with code $1"
    exit 1
fi

if [ ! -d $StagingDirectory ]; then
    mkdir -p $StagingDirectory
fi

# Copy llvm-tblgen from BinariesDirectory to StagingDirectory
find $BinariesDirectory -name llvm-tblgen -type f -exec cp -v {} $StagingDirectory \;

exit 0
