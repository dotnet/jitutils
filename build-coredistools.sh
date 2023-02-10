#!/usr/bin/env bash

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
    linux-arm)
        CMakeCrossCompiling=ON
        LLVMDefaultTargetTriple=thumbv7-linux-gnueabihf
        LLVMHostTriple=arm-linux-gnueabihf
        LLVMTargetsToBuild=ARM
        EnsureCrossRootfsDirectoryExists
        ;;

    linux-arm64)
        CMakeCrossCompiling=ON
        LLVMHostTriple=aarch64-linux-gnu
        EnsureCrossRootfsDirectoryExists
        ;;

    linux-x64)
        CMakeCrossCompiling=OFF
        LLVMHostTriple=x86_64-linux-gnu
        ;;

    linux-loongarch64)
        CMakeCrossCompiling=OFF
        LLVMHostTriple=loongarch64-linux-gnu
        LLVMTargetsToBuild="LoongArch"
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

which cmake >/dev/null 2>&1

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
        -DCMAKE_C_COMPILER=$(which clang) \
        -DCMAKE_C_FLAGS="-target $LLVMHostTriple" \
        -DCMAKE_CXX_COMPILER=$(which clang++) \
        -DCMAKE_CXX_FLAGS="-target $LLVMHostTriple" \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DCMAKE_OSX_ARCHITECTURES=$CMakeOSXArchitectures \
        -DCMAKE_STRIP=$(which strip) \
        -DLLVM_DEFAULT_TARGET_TRIPLE=$LLVMDefaultTargetTriple \
        -DLLVM_ENABLE_TERMINFO=OFF \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
        -DLLVM_HOST_TRIPLE=$LLVMHostTriple \
        -DLLVM_INCLUDE_TESTS=OFF \
        -DLLVM_TABLEGEN=$(which llvm-tblgen) \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        -DLLVM_TOOL_COREDISTOOLS_BUILD=ON \
        $SourcesDirectory/llvm-project/llvm
else
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CROSSCOMPILING=$CMakeCrossCompiling \
        -DCMAKE_C_COMPILER=$(which clang) \
        -DCMAKE_C_FLAGS="-target $LLVMHostTriple --sysroot=$CrossRootfsDirectory" \
        -DCMAKE_CXX_COMPILER=$(which clang++) \
        -DCMAKE_CXX_FLAGS="-target $LLVMHostTriple --sysroot=$CrossRootfsDirectory" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DCMAKE_LIBRARY_PATH=$CrossRootfsDirectory/usr/lib/$LLVMHostTriple \
        -DCMAKE_STRIP=/usr/$LLVMHostTriple/bin/strip \
        -DLLVM_DEFAULT_TARGET_TRIPLE=$LLVMDefaultTargetTriple \
        -DLLVM_ENABLE_TERMINFO=OFF \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
        -DLLVM_HOST_TRIPLE=$LLVMHostTriple \
        -DLLVM_INCLUDE_TESTS=OFF \
        -DLLVM_TABLEGEN=$(which llvm-tblgen) \
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
