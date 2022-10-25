# Import packages from a Klondike

[Klondike](https://github.com/chriseldredge/Klondike) is an Ember front-end that builds on NuGet.Lucene for private NuGet package hosting.

!!! info
    Please refer to the [BaGet vs Klondike](../vs/klondike.md) page for reasons to upgrade to BaGet.

## Steps

Locate the source directory where Klondike internally stores the packages. You can find the actual path in the `Settings.config` file under `packagesPath` entry. For example:

```xml
<add key="packagesPath" value="C:\Klondike\Packages" />
```

Make sure that you've installed [nuget.exe](https://www.nuget.org/downloads). In PowerShell, run:

```ps1
$source = "C:\Klondike\Packages"
$destination = "http://localhost:5000/v3/index.json"
```

If you've [configured BaGet to require an API Key](https://loic-sharma.github.io/BaGet/configuration/#requiring-an-api-key), set it using [the `setapikey` command](https://docs.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-setapikey):

```ps1
& nuget.exe setapikey "MY-API-KEY" -Source $destination
```

Now run the following PowerShell script:

```ps1
$packages = (Get-ChildItem -Filter *.nupkg -Recurse $source).fullname
 
foreach($path in $packages)
{  
  Write-Host "nuget.exe push -Source $destination ""$path"""
  & nuget.exe push -Source $destination $path
}
```
