FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
ARG APP_BUILD=Release
WORKDIR /src
COPY . .
RUN dotnet restore src/BaGet
RUN dotnet build src/BaGet -c ${APP_BUILD} -o /app

FROM build AS publish
RUN dotnet publish src/BaGet -c ${APP_BUILD} -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BaGet.dll"]
