@echo off

REM Bootstrap the jitutils tools:
REM 1. If this script is run not from within the jitutils directory (e.g., you've downloaded
REM    a copy of this file directly), then "git clone" the jitutils project first. Otherwise,
REM    if we can tell we're being run from within an existing "git clone", don't do that.
REM 2. Build the jitutils tools.
REM 3. Download (if necessary) clang-format.exe and clang-tidy.exe (used by the jit-format tool).

where /q dotnet.exe
if %errorlevel% NEQ 0 echo Can't find dotnet.exe! Please install this ^(e.g., from https://www.microsoft.com/net/core^) and add dotnet.exe to PATH && goto :EOF

where /q git.exe
if %errorlevel% NEQ 0 echo Can't find git.exe! Please add to PATH & goto :EOF

set __root=%~dp0
setlocal

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
if not %__root%==%gitroot% (
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

:done_build
popd

:: Add utilites to the current path, but only if not already there
endlocal
call :AddToPath %__root%bin
set __root=
goto :eof

REM ===================================================================
:clone_jitutils

pushd %__root%

:: Clone the jitutils repo

git clone https://github.com/dotnet/jitutils.git

pushd .\jitutils

call :build_jitutils

popd

popd

:: Add utilites to the current path
endlocal
call :AddToPath %__root%jitutils\bin
set __root=
goto :eof

REM ===================================================================
:build_jitutils

if not exist .\build.cmd echo Can't find build.cmd.&goto :eof

:: Pull in needed packages.  This works globally. (due to global.json)

dotnet restore

:: Build and publish all the utilties and frameworks

call .\build.cmd -p -f

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

GOTO CheckVersion

:DownloadTools

:: Download clang-format and clang-tidy
echo Downloading formatting tools
call powershell Invoke-WebRequest -Uri "https://clrjit.blob.core.windows.net/clang-tools/windows/clang-format.exe" -OutFile bin\clang-format.exe
call powershell Invoke-WebRequest -Uri "https://clrjit.blob.core.windows.net/clang-tools/windows/clang-tidy.exe" -OutFile bin\clang-tidy.exe

GOTO build_done

:CheckVersion

clang-format --version | findstr 3.8 > NUL
If %errorlevel% EQU 0 GOTO build_done

echo jit-format requires clang-format and clang-tidy version 3.8.*. Currently installed:
clang-format --version
clang-tidy --version
echo Please install version 3.8.* and put the tools on the PATH to use jit-format.
echo Tools can be found at http://llvm.org/releases/download.html#3.8.0

goto :DownloadTools

:build_done
goto :eof

REM ===================================================================
:AddToPath
set PATH | findstr /i %1 >nul
if %errorlevel% NEQ 0 set PATH=%PATH%;%1
goto :eof
