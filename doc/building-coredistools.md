# Building coredistools

## Building on Windows with Visual Studio 2022

1. Checkout the jitutils repository:
```
git clone https://github.com/dotnet/jitutils.git
cd jitutils
```

2. Checkout the LLVM project repository into a subdirectory named src/llvm-project:
```
git clone --depth 1 --branch llvmorg-17.0.6 https://github.com/llvm/llvm-project.git src\llvm-project
```

3. Build `llvm-tblgen.exe`:
```
build-tblgen.cmd
```

This builds llvm-tblgen.exe and puts it in the `bin` subdirectory.

4. Add the `bin` subdirectory to the `PATH`:
```
set "PATH=%cd%\bin;%PATH%"
```

This puts the just built lldb-tblgen.exe on the `PATH`.

5. Build `coredistools.dll` for a combination of target OS and architecture.

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

6. Build Windows x86, Windows ARM and Windows ARM64 binaries:
```
build-coredistools.cmd win-x86
build-coredistools.cmd win-arm
build-coredistools.cmd win-arm64
```

### Building Debug binaries

The `build-coredistools.cmd` script is set up to build a Release build. To create a Debug build with a PDB file,
for debugging, change the `--config Release` line to `--config Debug`.

## Building on Linux / Mac

1. Checkout the jitutils repository:
```
git clone https://github.com/dotnet/jitutils.git
cd jitutils
```

2. Checkout the LLVM project repository:
```
git clone --depth 1 --branch llvmorg-17.0.6 https://github.com/llvm/llvm-project.git src/llvm-project
```

3. Build `llvm-tblgen` in Docker:
```
docker run -it --rm --entrypoint /bin/bash -v ~/git/jitutils:/opt/code -w /opt/code -u $(id -u):$(id -g) mcr.microsoft.com/dotnet-buildtools/prereqs:cbl-mariner-2.0-cross-amd64
./build-tblgen.sh linux-x64 /crossrootfs/x64
```

This builds llvm-tblgen and puts it in the `bin` subdirectory.

4. Add `llvm-tblgen` to the PATH:
```
export PATH=$(pwd)/clang+llvm-17.0.6-x86_64-linux-gnu-ubuntu-18.04/bin:$PATH
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
docker run -it --rm --entrypoint /bin/bash -v ~/git/jitutils:/opt/code -w /opt/code -u $(id -u):$(id -g) mcr.microsoft.com/dotnet-buildtools/prereqs:cbl-mariner-2.0-cross-arm64
export PATH=$(pwd)/clang+llvm-17.0.6-x86_64-linux-gnu-ubuntu-18.04/bin:$PATH
./build-coredistools.sh linux-arm64 /crossrootfs/arm64
```

7. Build `libcoredistools.so` for Linux arm under Docker:
```
docker run -it --rm --entrypoint /bin/bash -v ~/git/jitutils:/opt/code -w /opt/code -u $(id -u):$(id -g) mcr.microsoft.com/dotnet-buildtools/prereqs:cbl-mariner-2.0-cross-arm
export PATH=$(pwd)/clang+llvm-17.0.6-x86_64-linux-gnu-ubuntu-18.04/bin:$PATH
./build-coredistools.sh linux-arm /crossrootfs/arm
```
