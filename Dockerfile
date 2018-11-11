FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
ENV IS_DOCKER true
COPY . .
RUN dotnet restore src/BaGet
RUN dotnet build src/BaGet -c Release -o /app

FROM build AS publish
RUN dotnet publish src/BaGet -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BaGet.dll"]
