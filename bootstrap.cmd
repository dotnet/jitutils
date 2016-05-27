:: Quick and dirty bootstrap. 

where /q dotnet.exe || echo Can't find dotnet.exe! Please add to PATH && goto :EOF
where /q git.exe || echo Can't find git.exe! Please add to PATH && goto :EOF
set root=%~dp0

:: Clone the mcgutils repo

git clone https://github.com/dotnet/jitutils.git

pushd .\jitutils

:: Pull in needed packages.  This works globally. (due to global.json)

dotnet restore

:: Build and publish all the utilties and frameworks

call .\build.cmd -p -f

popd

:: set utilites in the current path

set PATH=%PATH%;%root%\jitutils\bin\jit-dasm;%root%\jitutils\bin\jit-diff;%root%\jitutils\bin\jit-analyze;%root%\jitutils\bin\cijobs

:: lunch getstarted.md doc

start https://github.com/dotnet/jitutils/blob/master/doc/getstarted.md