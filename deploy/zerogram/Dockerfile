# syntax=docker/dockerfile:1
# Zerogram server image, built from source (needs NuGet access at build time).
# Without internet access on the build host use Dockerfile.prebuilt (see deploy/zerogram/README.fa.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-noble AS build
WORKDIR /src
COPY . .
RUN dotnet restore Ferrite/Ferrite.csproj -r linux-x64
RUN dotnet publish Ferrite/Ferrite.csproj -c Release -r linux-x64 --self-contained false --no-restore -p:UseAppHost=false --output /out

FROM mcr.microsoft.com/dotnet/runtime:10.0.0-noble
COPY --from=build /out /app
COPY deploy/zerogram/entrypoint.sh /app/entrypoint.sh
RUN chmod 0555 /app/entrypoint.sh \
 && mkdir -p /data && chown 1654:1654 /data && chmod 0700 /data
ENV FERRITE_PORT=5222 \
    FERRITE_DATA_PATH=/data/state \
    ZEROGRAM_HEALTH_PORT=8081 \
    DOTNET_gcServer=0 \
    DOTNET_TieredPGO=0 \
    DOTNET_EnableDiagnostics=0
WORKDIR /data
USER 1654:1654
VOLUME ["/data"]
EXPOSE 5222/tcp
HEALTHCHECK --interval=30s --timeout=6s --start-period=60s --retries=3 \
  CMD ["dotnet", "/app/Ferrite.dll", "--healthcheck"]
STOPSIGNAL SIGTERM
ENTRYPOINT ["/app/entrypoint.sh"]
