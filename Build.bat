MSBuild.exe BaGet.sln -t:clean
MSBuild.exe BaGet.sln -t:restore -p:RestorePackagesConfig=true
MSBuild.exe BaGet.sln -m /property:Configuration=%Configuration% 
cd service
MSBuild.exe BaGetService.sln -t:clean
MSBuild.exe BaGetService.sln -t:restore -p:RestorePackagesConfig=true
MSBuild.exe BaGetService.sln -m /property:Configuration=%Configuration% 
git push
git add -A
git commit -a --allow-empty-message -m ''
git push
