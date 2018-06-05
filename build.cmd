@echo off
setlocal

REM Build and optionally publish sub projects
REM
REM This script will by default build release versions of the tools.
REM If publish (-p) is requested it will create standalone versions of the
REM tools in <root>/src/<project>/<buildType>/netcoreapp2.1/<platform>/Publish/.
REM These tools can be installed via the install script (install.{sh|cmd}) in
REM this directory.

set scriptDir=%~dp0
set appInstallDir=%scriptDir%bin
set fxInstallDir=%scriptDir%fx
set buildType=Release
set publish=false
set fx=false
set tfm=netcoreapp2.1

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
if /i "%1"=="-t" (
    set tfm=%2
    shift
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
set projects=jit-diff jit-dasm jit-analyze jit-format cijobs pmi

REM Build each project
for %%p in (%projects%) do (
    if %publish%==true (
        dotnet publish -c %buildType% -f %tfm% -o %appInstallDir% .\src\%%p
        copy .\wrapper.bat %appInstallDir%\%%p.bat
    ) else (
        dotnet build -c %buildType% -f %tfm% .\src\%%p
    )
)

if %fx%==true (
    @REM Need to expicitly restore 'packages' project for host runtime in order
    @REM for subsequent publish to be able to accept --runtime parameter to
    @REM publish it as standalone.
    dotnet restore --runtime %platform% .\src\packages
    dotnet publish -c %buildType% -f %tfm% -o %fxInstallDir% --runtime %platform% .\src\packages
    
    @REM remove package version of mscorlib* - refer to core root version for diff testing
    if exist %fxInstallDir%\mscorlib* del /q %fxInstallDir%\mscorlib*
)

REM Done
exit /b 0

:usage
echo.
echo  build.cmd [-b ^<BUILD TYPE^>] [-f] [-h] [-p] [-t ^<TARGET^>]
echo.
echo      -b ^<BUILD TYPE^>   : Build type, can be Debug or Release.
echo      -h                : Show this message.
echo      -f                : Publish default framework directory in ^<script_root^>\fx.
echo      -p                : Publish utilities.
echo      -t ^<TARGET^>       : Target framework. Default is netcoreapp2.0.
echo. 
exit /b 1
