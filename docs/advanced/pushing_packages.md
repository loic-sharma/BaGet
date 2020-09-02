# Pushing packages to BaGet

The following **bash** script can serve as a reference when pushing **nupkg** and **snupkg** solution project packages to BaGet - there can be more than one project in a solution and this script will push all of them:

```sh
# Authors
#	Aleksander Kovač https://github.com/akovac35
#
# This script will push all Release project packages for a solution to <BaGet host>:<BaGet port>.
#
# Specific solution projects can be excluded with the following setting:
#  <PropertyGroup>
#    <IsPackable>false</IsPackable>
#  </PropertyGroup>
#
# Project should include the following setting:
#  <PropertyGroup>
#    <IncludeSymbols>true</IncludeSymbols>
#    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
#  </PropertyGroup>
#
# Set the nuget api key first:
# nuget setApiKey <key> -Source <BaGet host>:<BaGet port>/v3/index.json
# nuget setApiKey <key> -Source <BaGet host>:<BaGet port>/api/v2/symbol
#
# Script arguments:
# -f yes	force push even if some or all tests fail
# -s yes	skip tests
#
# Script usage example:
#	cd MySolutionFolder
#	./release_packages.sh
#
# References
# https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push
# https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file

# Remove all entries from path containing %, such as %USERPROFILE%
export PATH=$(echo ${PATH} | awk -v RS=: -v ORS=: '/%/ {next} {print}' | sed 's/:*$//')

while getopts s:f: flag
do
    case "${flag}" in
        f) forceRelease=${OPTARG};;
		s) skipTest=${OPTARG};;
    esac
done

# Remove existing packages
find . -wholename '*/Release/*.nupkg' -execdir rm {} \;
find . -wholename '*/Release/*.snupkg' -execdir rm {} \;

# Create packages
dotnet clean
dotnet restore
if [[ -z $skipTest ]]; then
	dotnet test
	if [[ $? -ne 0 && -z $forceRelease ]]; then
		echo
		echo "Will not release packages because tests failed!"
		exit 1
	fi
fi

dotnet build -c Release
dotnet pack -c Release

# Will push packages nupkg and snupkg
find . -wholename '*/Release/*.nupkg' -execdir dotnet nuget push -s <BaGet host>:<BaGet port>/v3/index.json --skip-duplicate {} \;
```
