@echo off

REM Bootstrap the jitutils tools:
REM 1. If this script is run not from within the jitutils directory (e.g., you've downloaded
REM    a copy of this file directly), then "git clone" the jitutils project first. Otherwise,
REM    if we can tell we're being run from within an existing "git clone", don't do that.
REM 2. Build the jitutils tools.
REM 3. Download (if necessary) clang-format.exe and clang-tidy.exe (used by the jit-format tool).

set __ExitCode=0

where /q dotnet.exe
if %errorlevel% NEQ 0 echo Can't find dotnet.exe! Please install this ^(e.g., from https://www.microsoft.com/net/core^) and add dotnet.exe to PATH&set __ExitCode=1&goto :script_exit

where /q git.exe
if %errorlevel% NEQ 0 echo Can't find git.exe! Please add to PATH&set __ExitCode=1&goto :script_exit

set __root=%~dp0
setlocal ENABLEEXTENSIONS ENABLEDELAYEDEXPANSION

REM Are we already in the dotnet/jitutils repo? Or do we need to clone it? We look for build.cmd
REM in the current directory (which is the directory this script was invoked from).

if not exist %__root%build.cmd goto clone_jitutils

pushd %__root%

REM Check if build.cmd is in the root of the repo.
set __tempfile=%TEMP%\gittemp-%RANDOM%.txt
git rev-parse --show-toplevel >%__tempfile% 2>&1
if errorlevel 1 (
    echo Error: git failure:
    type %__tempfile%
    echo Cloning jitutils repo.
    del %__tempfile%
    popd
    goto clone_jitutils
)
set /P gitroot=<%__tempfile%
del %__tempfile%
set gitroot=%gitroot:/=\%
if not %gitroot:~-1%==\ set gitroot=%gitroot%\
if /I not %__root%==%gitroot% (
    echo It doesn't looks like bootstrap.cmd is at the root of the repo.
    echo Cloning jitutils repo.
    popd
    goto clone_jitutils
)

REM Is this actually the jitutils repo?
git remote -v | findstr /i /c:"/jitutils" >nul
if errorlevel 1 (
    echo It doesn't looks like we're in the jitutils repo.
    echo Cloning jitutils repo.
    popd
    goto clone_jitutils
)

REM Now go ahead and build it.
call :build_jitutils

popd

REM If the build failed, we need to check it here, before the "endlocal".
if %__ExitCode% NEQ 0 goto :script_exit

:: Add utilites to the current path, but only if not already there
endlocal
call :AddToPath %__root%bin
set __root=
goto :script_exit

REM ===================================================================
REM == This is top-level, not a "call".
REM ==
:clone_jitutils

pushd %__root%

:: Clone the jitutils repo

git clone https://github.com/dotnet/jitutils.git
if errorlevel 1 echo ERROR: clone failed.&set __ExitCode=1&goto :script_exit
if not exist .\jitutils echo ERROR: can't find jitutils directory.&set __ExitCode=1goto :script_exit

pushd .\jitutils

call :build_jitutils

popd

popd

REM If the build failed, we need to check it here, before the "endlocal".
if %__ExitCode% NEQ 0 goto :script_exit

:: Add utilites to the current path
endlocal
call :AddToPath %__root%jitutils\bin
set __root=
goto :script_exit

REM ===================================================================
:build_jitutils

if not exist .\build.cmd echo Can't find build.cmd.&set __ExitCode=1&goto :eof

:: Pull in needed packages.  This works globally (due to jitutils.sln).

dotnet restore
if errorlevel 1 echo ERROR: dotnet restore failed.&set __ExitCode=1&goto :eof

:: Build and publish all the utilities

call .\build.cmd -p
if errorlevel 1 echo ERROR: build failed.&set __ExitCode=1&goto :eof

:: Check to see if clang-format and clang-tidy are available. Since we're going
:: to add the 'bin' directory to the path, and they most likely already live there
:: if you're running bootstrap.cmd not for the first time, add 'bin' to the path
:: here (within the setlocal scope for now) before checking for them. Add 'bin'
:: at the end of the path, to prefer other user downloaded versions, if any.

set PATH=%PATH%;.\bin

where /Q clang-format
IF %errorlevel% NEQ 0 GOTO DownloadTools

where /Q clang-tidy
IF %errorlevel% NEQ 0 GOTO DownloadTools

REM We found the tools on the path; now make sure the versions are good.

clang-format --version | findstr 3.8 > NUL
If %errorlevel% EQU 0 GOTO build_done

echo jit-format requires clang-format and clang-tidy version 3.8.*. Currently installed:
clang-format --version
clang-tidy --version
echo Please install version 3.8.* and put the tools on the PATH to use jit-format.
echo Tools can be found at http://llvm.org/releases/download.html#3.8.0

:DownloadTools

:: Download clang-format and clang-tidy
echo Downloading formatting tools

call :download_url clang-format "https://clrjit.blob.core.windows.net/clang-tools/windows/clang-format.exe" bin\clang-format.exe
if %__ExitCode% NEQ 0 goto :eof

call :download_url clang-tidy "https://clrjit.blob.core.windows.net/clang-tools/windows/clang-tidy.exe" bin\clang-tidy.exe
if %__ExitCode% NEQ 0 goto :eof

:build_done
goto :eof

REM ===================================================================
REM Download helper, with retry on failure. Args:
REM %1 - friendly name of download
REM %2 - URL to download
REM %3 - target of download
REM
REM We'd like to use Invoke-WebRequest `-MaximumRetryCount 8 -RetryIntervalSec 15`, but those aren't
REM available on all versions of PowerShell.
:download_url
set _friendly_name=%1
set _url=%2
set _target_file=%3
set _retry_count=1
:start_download
echo call powershell Invoke-WebRequest -Uri %_url% -OutFile %_target_file%
call powershell Invoke-WebRequest -Uri %_url% -OutFile %_target_file%
if %errorlevel% EQU 0 goto :eof
echo ERROR: attempt #%_retry_count% failed to download %_friendly_name%.
if %_retry_count% GEQ 4 echo ERROR: giving up attempting to download %_friendly_name%.&set __ExitCode=1&goto :eof
echo Waiting 15 seconds to retry...
powershell -command "Start-Sleep -Seconds 15"
set /A _retry_count=_retry_count + 1
goto start_download

REM ===================================================================
:script_exit
REM Note, this entire line gets variable expanded before execution, so the "exit" use of %__ExitCode% gets expanded before it is cleared during execution.
set __ExitCode=&exit /b %__ExitCode%

REM ===================================================================
:AddToPath
set PATH | findstr /i %1 >nul
if %errorlevel% NEQ 0 set PATH=%PATH%;%1
goto :eof
