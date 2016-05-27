@echo off
setlocal

REM Build and optionally publish sub projects
REM
REM This script will by default build release versions of the tools.
REM If publish (-p) is requested it will create standalone versions of the
REM tools in <root>/src/<project>/<buildType>/netcoreapp1.0/<platform>/Publish/.
REM These tools can be installed via the install script (install.{sh|cmd}) in
REM this directory.

set scriptDir=%~dp0
set appInstallDir=%scriptDir%bin
set fxInstallDir=%scriptDir%fx
set buildType=Release
set publish=false
set fx=false

REM REVIEW: 'platform' is never used
for /f "usebackq tokens=1,2" %%a in (`dotnet --info`) do (
    if "%%a"=="RID:" set platform=%%b
)

:argLoop
if "%1"=="" goto :build

if /i "%1"=="-b" (
    set buildType=%2
    shift
    goto :nextArg
)
if /i "%1"=="-f" (
    set fx=true
    goto :nextArg
)
if /i "%1"=="-p" (
    set publish=true
    goto :nextArg
)
if /i "%1" == "-h" (
    goto :usage
)
echo ERROR: unknown argument %1
goto :usage

:nextArg
shift
goto :argLoop

:build

REM Declare the list of projects
set projects=jit-diff jit-dasm jit-analyze cijobs

REM Build each project
for %%p in (%projects%) do (
    if %publish%==true (
        dotnet publish -c %buildType% -o %appInstallDir%\%%p .\src\%%p
    ) else (
        dotnet build  -c %buildType% .\src\%%p
    )
)

if %fx%==true (
    dotnet publish -c %buildType% -o %fxInstallDir% .\src\packages
    
    @REM remove package version of mscorlib* - refer to core root version for diff testing
    if exist %fxInstallDir%\mscorlib* del /q %fxInstallDir%\mscorlib*
)

REM Done
exit /b 0

:usage
echo.
echo  build.cmd [-b ^<BUILD TYPE^>] [-f] [-h] [-p]
echo.
echo      -b ^<BUILD TYPE^> : Build type, can be Debug or Release.
echo      -h                : Show this message.
echo      -f                : Publish default framework directory in ^<script_root^>\fx.
echo      -p                : Publish utilities.
echo. 
exit /b 1
