# BaGet

## Getting Started

1. Install [.NET Core SDK](https://www.microsoft.com/net/download)
2. Download and extract [BaGet's latest release](https://github.com/loic-sharma/BaGet/releases)
3. Start the service with `dotnet BaGet.dll`
4. Browse `http://localhost:5000/` in your browser

## Pushing Packages

You can push a package using this command:

```
dotnet nuget push -s http://localhost:5000/v3/index.json newtonsoft.json.11.0.2.nupkg
```

## Using BaGet as a Symbol Server

You can use BaGet as a Symbol Server by uploading
[symbol packages](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg).
After you've pushed a package to BaGet, you can push its corresponding
symbol package using this command:

```
dotnet nuget push -s http://localhost:5000/v3/index.json symbol.package.1.0.0.snupkg
```

You will need to add the symbol location `http://localhost:5000/api/download/symbols` to load symbols from BaGet.
[Use this guide](https://docs.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger?view=vs-2017#configure-symbol-locations-and-loading-options) for Visual Studio.

## Running BaGet on Docker

If you'd like, you can run BaGet on Docker:

1. Pull the latest docker image:

```
docker pull loicsharma/baget
```

2. Create a file named `baget.env` with the content:

```
ApiKey=NUGET-SERVER-API-KEY
Storage__Type=FileSystem
Storage__Path=/var/baget/packages
Database__Type=Sqlite
Database__ConnectionString=Data Source=/var/baget/baget.db
Search__Type=Database
```

!!! info
    The `baget.env` file stores [BaGet's configuration](configuration) as environment
    variables. To learn how these configurations work, please refer to
    [ASP.NET Core's Configuration documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-2.1&tabs=basicconfiguration#configuration-by-environment).

3. Create a folder named `baget-data`
4. Run:

```
docker run --rm --name nuget-server -p 5555:80 --env-file baget.env -v "$(pwd)/baget-data:/var/baget" loicsharma/baget:latest
```

5. Push your first package with:

```
dotnet nuget push -s http://localhost:5555/v3/index.json -k NUGET-SERVER-API-KEY newtonsoft.json.11.0.2.nupkg
```

6. Open the URL `http://localhost:5555/` in your browser
