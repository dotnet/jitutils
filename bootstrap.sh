#Quick and dirty bootstrap. 

if ! dotnet --info; then 
    echo "Can't find dotnet! Please add to PATH."
    return 1
fi

if ! git --version; then 
    echo "Can't find git! Please add to PATH."
    return 1
fi

root=$(pwd)

# Clone the mcgutils repo

git clone https://github.com/dotnet/jitutils.git

pushd ./jitutils

# Pull in needed packages.  This works globally. (due to global.json)

dotnet restore

# Build and publish all the utilties and frameworks

./build.sh -p -f

if ! which -s clang-format || ! which -s clang-tidy;
then

    info=$(dotnet --info)

    if echo $info | grep -q -i 'osx';
    then
        # download osx version of clang-tidy/format
        wget https://clrjit.blob.core.windows.net/clang-tools/osx/clang-format.exe -O bin/clang-format.exe
        wget https://clrjit.blob.core.windows.net/clang-tools/osx/clang-tidy.exe -O bin/clang-tidy.exe
    elif echo $info | grep -q -i 'ubuntu.16.04';
    then
        # download osx version of clang-tidy/format
        wget https://clrjit.blob.core.windows.net/clang-tools/ubuntu/16.04/clang-format.exe -O bin/clang-format.exe
        wget https://clrjit.blob.core.windows.net/clang-tools/ubuntu/16.04/clang-tidy.exe -O bin/clang-tidy.exe
    elif echo $info | grep -q -i 'ubuntu';
    then
        # download osx version of clang-tidy/format
        wget https://clrjit.blob.core.windows.net/clang-tools/ubuntu/14.04/clang-format.exe -O bin/clang-format.exe
        wget https://clrjit.blob.core.windows.net/clang-tools/ubuntu/14.04/clang-tidy.exe -O bin/clang-tidy.exe
    elif echo $info | grep -q -i 'centos';
    then
        # download osx version of clang-tidy/format
        wget https://clrjit.blob.core.windows.net/clang-tools/centos/clang-format.exe -O bin/clang-format.exe
        wget https://clrjit.blob.core.windows.net/clang-tools/centos/clang-tidy.exe -O bin/clang-tidy.exe
    else
        echo "Clang-tidy and clang-format not installed. Please install and put them on the PATH to use jit-format."
    fi
fi

popd

# set utilites in the current path

export PATH=$PATH:$root/jitutils/bin

echo "Done setting up!"
