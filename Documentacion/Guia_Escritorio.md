# Guía: CFDI Manager Escritorio

App de Windows (`CFDIManager.Escritorio`, WPF + WebView2) que se usa de dos formas:

| Modo | Para quién | Datos |
|---|---|---|
| **Conectar a un servidor** | Equipos de la oficina | Los del servidor (`http://<IP>:5000` o Tailscale). En la PC solo quedan las copias de XML de "Localizar en carpeta" (`Documentos\CFDI Manager\XML`) |
| **Instalación propia en esta PC** | Clientes | Propios de la PC en la carpeta de datos (por omisión `C:\CFDIManagerDatos`): `Storage`, `PostgreSQL` y `Logs` |

Plan y decisiones: https://claude.ai/artifact/Ech9cQ3pfXz39eoFahY6SA

## Publicar una versión

1. Sube el número en `<Version>` de `CFDIManager.Escritorio\CFDIManager.Escritorio.csproj` (p. ej. `0.2.0`) y haz
   commit. Las apps instaladas solo se actualizan a un número mayor.
2. Desde la carpeta raíz del proyecto, con un token de GitHub que pueda escribir releases:

   ```powershell
   $env:GITHUB_TOKEN = "<token>"
   pwsh -File .\herramientas\Publicar-Escritorio.ps1 -Subir
   ```

   Sin `-Subir` solo arma el instalador en `publish\Releases` para probarlo.
3. Queda en GitHub Releases con la etiqueta `escritorio-v<versión>`. Las apps instaladas la encuentran sola (al
   iniciar y cada 6 horas), la bajan y se instala al salir o desde el ícono de la bandeja ("Instalar la versión…").

Qué hace el script:

| Paso | Detalle |
|---|---|
| App y web | `dotnet publish` para Windows x64 sin .NET dentro; la web va en la carpeta `web` |
| PostgreSQL | Copia de un PostgreSQL instalado en el equipo (el más nuevo en `Program Files\PostgreSQL`) a `pgsql`, solo lo que usa la app: `postgres`, `initdb`, `pg_ctl`, `pg_dump`, `pg_restore`, `psql` y de diagnóstico; las DLL; `share` con traducciones solo en español |
| Instalador | `vpk pack` (Velopack, herramienta local en `dotnet-tools.json`): `CFDIManager.Escritorio-win-Setup.exe` (≈ 68 MB) y el paquete de actualización |
| Prerrequisitos | No van en el instalador: si faltan, el Setup baja e instala .NET 10 (Desktop y ASP.NET Core), WebView2 y Visual C++ 2015-2022 (lo necesita PostgreSQL) |

## Instalar

- Ejecutar `CFDIManager.Escritorio-win-Setup.exe`. No pide administrador: se instala para el usuario en
  `%LocalAppData%\CFDIManager.Escritorio` con acceso directo en el escritorio y el menú Inicio.
- Sin firma de código, Windows SmartScreen avisa la primera vez ("Más información" → "Ejecutar de todas formas").
- El primer arranque pide el modo:
  - **Servidor**: direcciones en orden (la de la red y la de Tailscale); usa la primera que responde.
  - **Instalación propia**: carpeta de datos y usuario administrador. La base se crea sola (≈ 15 s) y el cliente
    empieza de cero dando de alta sus empresas. En un equipo con el servicio `CFDIManager` no deja usar la carpeta
    del servicio.
- Las opciones del menú de cada usuario se controlan con la Asignación de tareas (Configuración → Usuarios).

## Cómo funciona la instalación propia

| Qué | Dónde / cómo |
|---|---|
| PostgreSQL | `pgsql` del programa, datos en `{datos}\PostgreSQL`, solo en 127.0.0.1, puerto 5433 (o el siguiente libre). Usuario `postgres` con contraseña aleatoria cifrada con DPAPI del equipo en `{datos}\escritorio.json` |
| Web | `web\CFDIManager.Web.exe --Modo=Escritorio` en `http://127.0.0.1:5180`: sin HTTPS, llaves en `{datos}\Storage\Llaves` con DPAPI del equipo, sin `CFDI_CLAVE_MAESTRA` |
| Base nueva | La web crea la base y aplica `Services\Esquema\esquema_inicial.sql`. Si cambian las tablas básicas (`cfdi`, `empresas`...), regenerarlo con `pg_dump --schema-only --no-owner --no-privileges` |
| Cerrar la ventana | Sigue en la bandeja: la descarga automática del SAT continúa |
| Salir | Pide confirmación y detiene la web y PostgreSQL |
| Iniciar con Windows | Arranca en la bandeja con el servidor local corriendo |
| Logs | `{datos}\Logs\web.log`, `postgresql.log` (e `initdb.log` si falló la creación) |
| Desinstalar | Detiene PostgreSQL y borra el programa; **la carpeta de datos se conserva** |
| Respaldo | El de Herramientas usa el `pg_dump` del paquete |

