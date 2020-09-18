@echo off
setlocal

set RootDirectory=%~dp0
set SourcesDirectory=%RootDirectory%src
set BinariesDirectory=%RootDirectory%obj
set TargetOSArchitecture=%1

if /i "%TargetOSArchitecture%" == "windows-arm" (
    set GeneratorPlatform=ARM
    set LLVMDefaultTargetTriple=thumbv7-pc-windows-msvc
    set LLVMHostTriple=arm-pc-windows-msvc
) else if /i "%TargetOSArchitecture%" == "windows-arm64" (
    set GeneratorPlatform=ARM64
    set LLVMHostTriple=aarch64-pc-windows-msvc
) else if /i "%TargetOSArchitecture%" == "windows-x64" (
    set GeneratorPlatform=x64
    set LLVMHostTriple=x86_64-pc-windows-msvc
) else if /i "%TargetOSArchitecture%" == "windows-x86" (
    set GeneratorPlatform=Win32
    set LLVMHostTriple=i686-pc-windows-msvc
) else (
    echo "Unknown target OS and architecture: %TargetOSArchitecture%"
    exit /b 1
)

if not defined LLVMDefaultTargetTriple (
    set LLVMDefaultTargetTriple=%LLVMHostTriple%
)

where /q llvm-tblgen.exe

if %ERRORLEVEL% neq 0 (
    echo llvm-tblgen.exe is not found in the PATH
    exit /b 1
)

for /f %%I in ('where llvm-tblgen.exe') do (
    set LLVMTableGen=%%~I
)

if not exist "%BinariesDirectory%" (
    mkdir "%BinariesDirectory%"
)

pushd "%BinariesDirectory%"

cmake.exe ^
    -G "Visual Studio 16 2019" ^
    -A %GeneratorPlatform% ^
    -DCMAKE_INSTALL_PREFIX="%RootDirectory%\" ^
    -DLLVM_DEFAULT_TARGET_TRIPLE=%LLVMDefaultTargetTriple% ^
    -DLLVM_EXTERNAL_PROJECTS=coredistools ^
    -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR="%SourcesDirectory%\coredistools" ^
    -DLLVM_HOST_TRIPLE=%LLVMHostTriple% ^
    -DLLVM_TABLEGEN="%LLVMTableGen%" ^
    -DLLVM_TARGETS_TO_BUILD=AArch64;ARM;X86 ^
    -DLLVM_TOOL_COREDISTOOLS_BUILD=ON ^
    "%SourcesDirectory%\llvm-project\llvm"

popd

cmake.exe ^
  --build "%BinariesDirectory%" ^
  --target coredistools ^
  --config Release

if %ERRORLEVEL% neq 0 (
    echo coredistools compilation has failed
    exit /b 1
)

cmake.exe ^
    --install "%BinariesDirectory%" ^
    --component coredistools

exit /b 0
