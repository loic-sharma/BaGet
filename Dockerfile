FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY /src .
COPY /Directory.Packages.props .

RUN dotnet restore BaGetter
RUN dotnet build BaGetter -c Release -o /app

FROM build AS publish
RUN dotnet publish BaGetter -c Release -o /app

FROM base AS final
LABEL org.opencontainers.image.source="https://github.com/bagetter/BaGetter"
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BaGetter.dll"]
