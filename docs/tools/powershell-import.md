# Powershell import

For moving an existing repo or adding NuGet packages in bulk, this may help. It simply creates a .bat file which can be executed for import.

```powershell
Get-ChildItem -Path "C:\Temp\NewPackages" -Recurse | Where-Object {$_.Extension -eq ".nupkg"} | Select-Object FullName | ForEach-Object {

    $f += "dotnet nuget push -s http://myserver:5000/v3/index.json -k {apikey} " + """" + $_.FullName + """" + "`n"
}

# just see the output
# Write-Output $f

# create the file for execution
Write-Output $f | Out-File "C:\temp\BaGetImport.cmd" -Encoding ascii

```