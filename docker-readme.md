# Build docker:

```
docker build . -t baget-nuget-server
```

# Run docker container:

Create file with environments or pass environment variables through command line via -e VAR=VALUE.

```baget-nuget-server.env
ASPNETCORE_ENVIRONMENT=Development
ApiKeyHash=<NUGET-SERVER-API-KEY>
Storage__Type=FileSystem
Storage__Path=/nuget-data/packages
Database__Type=Sqlite
Database__ConnectionString=Data Source=/nuget-data/baget.db
Search__Type=Database
```

```
docker run --name nuget-server -p 5555:80 --env-file baget-nuget-server.env -v c:/nuget-data:/nuget-data baget-nuget-server:latest
```

TODO:
- Replace port 5555 with appropriate one.
- Replace "c:/nuget-data" with appropriate path for desired environment and OS.
