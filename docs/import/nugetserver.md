# Import NuGet.Server packages

[NuGet.Server](https://github.com/NuGet/NuGet.Server) is a standalone A NuGet, Maven, NVM, Docker and symbol. It is strongly recommended that you upgrade to BaGet Extension if you use NuGet.Server. Feel free to open a [GitHub issue](https://github.com/loic-sharma/BaGet/issues) if you need help migrating.

!!! info
    Please refer to the [BaGet Extension vs NuGet.Server](../vs/nugetserver.md) page for reasons to upgrade to BaGet Extension.

## Steps

Make sure that you've installed [nuget.exe](https://www.nuget.org/downloads). In PowerShell, run:

```ps1
$source = "<NuGet.Server package source>"
$destination = "<BaGet Extension package source>"
```

If BaGet Extension requires an API key (a global `ApiKey`, or [accounts](../configuration.md#accounts-package-owners-and-api-keys), on by default), set it using [the `setapikey` command](https://docs.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-setapikey):

```ps1
& nuget.exe setapikey "MY-API-KEY" -Source $destination
```

With accounts on, use a user API key from `/account/apikeys`; the account must own each package, or its ID must be new.

Now run the following PowerShell script:

```ps1
if (!(Test-Path "Web.config")) {
  throw "Please run this script in the same directory as NuGet.Server's Web.config file"
}

(& nuget.exe list -AllVersions -Source $source).Split([Environment]::NewLine) | % {
  $id = $_.Split(" ")[0].Trim()
  $version = $_.Split(" ")[1].Trim()

  $path = [IO.Path]::Combine("Packages", $id, $version, "${id}.${version}.nupkg")

  Write-Host "nuget.exe push -Source $destination ""$path"""
  & nuget.exe push -Source $destination $path
}
```
