FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# appsettings.json binds Kestrel to localhost:6060, which is unreachable from outside the
# container; listen on every interface on the exposed port instead. An env file can override it.
ENV Kestrel__Endpoints__Http__Url=http://+:80
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# src/Directory.Build.props imports the repository-root Directory.Build.props, which signs every assembly with
# SignKey.snk: both must sit one folder above src/. Local data under src/ is kept out by .dockerignore.
WORKDIR /repo
COPY Directory.Build.props SignKey.snk ./
COPY src ./src
WORKDIR /repo/src
RUN dotnet restore BaGet
RUN dotnet build BaGet -c Release --no-restore -p:GeneratePackageOnBuild=false

FROM build AS publish
RUN dotnet publish BaGet -c Release --no-restore --no-build -o /app -p:GeneratePackageOnBuild=false

FROM base AS final
LABEL org.opencontainers.image.source="https://github.com/pieroviano/BaGet"
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BaGet.dll"]
