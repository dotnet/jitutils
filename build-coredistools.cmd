@echo off
setlocal EnableDelayedExpansion EnableExtensions

set TargetOSArchitecture=%1
set LLVMTargetsToBuild=AArch64;ARM;X86

if /i "%TargetOSArchitecture%" == "win-arm64" (
    set GeneratorPlatform=ARM64
    set LLVMHostTriple=aarch64-pc-windows-msvc
) else if /i "%TargetOSArchitecture%" == "win-x64" (
    set GeneratorPlatform=x64
    set LLVMHostTriple=x86_64-pc-windows-msvc
) else if /i "%TargetOSArchitecture%" == "win-x86" (
    set GeneratorPlatform=Win32
    set LLVMHostTriple=i686-pc-windows-msvc
    set LLVMTargetsToBuild=ARM;X86
) else (
    echo ERROR: Unknown target OS and architecture: %TargetOSArchitecture%
    echo        Use one of win-arm64, win-x64, win-x86.
    exit /b 1
)

set BuildFlavor=%2
if "%BuildFlavor%"=="" set BuildFlavor=Release

if /i "%BuildFlavor%" == "Release" (
    @REM ok
) else if /i "%BuildFlavor%" == "Debug" (
    @REM ok
) else (
    echo ERROR: Unknown build flavor: %BuildFlavor%
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

if %ERRORLEVEL% equ 0 goto found_llvm_tblgen

@REM We expect it to be in the `bin` directory, so add that to the PATH if it's there.
if not exist %RootDirectory%bin\llvm-tblgen.exe (
    echo ERROR: llvm-tblgen.exe is not found in the PATH
    exit /b 1
)

echo Found llvm-tblgen.exe in %RootDirectory%bin; adding that directory to PATH.
set PATH=%RootDirectory%bin;%PATH%

:found_llvm_tblgen

for /f %%I in ('where llvm-tblgen.exe') do (
    set LLVMTableGen=%%~I
)

if not exist "%BinariesDirectory%" (
    mkdir "%BinariesDirectory%"
)

pushd "%BinariesDirectory%"
    
@REM To use the Debug CRT, use:
@REM    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreadedDebug
@REM
@REM To build a Debug version (asserts, debug info):
@REM    -DCMAKE_BUILD_TYPE=Debug
@REM To build a Release version (no asserts, no debug info):
@REM    -DCMAKE_BUILD_TYPE=Release
@REM
@REM Misc. LLVM CMake documentation: https://llvm.org/docs/CMake.html

cmake.exe ^
    -G "Visual Studio 17 2022" ^
    -A %GeneratorPlatform% ^
    -DCMAKE_INSTALL_PREFIX="%StagingDirectory%" ^
    -DCMAKE_BUILD_TYPE=%BuildFlavor% ^
    -DLLVM_DEFAULT_TARGET_TRIPLE=%LLVMDefaultTargetTriple% ^
    -DLLVM_EXTERNAL_PROJECTS=coredistools ^
    -DLLVM_EXTERNAL_COREDISTOOLS_SOURCE_DIR="%SourcesDirectory%\coredistools" ^
    -DLLVM_HOST_TRIPLE=%LLVMHostTriple% ^
    -DLLVM_INCLUDE_TESTS=OFF ^
    -DLLVM_TABLEGEN="%LLVMTableGen%" ^
    -DLLVM_TARGETS_TO_BUILD=%LLVMTargetsToBuild% ^
    -DLLVM_TOOL_COREDISTOOLS_BUILD=ON ^
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded ^
    "%SourcesDirectory%\llvm-project\llvm"

popd

if %ERRORLEVEL% neq 0 goto :CMakeNonZeroExitStatus

@REM Use `--config Release` for release build, `--config Debug` for debug build

cmake.exe ^
  --build "%BinariesDirectory%" ^
  --target coredistools ^
  --config %BuildFlavor%

if %ERRORLEVEL% neq 0 goto :CMakeNonZeroExitStatus

cmake.exe ^
    --install "%BinariesDirectory%" ^
    --component coredistools

if %ERRORLEVEL% neq 0 goto :CMakeNonZeroExitStatus

exit /b 0

:CMakeNonZeroExitStatus

echo ERROR: cmake exited with code %ERRORLEVEL%
exit /b 1
