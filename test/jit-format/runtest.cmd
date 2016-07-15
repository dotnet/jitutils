rem Run jit-format tool on test.cpp and compare the output to test-fixed.cpp

setlocal

set testroot=%~dp0
set testrootReplace=%testroot:\=/%
set jitUtilsBin=%testroot%\..\..\bin

copy test.cpp test-pre.cpp

echo [> %testroot%\compile_commands.json
echo     {>> %testroot%\compile_commands.json
echo         "directory": "%testrootReplace%",>> %testroot%\compile_commands.json
echo         "command": "cl.exe %testrootReplace%test.cpp",>> %testroot%\compile_commands.json
echo         "file": "%testrootReplace%test.cpp">> %testroot%\compile_commands.json
echo     }>> %testroot%\compile_commands.json
echo ]>> %testroot%\compile_commands.json

rem Because we specified full paths, we will ignore the --coreclr argument, but it is required
call %jitUtilsBin%\jit-format.cmd --fix --compile-commands %testroot%\compile_commands.json --coreclr %testroot% %testroot%\test.cpp

timeout 1 > NUL

fc test.cpp test-fixed.cpp

if "%errorlevel%" == "0" (
    echo Test Passed
) else (
    echo Test Failed
)

move /Y test-pre.cpp test.cpp
