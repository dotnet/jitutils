@echo off
setlocal EnableDelayedExpansion EnableExtensions

set TargetOSArchitecture=%1
set LLVMTargetsToBuild=AArch64;ARM;X86

if /i "%TargetOSArchitecture%" == "win-arm" (
    set GeneratorPlatform=ARM
    set LLVMDefaultTargetTriple=thumbv7-pc-windows-msvc
    set LLVMHostTriple=arm-pc-windows-msvc
    set LLVMTargetsToBuild=AArch64;ARM
) else if /i "%TargetOSArchitecture%" == "win-arm64" (
    set GeneratorPlatform=ARM64
    set LLVMHostTriple=aarch64-pc-windows-msvc
    set LLVMTargetsToBuild=AArch64;ARM
) else if /i "%TargetOSArchitecture%" == "win-x64" (
    set GeneratorPlatform=x64
    set LLVMHostTriple=x86_64-pc-windows-msvc
) else if /i "%TargetOSArchitecture%" == "win-x86" (
    set GeneratorPlatform=Win32
    set LLVMHostTriple=i686-pc-windows-msvc
) else (
    echo ERROR: Unknown target OS and architecture: %TargetOSArchitecture%
    exit /b 1
)

if not defined LLVMDefaultTargetTriple (
    set LLVMDefaultTargetTriple=%LLVMHostTriple%
)

set RootDirectory=%~dp0
set SourcesDirectory=%RootDirectory%src
set BinariesDirectory=%RootDirectory%obj\%TargetOSArchitecture%
set StagingDirectory=%RootDirectory%artifacts\%TargetOSArchitecture%

where /q cmake.exe

if %ERRORLEVEL% neq 0 (
    echo ERROR: cmake.exe is not found in the PATH
    exit /b 1
)

where /q llvm-tblgen.exe

if %ERRORLEVEL% neq 0 (
    echo ERROR: llvm-tblgen.exe is not found in the PATH
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
    -DCMAKE_INSTALL_PREFIX="%StagingDirectory%" ^
    -DLLVM_DEFAULT_TARGET_TRIPLE=%LLVMDefaultTargetTriple% ^
    -DLLVM_EXTERNAL_PROJECTS=coredistools ^
    -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR="%SourcesDirectory%\coredistools" ^
    -DLLVM_HOST_TRIPLE=%LLVMHostTriple% ^
    -DLLVM_INCLUDE_TESTS=OFF ^
    -DLLVM_TABLEGEN="%LLVMTableGen%" ^
    -DLLVM_TARGETS_TO_BUILD=%LLVMTargetsToBuild% ^
    -DLLVM_TOOL_COREDISTOOLS_BUILD=ON ^
    -DLLVM_USE_CRT_DEBUG=MTd ^
    -DLLVM_USE_CRT_RELEASE=MT ^
    "%SourcesDirectory%\llvm-project\llvm"

popd

if %ERRORLEVEL% neq 0 goto :CMakeNonZeroExitStatus

cmake.exe ^
  --build "%BinariesDirectory%" ^
  --target coredistools ^
  --config Release

if %ERRORLEVEL% neq 0 goto :CMakeNonZeroExitStatus

cmake.exe ^
    --install "%BinariesDirectory%" ^
    --component coredistools

if %ERRORLEVEL% neq 0 goto :CMakeNonZeroExitStatus

exit /b 0

:CMakeNonZeroExitStatus

echo ERROR: cmake exited with code %ERRORLEVEL%
exit /b 1
