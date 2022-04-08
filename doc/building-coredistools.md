# Building coredistools

## Building on Windows with Visual Studio 2022

1. Checkout the jitutils repository:
```
git clone https://github.com/dotnet/jitutils.git
cd jitutils
```

2. Checkout the LLVM project repository:
```
git clone --depth 1 --branch llvmorg-13.0.1 https://github.com/llvm/llvm-project.git src\llvm-project
```

4. Build `llvm-tblgen.exe`:
```
build-tblgen.cmd
```

5. Add the `bin` subdirectory to the `PATH`:
```
set "PATH=%cd%\bin;%PATH%"
````

6. Build `coredistools.dll` for a combination of target OS and architecture.

For example, the following command will result in `coredistools.dll` binary that can be run on Windows x64:
```
build-coredistools.cmd win-x64
```

The file will be copied to subdirectory `artifacts` after the command finishes:
```
dir /A:-D /B /S artifacts\win-x64
F:\echesako\git\jitutils\artifacts\win-x64\bin\coredistools.dll
F:\echesako\git\jitutils\artifacts\win-x64\lib\coredistools.lib
```

7. Build Windows x86, Windows ARM and Windows ARM64 binaries:
```
build-coredistools.cmd win-x86
build-coredistools.cmd win-arm
build-coredistools.cmd win-arm64
```

### Building Debug binaries

The `build-coredistools.cmd` script is set up to build a Release build. To create a Debug build with a PDB file,
for debugging, change the `--config Release` line to `--config Debug`.

## Building on Linux

1. Checkout the jitutils repository:
```
git clone https://github.com/dotnet/jitutils.git
cd jitutils
```

2. Checkout the LLVM project repository:
```
git clone --depth 1 --branch llvmorg-13.0.1 https://github.com/llvm/llvm-project.git src/llvm-project
```

3. Download LLVM release from GitHub:

```
python3 eng/download-llvm-release.py -release llvmorg-13.0.1 -os linux
```

4. Locate under the current directory file `llvm-tblgen`
```
find -name llvm-tblgen
./clang+llvm-13.0.1-x86_64-linux-gnu-ubuntu-18.04/bin/llvm-tblgen
```
and add its parent directory location to the `PATH`:

```
export PATH=$(pwd)/clang+llvm-13.0.1-x86_64-linux-gnu-ubuntu-18.04/bin:$PATH
```

5. Build `libcoredistools.so` for Linux x64:
```
./build-coredistools.sh linux-x64
```

The file will be copied to subdirectory `artifacts` after the command finishes:
```
find ./artifacts -name libcoredistools.so
./artifacts/linux-x64/bin/libcoredistools.so
./artifacts/linux-x64/lib/libcoredistools.so
```

6. Build `libcoredistools.so` for Linux arm64 under Docker:

```
docker run -it --rm --entrypoint /bin/bash -v ~/git/jitutils:/opt/code -w /opt/code -u $(id -u):$(id -g) mcr.microsoft.com/dotnet-buildtools/prereqs:ubuntu-18.04-cross-arm64-20220312201346-b2c2436
export PATH=$(pwd)/clang+llvm-13.0.1-x86_64-linux-gnu-ubuntu-18.04/bin:$PATH
./build-coredistools.sh linux-arm64 /crossrootfs/arm64
```

7. Build `libcoredistools.so` for Linux arm under Docker:
```
docker run -it --rm --entrypoint /bin/bash -v ~/git/jitutils:/opt/code -w /opt/code -u $(id -u):$(id -g) mcr.microsoft.com/dotnet-buildtools/prereqs:ubuntu-18.04-cross-20220312201346-b9de666
export PATH=$(pwd)/clang+llvm-13.0.1-x86_64-linux-gnu-ubuntu-18.04/bin:$PATH
./build-coredistools.sh linux-arm /crossrootfs/arm
```

