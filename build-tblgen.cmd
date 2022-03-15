@echo off
setlocal EnableDelayedExpansion EnableExtensions

set RootDirectory=%~dp0
set SourcesDirectory=%RootDirectory%src
set BinariesDirectory=%RootDirectory%obj
set StagingDirectory=%RootDirectory%bin

where /q cmake.exe

if %ERRORLEVEL% neq 0 (
    echo ERROR: cmake.exe is not found in the PATH
    exit /b 1
)

if not exist "%BinariesDirectory%" (
    mkdir "%BinariesDirectory%"
)

pushd "%BinariesDirectory%"

cmake.exe ^
    -G "Visual Studio 17 2022" ^
    -DCMAKE_INSTALL_PREFIX="%RootDirectory%\" ^
    -DLLVM_TARGETS_TO_BUILD=AArch64;ARM;X86 ^
    "%SourcesDirectory%\llvm-project\llvm"

popd

cmake.exe ^
    --build %BinariesDirectory% ^
    --target llvm-tblgen ^
    --config Release

if %ERRORLEVEL% neq 0 (
    echo llvm-tblgen compilation has failed
    exit /b 1
)

if not exist "%StagingDirectory%" (
    mkdir "%StagingDirectory%"
)

for /r "%BinariesDirectory%" %%I in (llvm-tblgen.ex?) do (
    if "%%~nxI" == "llvm-tblgen.exe" (
        xcopy "%%~I" "%StagingDirectory%"
        exit /b 0
    )
)

echo llvm-tblgen.exe is not found in "%BinariesDirectory%"
exit /b 1
