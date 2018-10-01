FROM microsoft/dotnet:2.1.402-sdk-stretch
EXPOSE 9090

RUN apt-get update && apt-get install -y sudo &&\
  apt-get -y autoremove && apt-get -y autoclean && apt-get -y clean &&\
  rm -rf /tmp/* /var/tmp/* && rm -rf /var/lib/apt/lists/*

ENV TINI_VERSION v0.16.1
ADD https://github.com/krallin/tini/releases/download/${TINI_VERSION}/tini /tini

RUN chmod +x /tini
ENTRYPOINT ["/tini", "--"]

RUN mkdir -p /home/baget /home/baget/.nuget/NuGet &&\
    mkdir -p /var/baget/packages /var/baget/db &&\
    groupadd -g 1000 baget &&\
    useradd -d /home/baget -s /bin/bash -u 1000 -g baget baget &&\
    chown -R baget:baget /home/baget /var/baget/

ENV ASPNETCORE_ENVIRONMENT=Production \
    ApiKeyHash=658489D79E218D2474D049E8729198D86DB0A4AF43981686A31C7DCB02DC0900 \
    Storage__Type=FileSystem \
    Storage__Path=/var/baget/packages \
    Database__RunMigrations=true \
    Database__Type=Sqlite \
    Database__ConnectionString="Data Source=/var/baget/db/sqlite.db" \
    Search__Type=Database

COPY /src/BaGet/bin/Release/netcoreapp2.1/publish/ /app

ADD docker-scripts/run.sh /app/run.sh
RUN chmod +x /app/run.sh
CMD /app/run.sh
