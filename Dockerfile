# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY LightWeightOverlay/ LightWeightOverlay/
COPY LWOServer/ LWOServer/
COPY CacheDragon/ CacheDragon/
COPY lol_picksandbans/ lol_picksandbans/
RUN dotnet publish LWOServer/LWOServer.csproj -c Release -o /out \
 && dotnet publish lol_picksandbans/lol_picksandbans.csproj -c Release -o /out/apps/lol_picksandbans \
 && dotnet publish CacheDragon/cache_dragon.csproj -c Release -o /out/apps/cache_dragon \
 && cp -r lol_picksandbans/web /out/apps/lol_picksandbans/web

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out/ ./
ENV LWO_IP=* \
    LWO_DISABLE_LCU=1 \
    LWO_STATE=/data/globalstate.json
VOLUME ["/data", "/app/apps/cache_dragon/cache"]
EXPOSE 9345 9346
ENTRYPOINT ["sh", "-c", "mkdir -p /data && { [ -f /data/globalstate.json ] || cp /app/globalstate.json /data/globalstate.json; } && exec dotnet LWOServer.dll"]
