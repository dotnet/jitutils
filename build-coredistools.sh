#!/usr/bin/env bash

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
LLVMTargetsToBuild="AArch64;ARM;X86"

# Figure out which `strip` to use. Prefer `llvm-strip` if it is available.
# `llvm-strip` is available in CBL-Mariner container,
# `llvm-strip-<version>` is available on standard cross build Ubuntu container,
# `strip` is available on macOS.
StripTool=$(command -v llvm-strip{,-{20..15}} strip | head -n 1)
if [ -z "$StripTool" ]; then
    echo "Strip tool not found"
    exit 1
fi

TblGenTool=$(command -v llvm-tblgen)
if [ -z "$TblGenTool" ]; then
    echo "llvm-tblgen tool not found"
    exit 1
fi

# Take first match from: clang clang-20 clang-19 .. clang-15
C_COMPILER=$(command -v clang{,-{20..15}} | head -n 1)
if [ -z "$C_COMPILER" ]; then
    echo "C compiler not found"
    # Keep going in case cmake can find one?
fi

CXX_COMPILER=$(command -v clang++{,-{20..15}} | head -n 1)
if [ -z "$CXX_COMPILER" ]; then
    echo "C++ compiler not found"
    # Keep going in case cmake can find one?
fi

echo "Using C compiler: $C_COMPILER"
echo "Using C++ compiler: $CXX_COMPILER"

case "$TargetOSArchitecture" in
    linux-arm)
        CMakeCrossCompiling=ON
        LLVMDefaultTargetTriple=thumbv7-linux-gnueabihf
        LLVMHostTriple=arm-linux-gnueabihf
        LLVMTargetsToBuild="ARM"
        EnsureCrossRootfsDirectoryExists
        ;;

    linux-arm64)
        CMakeCrossCompiling=ON
        LLVMHostTriple=aarch64-linux-gnu
        EnsureCrossRootfsDirectoryExists
        ;;

    linux-x64)
        LLVMHostTriple=x86_64-linux-gnu
        if [ $CrossBuildUsingMariner -eq 1 ]; then
            CMakeCrossCompiling=ON
            EnsureCrossRootfsDirectoryExists
        else
            CMakeCrossCompiling=OFF
        fi
        ;;

    linux-loongarch64)
        CMakeCrossCompiling=OFF
        LLVMHostTriple=loongarch64-linux-gnu
        LLVMTargetsToBuild="LoongArch"
        ;;

    linux-riscv64)
        CMakeCrossCompiling=ON
        LLVMHostTriple=riscv64-linux-gnu
        LLVMTargetsToBuild="RISCV"
        EnsureCrossRootfsDirectoryExists
        ;;

    osx-arm64)
        CMakeCrossCompiling=ON
        CMakeOSXArchitectures=arm64
        LLVMHostTriple=arm64-apple-macos
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

LLVMDefaultTargetTriple=${LLVMDefaultTargetTriple:-$LLVMHostTriple}

RootDirectory="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
SourcesDirectory=$RootDirectory/src
BinariesDirectory=$RootDirectory/obj/$TargetOSArchitecture
StagingDirectory=$RootDirectory/artifacts/$TargetOSArchitecture

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
    BUILD_FLAGS="-target $LLVMHostTriple"
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=${C_COMPILER} \
        -DCMAKE_CXX_COMPILER=${CXX_COMPILER} \
        -DCMAKE_C_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_CXX_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DCMAKE_OSX_ARCHITECTURES=$CMakeOSXArchitectures \
        -DCMAKE_STRIP=$StripTool \
        -DLLVM_DEFAULT_TARGET_TRIPLE=$LLVMDefaultTargetTriple \
        -DLLVM_ENABLE_TERMINFO=OFF \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
        -DLLVM_HOST_TRIPLE=$LLVMHostTriple \
        -DLLVM_INCLUDE_TESTS=OFF \
        -DLLVM_TABLEGEN=$TblGenTool \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        -DLLVM_TOOL_COREDISTOOLS_BUILD=ON \
        $SourcesDirectory/llvm-project/llvm
elif [ $CrossBuildUsingMariner -eq 1 ]; then
    BUILD_FLAGS="--sysroot=$CrossRootfsDirectory -target $LLVMHostTriple"
    # CBL-Mariner doesn't have `ld` so need to tell clang to use `lld` with "-fuse-ld=lld"
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=${C_COMPILER} \
        -DCMAKE_CXX_COMPILER=${CXX_COMPILER} \
        -DCMAKE_C_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_CXX_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_EXE_LINKER_FLAGS="-fuse-ld=lld" \
        -DCMAKE_SHARED_LINKER_FLAGS="-fuse-ld=lld" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DCMAKE_LIBRARY_PATH=$CrossRootfsDirectory/usr/lib/$LLVMHostTriple \
        -DCMAKE_STRIP=$StripTool \
        -DLLVM_DEFAULT_TARGET_TRIPLE=$LLVMDefaultTargetTriple \
        -DLLVM_ENABLE_TERMINFO=OFF \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
        -DLLVM_HOST_TRIPLE=$LLVMHostTriple \
        -DLLVM_INCLUDE_TESTS=OFF \
        -DLLVM_TABLEGEN=$TblGenTool \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        -DLLVM_TOOL_COREDISTOOLS_BUILD=ON \
        $SourcesDirectory/llvm-project/llvm
else
    BUILD_FLAGS="--sysroot=$CrossRootfsDirectory -target $LLVMHostTriple"
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=${C_COMPILER} \
        -DCMAKE_CXX_COMPILER=${CXX_COMPILER} \
        -DCMAKE_C_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_CXX_FLAGS="${BUILD_FLAGS}" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DCMAKE_LIBRARY_PATH=$CrossRootfsDirectory/usr/lib/$LLVMHostTriple \
        -DCMAKE_STRIP=$StripTool \
        -DLLVM_DEFAULT_TARGET_TRIPLE=$LLVMDefaultTargetTriple \
        -DLLVM_ENABLE_TERMINFO=OFF \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
        -DLLVM_HOST_TRIPLE=$LLVMHostTriple \
        -DLLVM_INCLUDE_TESTS=OFF \
        -DLLVM_TABLEGEN=$TblGenTool \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        -DLLVM_TOOL_COREDISTOOLS_BUILD=ON \
        $SourcesDirectory/llvm-project/llvm
fi

popd

if [ "$?" -ne 0 ]; then
    echo "ERROR: cmake exited with code $1"
    exit 1
fi

cmake \
    --build $BinariesDirectory \
    --parallel \
    --target install-coredistools-stripped

if [ "$?" -ne 0 ]; then
    echo "ERROR: cmake exited with code $1"
    exit 1
fi

exit 0
