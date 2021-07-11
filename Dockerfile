FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
RUN curl -sL https://deb.nodesource.com/setup_10.x | bash -
RUN apt-get install -y nodejs
WORKDIR /src
COPY /src .
RUN dotnet restore BaGet
RUN dotnet build BaGet -c Release -o /app

FROM build AS publish
RUN dotnet publish BaGet -c Release -o /app

FROM base AS final
LABEL org.opencontainers.image.source="https://github.com/loic-sharma/BaGet"
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BaGet.dll"]
