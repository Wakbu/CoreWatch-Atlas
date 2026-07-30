FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --system --uid 10001 atlas
COPY src/CoreWatch.Atlas.Server/bin/Release/net10.0/ ./
RUN mkdir -p /var/lib/corewatch-atlas/keys && chown -R atlas:atlas /app /var/lib/corewatch-atlas
USER atlas
ENV ASPNETCORE_URLS=https://+:5443 \
    Atlas__Server__DatabasePath=/var/lib/corewatch-atlas/atlas.db \
    Atlas__Security__DataProtectionKeyPath=/var/lib/corewatch-atlas/keys \
    Atlas__Security__AllowLoopbackHttp=false
EXPOSE 5443
ENTRYPOINT ["dotnet","CoreWatch.Atlas.Server.dll"]