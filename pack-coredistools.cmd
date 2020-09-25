@echo off
setlocal EnableDelayedExpansion EnableExtensions

set RootDirectory=%~dp0
set SourcesDirectory=%RootDirectory%src
set PackagesDirectory=%RootDirectory%artifacts\pkg

where /q nuget.exe

if %ERRORLEVEL% neq 0 (
    echo ERROR: nuget.exe is not found in the PATH
    exit /b 1
)

if /i "%1"=="portable" (
    nuget.exe pack ^
        "%SourcesDirectory%\coredistools\.nuget\Microsoft.NETCore.CoreDisTools.nuspec" ^
        -OutputDirectory "%PackagesDirectory%" ^
        -BasePath %RootDirectory% ^
        -NonInteractive

    if %ERRORLEVEL% neq 0 goto :NuGetNonZeroExitStatus
    exit /b 0
)

call :EnsureSupportedTargetOSArchitecture %1

if %ERRORLEVEL% neq 0 (
    echo ERROR: Unknown target OS and architecture: %TargetOSArchitecture%
    exit /b 1
)

set TargetOSArchitecture=%1
set BinariesDirectory=%~2

if "%BinariesDirectory%"=="" (
    set BinariesDirectory=%RootDirectory%artifacts\%TargetOSArchitecture%\bin
)

nuget.exe pack ^
    "%SourcesDirectory%\coredistools\.nuget\runtime.%TargetOSArchitecture%.Microsoft.NETCore.CoreDisTools.nuspec" ^
    -OutputDirectory "%PackagesDirectory%" ^
    -BasePath %RootDirectory% ^
    -Properties BinariesDirectory="%BinariesDirectory%" ^
    -NonInteractive

if %ERRORLEVEL% neq 0 goto :NuGetNonZeroExitStatus

exit /b 0

:NuGetNonZeroExitStatus

echo ERROR: nuget pack exited with code %ERRORLEVEL%
exit /b 1

:EnsureSupportedTargetOSArchitecture

for %%I in (linux-arm linux-arm64 linux-x64 osx-x64 win-arm win-arm64 win-x64 win-x86) do (if /i "%%I"=="%1" exit /b 0)
exit /b 1
