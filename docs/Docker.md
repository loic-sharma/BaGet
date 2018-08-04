# Running BaGet on Docker

1. Build the docker image:

```
docker build . -t baget
```

2. Create a file named `baget.env` with the content:

```
# The default API key is "NUGET-SERVER-API-KEY"
ASPNETCORE_ENVIRONMENT=Development
ApiKeyHash=658489D79E218D2474D049E8729198D86DB0A4AF43981686A31C7DCB02DC0900
Storage__Type=FileSystem
Storage__Path=/var/baget/packages
Database__Type=Sqlite
Database__ConnectionString=Data Source=/var/baget/baget.db
Search__Type=Database
```

3. Create a folder named `baget-data`
4. Run:

```
docker run --rm --name nuget-server -p 5555:80 --env-file baget.env -v "$(pwd)/baget-data:/var/baget" baget-nuget-server:latest
```

5. Push your first package with:

```
dotnet nuget push -s http://localhost:5555/v3/index.json -k NUGET-SERVER-API-KEY newtonsoft.json.11.0.2.nupkg
```

6. Browse your packages at `http://localhost:5555/`