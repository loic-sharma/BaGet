# Import packages from a local feed

[Local feeds](https://docs.microsoft.com/en-us/nuget/hosting-packages/local-feeds) let you use a folder as a NuGet package source.

!!! info
    Please refer to the [BaGet vs local feeds](../vs/local-feeds.md) page for reasons to upgrade to BaGet.

## Steps

Make sure that you've installed [nuget.exe](https://www.nuget.org/downloads). In PowerShell, run:

```ps1
$source = "C:\path\to\local\feed"
$destination = "http://localhost:5000/v3/index.json"
```

If you've [configured BaGet to require an API Key](https://loic-sharma.github.io/BaGet/configuration/#requiring-an-api-key), set it using [the `setapikey` command](https://docs.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-setapikey):

```ps1
& nuget.exe setapikey "MY-API-KEY" -Source $destination
```

Now run the following PowerShell script:

```ps1
$packages = nuget list -AllVersions -Source $source

$packages | % {
  $id, $version = $_ -Split " "
  $nupkg = $id + "." + $version + ".nupkg"
  $path = [IO.Path]::Combine($source, $id, $version, $nupkg)

  Write-Host "nuget.exe push -Source $destination ""$path"""
  & nuget.exe push -Source $destination $path
}
```
