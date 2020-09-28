#!/usr/bin/env bash

TargetOSArchitecture=$1
CrossRootfsDirectory=$2

case "$TargetOSArchitecture" in
    linux-arm)
        CrossCompiling=1
        LLVMDefaultTargetTriple=thumbv7-linux-gnueabihf
        LLVMHostTriple=arm-linux-gnueabihf
        LLVMTargetsToBuild=ARM
        ;;

    linux-arm64)
        CrossCompiling=1
        LLVMDefaultTargetTriple=aarch64-linux-gnu
        LLVMHostTriple=aarch64-linux-gnu
        LLVMTargetsToBuild=AArch64
        ;;

    linux-x64|osx-x64)
        CrossCompiling=0
        LLVMTargetsToBuild="AArch64;X86"
        ;;

    *)
        echo "Unknown target OS and architecture: $TargetOSArchitecture"
        exit 1
esac

if [[ $CrossCompiling -eq 1 && ! -d $CrossRootfsDirectory ]]; then
    echo "Invalid or unspecified CrossRootfsDirectory: $CrossRootfsDirectory"
    exit 1
fi

RootDirectory="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
SourcesDirectory=$RootDirectory/src
BinariesDirectory=$RootDirectory/obj
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

if [ "$CrossCompiling" -eq 1 ]; then
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CROSSCOMPILING=ON \
        -DCMAKE_C_COMPILER=$(which clang) \
        -DCMAKE_C_FLAGS="-target $LLVMHostTriple --sysroot=$CrossRootfsDirectory" \
        -DCMAKE_CXX_COMPILER=$(which clang++) \
        -DCMAKE_CXX_FLAGS="-target $LLVMHostTriple --sysroot=$CrossRootfsDirectory" \
        -DCMAKE_INCLUDE_PATH=$CrossRootfsDirectory/usr/include \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DCMAKE_LIBRARY_PATH=$CrossRootfsDirectory/usr/lib/$LLVMHostTriple \
        -DCMAKE_STRIP=/usr/$LLVMHostTriple/bin/strip \
        -DLLVM_DEFAULT_TARGET_TRIPLE=$LLVMDefaultTargetTriple \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
        -DLLVM_HOST_TRIPLE=$LLVMHostTriple \
        -DLLVM_TABLEGEN=$(which llvm-tblgen) \
        -DLLVM_TARGETS_TO_BUILD=$LLVMTargetsToBuild \
        -DLLVM_TOOL_COREDISTOOLS_BUILD=ON \
        $SourcesDirectory/llvm-project/llvm
else
    cmake \
        -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_C_COMPILER=$(which clang) \
        -DCMAKE_CXX_COMPILER=$(which clang++) \
        -DCMAKE_INSTALL_PREFIX=$StagingDirectory \
        -DLLVM_EXTERNAL_PROJECTS=coredistools \
        -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR=$SourcesDirectory/coredistools \
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
    --target install-coredistools-stripped

if [ "$?" -ne 0 ]; then
    echo "ERROR: cmake exited with code $1"
    exit 1
fi

exit 0