## Configuración por usuario

`%LocalAppData%\CFDIManagerEscritorio\configuracion.json`: modo, servidores, carpeta de datos, última carpeta de
"Guardar como", bandeja, inicio con Windows y tamaño de la ventana. Borrarlo hace que el siguiente arranque vuelva a
preguntar el modo (los datos no se tocan).

## Pruebas en una PC limpia (F4)

Ya probado en el servidor (26/11): instalación silenciosa, modo Local desde cero, inicio de sesión, desinstalación con
PostgreSQL corriendo (datos conservados) y actualización 0.1.0 → 0.1.1 (delta de 0.5 MB, PostgreSQL detenido antes,
la 0.1.1 arranca sobre los mismos datos). Falta en equipos sin nada instalado, uno con Windows 10 y otro con Windows 11:

| # | Prueba | Qué revisar |
|---|---|---|
| 1 | Ejecutar el Setup | Aviso de SmartScreen; instala .NET 10, WebView2 y Visual C++ si faltan; acceso directo en escritorio |
| 2 | Asistente, instalación propia | Rechaza contraseña débil o que no coincide; crea la base y abre el login (≈ 15 s) |
| 3 | Alta de empresa | e.firma y CIEC se validan y guardan; reabrir la app y que sigan ahí |
| 4 | Descarga SAT | Descargar ahora un mes; XML en `{datos}\Storage\XML\<RFC>\...` |
| 5 | Administración de XML | Vista previa, PDF (abre ventana), Excel General, ZIP de XML: "Guardar como" y aviso en la bandeja |
| 6 | Localizar en carpeta | Abre el Explorador en la PC con el XML seleccionado |
| 7 | Imagen del resumen | Copiar para correo (pegar en Outlook) y para WhatsApp |
| 8 | Correo | Configuración → Correo y Enviar por correo |
| 9 | Respaldo | Herramientas → respaldo a una carpeta; se genera el ZIP con la base |
| 10 | Bandeja e inicio con Windows | Cerrar la ventana (sigue la descarga), "Iniciar con Windows", reiniciar la PC: arranca en la bandeja |
| 11 | Salir | Pide confirmación; no quedan procesos `postgres` ni `CFDIManager.Web` |
| 12 | Modo Servidor | En una PC de la oficina: servidor de la red y de Tailscale; Localizar en carpeta baja el XML a `Documentos\CFDI Manager\XML` |
| 13 | Actualización | Publicar una versión mayor con `-Subir`; la app la baja sola y la instala al salir |
| 14 | Desinstalar | Desde Configuración de Windows → Aplicaciones; la carpeta de datos se conserva |

Una PC limpia puede ser Windows Sandbox (Windows 10/11 Pro: "Características de Windows" → "Espacio aislado de
Windows", requiere administrador y reiniciar; no activarlo en el servidor).

## Desarrollo

- `pwsh -File .\herramientas\Publicar-Escritorio.ps1` arma `publish\Escritorio` (app + `web` + `pgsql`) y el
  instalador en `publish\Releases`, sin subir nada.
- Para correr la app desde `bin` en modo Local: variables `CFDI_ESCRITORIO_WEB` (carpeta con `CFDIManager.Web.exe`
  publicado) y `CFDI_ESCRITORIO_PGSQL` (carpeta de PostgreSQL). `CFDI_ESCRITORIO_ADMIN_PASSWORD` crea el
  administrador `admin` sin el asistente (solo pruebas). `CFDI_ESCRITORIO_ACTUALIZACIONES` (carpeta, p. ej.
  `publish\Releases`) busca actualizaciones ahí en vez de GitHub, a los 5 segundos de abrir.
- No usar como carpeta de datos `C:\CFDIManagerDatos` en el servidor: es la del servicio.
