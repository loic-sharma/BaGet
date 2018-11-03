# BaGet

## Getting Started

1. Install [.NET Core](https://www.microsoft.com/net/download)
2. Run `git clone https://github.com/loic-sharma/BaGet.git`
3. Navigate to `.\BaGet\src\BaGet`
4. Start the service with `dotnet run`
5. Open the URL `http://localhost:5000/v3/index.json` in your browser

## Pushing Packages

You can push a package with this command:

```
dotnet nuget push -s http://localhost:5000/v3/index.json newtonsoft.json.11.0.2.nupkg
```

## Running BaGet on Docker

If you'd like, you can run BaGet on Docker:

1. Build the docker image:

```
docker build . -t baget
```

2. Create a file named `baget.env` with the content:

```
# The default API key is "NUGET-SERVER-API-KEY"
ApiKeyHash=658489D79E218D2474D049E8729198D86DB0A4AF43981686A31C7DCB02DC0900
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
docker run --rm --name nuget-server -p 5555:80 --env-file baget.env -v "$(pwd)/baget-data:/var/baget" baget:latest
```

5. Push your first package with:

```
dotnet nuget push -s http://localhost:5555/v3/index.json -k NUGET-SERVER-API-KEY newtonsoft.json.11.0.2.nupkg
```

6. Open the URL `http://localhost:5555/` in your browser