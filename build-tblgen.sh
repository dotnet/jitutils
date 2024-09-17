#!/usr/bin/env bash
#
# Build the Linux/Mac llvm-tblgen tool. Note that this will be run during the
# Linux/Mac coredistools build. So, we only build the versions that we'll use
# during those builds. Thus, we need a linux-x64 version (used for
# linux-x64, linux-arm, linux-arm64 builds) and osx-x64 version (used for
# osx-x64 and osx-arm64 builds).
#
# The linux-x64 build is itself a cross-build when using CBL-Mariner container to build.

TargetOSArchitecture=$1
CrossRootfsDirectory=$2

# Set this to 1 to build using CBL-Mariner
CrossBuildUsingMariner=1

EnsureCrossRootfsDirectoryExists () {
    if [ ! -d "$CrossRootfsDirectory" ]; then
        echo "Invalid or unspecified CrossRootfsDirectory: $CrossRootfsDirectory"
        exit 1
    fi
}

CMakeOSXArchitectures=
LLVMTargetsToBuild="AArch64;ARM;X86;LoongArch;RISCV"

case "$TargetOSArchitecture" in
    linux-x64)
        LLVMHostTriple=x86_64-linux-gnu
        if [ $CrossBuildUsingMariner -eq 1 ]; then
            CMakeCrossCompiling=ON
            CMakeSystemName=Linux
            EnsureCrossRootfsDirectoryExists
        else
            CMakeCrossCompiling=OFF
            CMakeSystemName=
        fi
        ;;

    linux-loongarch64)
        CMakeCrossCompiling=OFF
        CMakeSystemName=
        LLVMHostTriple=loongarch64-linux-gnu
        LLVMTargetsToBuild="LoongArch"
        ;;

    osx-x64)
        CMakeCrossCompiling=OFF
        CMakeSystemName=
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

# Take first match from: clang clang-20 clang-19 .. clang-15
C_COMPILER=$(command -v clang{,-{20..15}} | head -n 1)
CXX_COMPILER=$(command -v clang++{,-{20..15}} | head -n 1)

if [ -z "$CrossRootfsDirectory" ]; then
    BUILD_FLAGS=""
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_SYSTEM_NAME=$CMakeSystemName \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=$C_COMPILER \
        -DCMAKE_CXX_COMPILER=$CXX_COMPILER \
        -DCMAKE_C_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_CXX_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_INSTALL_PREFIX=$RootDirectory \
        -DCMAKE_OSX_ARCHITECTURES=$CMakeOSXArchitectures \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        $SourcesDirectory/llvm-project/llvm
elif [ $CrossBuildUsingMariner -eq 1 ]; then
    BUILD_FLAGS="--sysroot=$CrossRootfsDirectory"
    # CBL-Mariner doesn't have `ld` so need to tell clang to use `lld` with "-fuse-ld=lld"
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX=$RootDirectory \
        -DCMAKE_SYSTEM_NAME=$CMakeSystemName \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=$C_COMPILER \
        -DCMAKE_CXX_COMPILER=$CXX_COMPILER \
        -DCMAKE_C_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_CXX_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_EXE_LINKER_FLAGS="-fuse-ld=lld" \
        -DCMAKE_SHARED_LINKER_FLAGS="-fuse-ld=lld" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
        -DCMAKE_LIBRARY_PATH=$CrossRootfsDirectory/usr/lib/$LLVMHostTriple \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        $SourcesDirectory/llvm-project/llvm
else
    BUILD_FLAGS="--sysroot=$CrossRootfsDirectory"
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX=$RootDirectory \
        -DCMAKE_SYSTEM_NAME=$CMakeSystemName \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=$C_COMPILER \
        -DCMAKE_CXX_COMPILER=$CXX_COMPILER \
        -DCMAKE_C_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_CXX_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
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
