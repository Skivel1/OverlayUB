# Despliegue en Portainer (1 usuario)

## 1. Repo
Sube estos archivos a tu fork (rama `master`). El Dockerfile y los compose van en la raiz.

## 2. Stack en Portainer
Stacks -> Add stack -> **Repository** -> URL de tu fork, referencia `refs/heads/master`.

### Opcion A: red local (sin dominio) -> compose path `docker-compose.yml`
Variables: `LWO_TOKEN` (secreto largo).
- Overlay (OBS):  `http://IP_SERVIDOR:9345/lol_picksandbans/overlay.html?token=LWO_TOKEN`
- Admin:          `http://IP_SERVIDOR:9345/lol_picksandbans/admin.html?token=LWO_TOKEN`
- Agente:         `LWOAgent.exe "ws://IP_SERVIDOR:9346?token=LWO_TOKEN"`
Solo sirve si OBS y el PC del jugador estan en la misma red que el servidor.

### Opcion B: dominio + HTTPS -> compose path `docker-compose.caddy.yml`
Variables: `LWO_TOKEN` y `DOMAIN` (DNS apuntando al servidor; puertos 80/443 libres).
- Overlay (OBS):  `https://DOMAIN/lol_picksandbans/overlay.html?token=LWO_TOKEN`
- Admin:          `https://DOMAIN/lol_picksandbans/admin.html?token=LWO_TOKEN`
- Agente:         `LWOAgent.exe "wss://DOMAIN/ws?token=LWO_TOKEN"`
Si ya tienes NPM/Traefik en 80/443, usa A y publica 9345/9346 detras de tu proxy
(`/` -> 9345, `/ws` -> 9346 quitando el prefijo `/ws`).

## 3. Agente (PC con League)
GitHub -> Actions -> "Build LWOAgent" -> Run workflow -> descarga el artefacto `LWOAgent-win-x64`.
(o por SSH: `docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet publish LWOAgent/LWOAgent.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o /src/agent-out`)
La URL tambien puede ir en `agent.url.txt` junto al .exe.

## Verificacion
Logs del contenedor `lwo`: "Pick&Ban Overlay..." y "[LCU] Deshabilitado".
Con `?token=malo` el WebSocket debe rechazar ("[WS] Conexion rechazada").

## Si algo falla
- Build: pasar el error completo (no se compilo en el entorno donde se genero).
- Contenedor no escucha / error de HttpListener: poner `LWO_IP=0.0.0.0` en las variables del stack.
