# Spellbound web app with its embedded RavenDB server. See docs/deploy.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Spellbound.slnx Directory.Build.props ./
COPY src/Spellbound.Domain/Spellbound.Domain.csproj src/Spellbound.Domain/
COPY src/Spellbound.Web/Spellbound.Web.csproj src/Spellbound.Web/
RUN dotnet restore src/Spellbound.Web/Spellbound.Web.csproj
COPY src/ src/
RUN dotnet publish src/Spellbound.Web/Spellbound.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Everything that must survive a new image lives on /data: the database and the sign-in cookie keys.
# RavenDB also writes its logs and package caches next to its binaries, so the app user needs those two folders.
RUN mkdir -p /data /app/RavenDB /app/RavenDBServer/Packages \
    && chown -R app:app /data /app/RavenDB /app/RavenDBServer/Packages
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    RavenDb__DataDirectory=/data/raven \
    DataProtection__KeysDirectory=/data/keys \
    Backup__Directory=/data/backups \
    ForwardedHeaders__Enabled=true
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Spellbound.Web.dll"]
