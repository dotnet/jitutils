@echo off
setlocal

REM Build and optionally publish sub projects
REM
REM This script will by default build release versions of the tools.
REM If publish (-p) is requested it will create standalone versions of the
REM tools in <root>/src/<project>/bin/<platform>/<BuildType>/netcoreapp<version>.

set scriptDir=%~dp0
set appInstallDir=%scriptDir%bin
set fxInstallDir=%scriptDir%fx
set buildType=Release
set publish=false
set fx=false

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

REM Do as many builds as possible; don't stop on first failure (if any).
set __ExitCode=0

REM Declare the list of projects
set projects=jit-diff jit-dasm jit-analyze jit-format cijobs pmi jit-dasm-pmi jit-decisions-analyze

REM Build each project
for %%p in (%projects%) do (
    if %publish%==true (
        dotnet publish -c %buildType% -o %appInstallDir% .\src\%%p
        if errorlevel 1 echo ERROR: dotnet publish failed for .\src\%%p.&set __ExitCode=1

        copy .\wrapper.bat %appInstallDir%\%%p.bat
        if not exist %appInstallDir%\%%p.bat echo ERROR: Failed to copy wrapper script to %appInstallDir%\%%p.bat&set __ExitCode=1
    ) else (
        dotnet build -c %buildType% .\src\%%p
        if errorlevel 1 echo ERROR: dotnet build failed for .\src\%%p.&set __ExitCode=1
    )
)

if %fx%==true (
    @REM Need to expicitly restore 'packages' project for host runtime in order
    @REM for subsequent publish to be able to accept --runtime parameter to
    @REM publish it as standalone.

    dotnet restore --runtime %platform% .\src\packages
    if errorlevel 1 echo ERROR: dotnet restore of .\src\packages failed.&set __ExitCode=1

    dotnet publish -c %buildType% -o %fxInstallDir% --runtime %platform% .\src\packages
    if errorlevel 1 echo ERROR: dotnet publish of .\src\packages failed.&set __ExitCode=1
    
    @REM remove package version of mscorlib* - refer to core root version for diff testing
    if exist %fxInstallDir%\mscorlib* del /q %fxInstallDir%\mscorlib*
)

REM Done
exit /b %__ExitCode%

:usage
echo.
echo  build.cmd [-b ^<BUILD TYPE^>] [-f] [-h] [-p]
echo.
echo      -b ^<BUILD TYPE^>   : Build type, can be Debug or Release.
echo      -h                : Show this message.
echo      -f                : Publish default framework directory in ^<script_root^>\fx.
echo      -p                : Publish utilities.
echo. 
exit /b 1
