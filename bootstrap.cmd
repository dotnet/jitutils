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
set formatExists=%errorlevel%

where /Q clang-tidy
set tidyExists=%errorlevel%
echo blah
IF %formatExits% EQU 1 GOTO DownloadTools
IF %tidyExists% EQU 1 GOTO DownloadTools

GOTO SetPath

:DownloadTools

:: Download clang-format and clang-tidy
echo Downloading formatting tools
call powershell Invoke-WebRequest -Uri "https://clrjit.blob.core.windows.net/clang-tools/windows/clang-format.exe" -OutFile bin\clang-format.exe
call powershell Invoke-WebRequest -Uri "https://clrjit.blob.core.windows.net/clang-tools/windows/clang-tidy.exe" -OutFile bin\clang-tidy.exe

:SetPath

popd

:: set utilites in the current path

set PATH=%PATH%;%root%\jitutils\bin

popd

