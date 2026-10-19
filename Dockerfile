FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/EternalRealms.ServerHost -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /srv/eternal-realms
COPY --from=build /app ./bin
COPY content ./content
COPY db ./db
ENV ER_CONTENT=/srv/eternal-realms/content
EXPOSE 7777/udp
# Die Datenbankmigrationen werden über den Pfad db/migrations neben content gefunden.
ENTRYPOINT ["dotnet", "bin/EternalRealms.ServerHost.dll"]
