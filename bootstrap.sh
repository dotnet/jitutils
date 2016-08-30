#Quick and dirty bootstrap. 

function validate_url {
  if [[ `wget -S --spider $1  2>&1 | grep 'HTTP/1.1 200 OK'` ]];
  then
      return 0;
  else
      return 1;
  fi
}

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

if ! which -s clang-format || ! which -s clang-tidy || ! clang-format --version | grep -q 3.8 || ! clang-tidy --version | grep -q 3.8;
then

    info=$(dotnet --info)
    info=${output//RID:}
    info=${output// }

    clangFormatUrl=https://clrjit.blob.core.windows.net/clang-tools/${info}/clang-format

    if `validate_url ${clangFormatUrl} > /dev/null`;
    then
        echo "Downloading clang-format to bin directory"
        # download appropriate version of clang-format
        wget ${clangFormatUrl} -O bin/clang-format
        chmod 751 bin/clang-format
    fi

    clangTidyUrl=https://clrjit.blob.core.windows.net/clang-tools/${info}/clang-tidy

    if `validate_url ${clangTidyUrl} > /dev/null`;
    then
        echo "Downloading clang-tidy to bin directory"
        # download appropriate version of clang-tidy
        wget ${clangTidyUrl} -O bin/clang-tidy
        chmod 751 bin/clang-tidy
    fi

    if [ ! -f bin/clang-format -o ! -f bin/clang-tidy ]
    then
        echo "Either Clang-tidy or clang-format was not installed. Please install and put them on the PATH to use jit-format."
        echo "Tools can be found at http://llvm.org/releases/download.html#3.8.0"
    fi
else
    if ! clang-format --version | grep -q 3.8 || ! clang-tidy --version | grep -q 3.8;
    then
        echo "jit-format requires clang-format and clang-tidy version 3.8.*. Currently installed: "
        clang-format --version
        clang-tidy --version
        echo "Please install version 3.8.* and put the tools on the PATH to use jit-format."
        echo "Tools can be found at http://llvm.org/releases/download.html#3.8.0"
    fi
fi

popd

# set utilites in the current path

export PATH=$PATH:$root/jitutils/bin

echo "Done setting up!"
