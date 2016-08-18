:: Quick and dirty bootstrap. 

where /q dotnet.exe || echo Can't find dotnet.exe! Please add to PATH && goto :EOF
where /q git.exe || echo Can't find git.exe! Please add to PATH && goto :EOF
set root=%~dp0
pushd %root%

:: Clone the mcgutils repo

git clone https://github.com/dotnet/jitutils.git

pushd .\jitutils

:: Pull in needed packages.  This works globally. (due to global.json)

dotnet restore

:: Build and publish all the utilties and frameworks

call .\build.cmd -p -f

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

:CheckVersion

clang-format --version | findstr 3.8 > NUL
If %errorlevel% EQU 0 GOTO SetPath

echo jit-format requires clang-format and clang-tidy version 3.8.*. Currently installed:
clang-format --version
clang-tidy --version
echo Please install version 3.8.* and put the tools on the PATH to use jit-format.
echo Tools can be found at http://llvm.org/releases/download.html#3.8.0

:SetPath

popd

:: set utilites in the current path

set PATH=%PATH%;%root%\jitutils\bin

popd

