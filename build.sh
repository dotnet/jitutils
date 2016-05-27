## Build and optionally publish sub projects
##
## This script will by default build release versions of the tools.
## If publish (-p) is requested it will create standalone versions of the
## tools in <script_root>/bin.
## 
## If frameworks (-f) are requested the script will create a scratch empty 'app'
## publish that contains the default frameworks.  See ./src/packages/project.json
## for specific version numbers.

function usage
{
    echo ""
    echo "build.sh [-b <BUILD TYPE>] [-f] [-h] [-p]"
    echo ""
    echo "    -b <BUILD TYPE> : Build type, can be Debug or Release."
    echo "    -h              : Show this message."
    echo "    -f              : Install default framework directory in <script_root>/fx."
    echo "    -p              : Publish utilities."
    echo ""
}

# defaults
buildType="Release"
publish=false
scriptDir="`dirname \"$0\"`"
platform="`dotnet --info | awk '/RID/ {print $2}'`"
# default install in 'bin' dir at script location
appInstallDir="$scriptDir/bin"
fxInstallDir="$scriptDir/fx"

# process for '-h', '-p', 'f', and '-b <arg>'
while getopts "hpfb:" opt; do
    case "$opt" in
    h)
        usage
        exit 0
        ;;
    b)  
        buildType=$OPTARG
        ;;
    p)  
        publish=true
        ;;
    f)  
        fx=true
        ;;
    esac
done

# declare the array of projects   
declare -a projects=(jit-dasm jit-diff jit-analyze cijobs)

# for each project either build or publish
for proj in "${projects[@]}"
do
    if [ "$publish" == true ]; then
        dotnet publish -c $buildType -o $appInstallDir/$proj ./src/$proj
    else
        dotnet build -c $buildType ./src/$proj
    fi
done

# set up fx if requested.

if [ "$fx" == true ]; then
    dotnet publish -c $buildType -o $fxInstallDir ./src/packages

    # remove package version of mscorlib* - refer to core root version for diff testing.
    rm $fxInstallDir/mscorlib*
fi
