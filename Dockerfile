FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build

WORKDIR /src
COPY src/BaGet/BaGet.csproj src/BaGet/
COPY src/BaGet.Core/BaGet.Core.csproj src/BaGet.Core/
COPY src/BaGet.Web/BaGet.Web.csproj src/BaGet.Web/
COPY src/BaGet.Azure/BaGet.Azure.csproj src/BaGet.Azure/
RUN dotnet restore src/BaGet/BaGet.csproj
COPY . .
WORKDIR /src/src/BaGet
RUN dotnet build BaGet.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish BaGet.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BaGet.dll"]
