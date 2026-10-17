# Spellbound web app. Its PostgreSQL database runs in its own container (docker-compose.yml). See docs/deploy.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Spellbound.slnx Directory.Build.props ./
COPY src/Spellbound.Domain/Spellbound.Domain.csproj src/Spellbound.Domain/
COPY src/Spellbound.Web/Spellbound.Web.csproj src/Spellbound.Web/
RUN dotnet restore src/Spellbound.Web/Spellbound.Web.csproj
COPY src/ src/
RUN dotnet publish src/Spellbound.Web/Spellbound.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# pg_dump and pg_restore for backups. They must be the database server's major version or newer: this image is
# Ubuntu 24.04, whose client is PostgreSQL 16, so docker-compose.yml runs postgres:16.
RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-16 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Everything of the app's that must survive a new image lives on /data: the sign-in cookie keys and the backups.
RUN mkdir -p /data && chown -R app:app /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysDirectory=/data/keys \
    Backup__Directory=/data/backups \
    ForwardedHeaders__Enabled=true
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Spellbound.Web.dll"]
