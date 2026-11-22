# LightWeightOverlay (Docker / Portainer)

Overlay de picks y bans de League of Legends para OBS, con servidor en Docker y un agente local.

Basado en [Skivel1/LightWeightOverlay2026](https://github.com/Skivel1/LightWeightOverlay2026) (GPL-3.0).
Esta version fue modificada: target net8.0, soporte Linux/Docker, token de acceso por WebSocket,
`IStatePublisher` para desacoplar el hook del LCU, y un agente (`LWOAgent`) que publica el estado
desde el PC con League hacia el servidor remoto.

## Estructura
- `LWOServer/`, `LightWeightOverlay/`, `lol_picksandbans/`, `CacheDragon/`: servidor y plugins.
- `LWOAgent/`: agente para el PC con League (Windows).
- `Dockerfile`, `docker-compose.yml` (red local), `docker-compose.caddy.yml` (dominio + HTTPS), `deploy/`.

Instrucciones de despliegue: ver [DEPLOY.md](DEPLOY.md).

## Licencia
GPL-3.0 (ver `License.txt`). Si distribuyes binarios o imagenes, debes ofrecer el codigo fuente.
