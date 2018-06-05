## Build and optionally publish sub projects
##
## This script will by default build release versions of the tools.
## If publish (-p) is requested it will create standalone versions of the
## tools in <script_root>/bin.
## 
## If frameworks (-f) are requested the script will create a scratch empty 'app'
## publish that contains the default frameworks.

function usage
{
    echo ""
    echo "build.sh [-b <BUILD TYPE>] [-f] [-h] [-p] [-t <TARGET>]"
    echo ""
    echo "    -b <BUILD TYPE> : Build type, can be Debug or Release."
    echo "    -h              : Show this message."
    echo "    -f              : Install default framework directory in <script_root>/fx."
    echo "    -p              : Publish utilities."
    echo "    -t <TARGET>     : Target framework. Default is netcoreapp2.0."
    echo ""
}

# defaults
buildType="Release"
publish=false
tfm=netcoreapp2.1
workingDir="$PWD"
cd "`dirname \"$0\"`"
scriptDir="$PWD"
cd $workingDir
platform="`dotnet --info | awk '/RID/ {print $2}'`"
# default install in 'bin' dir at script location
appInstallDir="$scriptDir/bin"
fxInstallDir="$scriptDir/fx"

# process for '-h', '-p', 'f', '-b <arg>', and '-t <arg>'
while getopts "hpfbt:" opt; do
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
    t)
        tfm=$OPTARG
        ;;
    esac
done

# declare the array of projects   
declare -a projects=(jit-dasm jit-diff jit-analyze jit-format cijobs pmi)

# for each project either build or publish
for proj in "${projects[@]}"
do
    if [ "$publish" == true ]; then
        dotnet publish -c $buildType -f $tfm -o $appInstallDir ./src/$proj
        cp ./wrapper.sh $appInstallDir/$proj
    else
        dotnet build -c $buildType -f $tfm ./src/$proj
    fi
done

# set up fx if requested.

if [ "$fx" == true ]; then
    # Need to explicitly restore 'packages' project for host runtime in order
    # for subsequent publish to be able to accept --runtime parameter to publish
    # it as standalone.
    dotnet restore --runtime $platform ./src/packages
    dotnet publish -c $buildType -f $tfm -o $fxInstallDir --runtime $platform ./src/packages

    # remove package version of mscorlib* - refer to core root version for diff testing.
    rm -f $fxInstallDir/mscorlib*
fi
