# Guía: publicar CFDI Manager como servicio de Windows

Cómo se genera la app y cómo se instala o actualiza el servicio `CFDIManager` en el servidor (hoy
**CFDIManagerPC-CGA**). Hay dos casos:

- **Actualizar** un servicio que ya existe: es lo de todos los días, después de cada cambio. Ver [Actualizar](#actualizar-el-servicio).
- **Instalar** por primera vez en un equipo. Ver [Instalación](#instalación-en-un-equipo-nuevo).

## Cómo queda armado

| Qué | Dónde |
|---|---|
| Servicio | `CFDIManager` ("CFDI Manager"), inicio automático, corre como SYSTEM y se reinicia solo si falla |
| Programa | `C:\CFDIManager\CFDIManager.Web.exe` |
| Configuración del servidor | `C:\CFDIManager\appsettings.Production.json`: puerto, contraseña de PostgreSQL y `CFDI_CLAVE_MAESTRA`. Solo la pueden leer SYSTEM y Administradores |
| Datos | `C:\CFDIManager\Storage` (XML, e.firmas, descargas SAT, CIF, etc.), o la carpeta definida en Configuración → Herramientas |
| Base de datos | PostgreSQL, base `cfdi_manager`. Las tablas y columnas nuevas se crean solas al arrancar |
| Dirección | `http://localhost:5000` en el servidor; desde la red, `http://<IP del servidor>:5000`; por Tailscale, `http://100.104.146.61:5000` |

Los scripts están en `herramientas\` dentro del repositorio.

## Actualizar el servicio

1. Trae el código que vas a publicar (`git pull`) o termina tus cambios. Lo que se publica es lo que hay en la carpeta,
   esté o no en GitHub.
2. Desde la carpeta raíz del proyecto (`Desktop\CFDIManagerBoveda`, la que contiene `herramientas`; no desde
   `CFDIManager.Web`), en PowerShell (no hace falta abrirla como administrador):

   ```powershell
   pwsh -File .\herramientas\Actualizar-Servicio.ps1
   ```

3. Acepta el aviso de **UAC** (Control de cuentas de usuario). Detener el servicio requiere permisos de administrador.
4. Espera el resultado:

   ```
   Publicando CFDIManager.Web (Release)...
   Archivos copiados a C:\CFDIManager.
   OK: el servicio está Running y responde en http://localhost:5000
   ```

5. En el navegador, recarga la app con **Ctrl+F5**. Los usuarios conectados pierden la conexión unos segundos y al
   reconectar regresan al Resumen.

Qué hace el script:

1. Compila en Release y publica en una carpeta temporal (`%TEMP%\CFDIManager-publicacion`). Si la compilación
   falla, se detiene ahí y **el servicio no se toca**.
2. Detiene el servicio y copia la publicación a `C:\CFDIManager`. **No copia** `appsettings.Production.json` ni la
   carpeta `Storage`: la configuración y los datos del servidor se conservan.
3. Arranca el servicio y espera hasta 2 minutos a que responda.

> Hazlo cuando nadie esté capturando o subiendo archivos: la app se detiene unos 10 a 30 segundos.

## Instalación en un equipo nuevo

Solo la primera vez, o para reinstalar el servicio desde cero.

### Requisitos

- .NET 10 SDK (o el Hosting Bundle / ASP.NET Core Runtime 10 si solo se va a ejecutar).
- PostgreSQL con la base `cfdi_manager` restaurada.
- Variables de entorno del usuario que instala:
  - `CFDI_POSTGRES_PASSWORD`: contraseña del usuario `postgres`. Si no existe, el script la pide.
  - `CFDI_CLAVE_MAESTRA`: la **misma** en todos los equipos. Sin ella, las contraseñas guardadas (CIEC, correo,
    SINUBE, e.firma) solo se leen en el equipo donde se capturaron.

Para revisar un equipo antes de instalar (versión de .NET, PostgreSQL, variables, base, carpeta Storage y rutas
de los XML):

```powershell
powershell -ExecutionPolicy Bypass -File .\herramientas\Diagnostico-Equipo.ps1
```

### Pasos

1. Publica la app en la carpeta del servicio:

   ```powershell
   dotnet publish CFDIManager.Web\CFDIManager.Web.csproj -c Release -o C:\CFDIManager
   ```

2. Copia la carpeta `Storage` del equipo de origen a `C:\CFDIManager\Storage`, **sin** `Storage\Llaves`.
3. Cierra Visual Studio / VS Code si tienen la app corriendo.
4. Abre PowerShell **como administrador** en la carpeta del proyecto y ejecuta:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\herramientas\Instalar-Servicio.ps1
   ```

   Opcional: `-Carpeta D:\OtraCarpeta -Puerto 5080`.

El script:

1. Cierra la app de desarrollo si está abierta.
2. Crea `appsettings.Production.json` con el puerto, la contraseña de PostgreSQL y la clave maestra.
3. Crea el servicio (si ya existía, lo borra y lo vuelve a crear).
4. Abre el puerto en el Firewall para redes de dominio y privadas.
5. Arranca el servicio y muestra las direcciones donde responde.

La primera vez puede tardar unos minutos en responder porque sincroniza la carpeta Storage con la base.

### Después de instalar

- Si la tarjeta de red del servidor está como red **Pública**, abre también ese perfil del Firewall (en
  CFDIManagerPC-CGA fue necesario):

  ```powershell
  Set-NetFirewallRule -DisplayName 'CFDI Manager (TCP 5000)' -Profile Domain, Private, Public
  ```

- Sin la misma `CFDI_CLAVE_MAESTRA`, vuelve a capturar en la app la CIEC, la e.firma, el correo y el acceso a SINUBE.
- Define la carpeta de datos en Configuración → Herramientas si no es la de junto al programa.

## Problemas comunes

| Síntoma | Qué revisar |
|---|---|
| "Cannot open 'CFDIManager' service" al detenerlo | Falta correr como administrador. `Actualizar-Servicio.ps1` ya pide UAC; si lo cancelas, no se actualiza nada |
| El script dice "No se pudo leer el resultado" | Se canceló el UAC. Vuelve a correrlo |
| "La publicación falló" | Error de compilación. Corrígelo y vuelve a correr el script; el servicio sigue con la versión anterior |
| El servicio arranca pero no responde | Visor de eventos → Registros de Windows → Aplicación, origen `CFDIManager` o `.NET Runtime`. Lo más común: contraseña de PostgreSQL equivocada en `appsettings.Production.json` o PostgreSQL detenido |
| El servicio no alcanza a iniciar (límite de 30 s de Windows) | Algún proceso de arranque tarda demasiado (pasó con el estado SAT de los metadatos antes de agregar su índice). Revisa el Visor de eventos |
| Desde otro equipo no abre | Firewall (perfil de red, ver arriba), que estén en la misma red o en Tailscale, y que se use el puerto correcto |
| Las descargas automáticas se duplican | Hay otra instancia corriendo (la app desde Visual Studio / VS Code). No la ejecutes mientras el servicio está activo |
| Contraseñas guardadas ya no funcionan | `CFDI_CLAVE_MAESTRA` distinta o ausente en `appsettings.Production.json`. Recaptúralas o corrige la clave y reinstala |

## Para no olvidar

- Nunca publiques directo sobre `C:\CFDIManager` con el servicio en marcha: los archivos están en uso y la copia
  queda a medias. Usa `Actualizar-Servicio.ps1`.
- `Storage` no se publica (el `.csproj` la excluye). Antes del 2/11/2026 sí se publicaba, y cada actualización
  regresaba los `solicitudes.json` de Descargas SAT a la copia del equipo de desarrollo.
- `appsettings.Production.json` no está en git y no se sobrescribe al actualizar: si cambia la contraseña de
  PostgreSQL o la clave maestra, edítalo a mano como administrador, o vuelve a correr `Instalar-Servicio.ps1`, y
  reinicia el servicio.
