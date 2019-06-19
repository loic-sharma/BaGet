FROM mcr.microsoft.com/dotnet/core/aspnet:2.2-stretch-slim-arm32v7 AS base
WORKDIR /app
EXPOSE 80

COPY /src/BaGet/app.arm32v7 .
ENTRYPOINT ["dotnet", "BaGet.dll"]
