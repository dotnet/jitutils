@echo off
setlocal EnableDelayedExpansion EnableExtensions

set RootDirectory=%~dp0
set SourcesDirectory=%RootDirectory%src
set PackagesDirectory=%RootDirectory%artifacts\pkg

set BinariesDirectory=%~f1

if "%BinariesDirectory%"=="" (
    echo ERROR: Binaries directory is not specified
    exit /b 1
) else if not exist "%BinariesDirectory%" (
    echo ERROR: Binaries directory does not exist: %BinariesDirectory%
    exit /b 1
)

where /q nuget.exe

if %ERRORLEVEL% neq 0 (
    echo ERROR: nuget.exe is not found in the PATH
    exit /b 1
)

nuget.exe pack ^
    "%SourcesDirectory%\coredistools\.nuget\Microsoft.NETCore.CoreDisTools.nuspec" ^
    -OutputDirectory "%PackagesDirectory%" ^
    -BasePath %RootDirectory% ^
    -Properties BinariesDirectory="%BinariesDirectory%" ^
    -NonInteractive

if %ERRORLEVEL% neq 0 (
    echo ERROR: nuget pack exited with code %ERRORLEVEL%
    exit /b 1
)

exit /b 0
