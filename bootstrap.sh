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

popd

# set utilites in the current path

export PATH=$PATH:$root/jitutils/bin

echo "Done setting up!"
