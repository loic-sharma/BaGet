FROM microsoft/dotnet:2.1.1-aspnetcore-runtime-alpine

WORKDIR /src

COPY out .

ENV ASPNETCORE_URLS http://0.0.0.0:5000
EXPOSE 5000

CMD ["dotnet", "BaGet.dll"]