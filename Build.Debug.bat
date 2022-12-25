nuget.exe restore BaGetService.sln
MSBuild.exe BaGetService.sln -m /property:Configuration=Debug
git add -A
git commit -a --allow-empty-message -m ''
git push