# Manual técnico

Cómo está hecho CFDI Manager, dónde vive cada cosa, cómo se publica y cómo se lleva la bitácora de cambios. Es para
quien mantiene el sistema; solo lo ve el **Administrador**. Arriba están también la
[bitácora de cambios](Historial_CFDIManagerBoveda.md#8-bitácora-de-cambios-desde-el-711) y las guías técnicas, leídas
directamente de la carpeta `Documentacion` del repositorio.

## Componentes

| Proyecto | Qué es |
|---|---|
| `CFDIManager.Web` | La aplicación: Blazor Server en .NET 10, MudBlazor 9 con Bootstrap/Tabler, Identity para usuarios. Pantallas en `Components\Pages`, lógica en `Services` |
| `CFDIParser` | Biblioteca: lector de XML de CFDI (`XmlCfdiParser`), modelos y acceso a PostgreSQL con Npgsql (`PostgreSqlRepository`, repartido en archivos `PostgreSqlRepository.<tema>.cs`) |
| `CFDIManager.Escritorio` | App de Windows: WPF + WebView2 que abre la web de un servidor (modo Servidor) o la corre en la misma PC con PostgreSQL portátil (modo Local). Instalador y actualizaciones con Velopack |

Código en GitHub `WebsurferMX/CFDIManagerBoveda` (rama `main`, **repositorio público**: nunca se suben contraseñas,
`appsettings.Production.json`, Storage ni respaldos). La solución es `CFDIManager.slnx`.

Bibliotecas principales: QuestPDF (PDF y la imagen del resumen; licencia Community mientras la facturación anual sea
menor a 1 millón de USD), ClosedXML (Excel), MailKit (correo), PdfPig (lectura de la constancia fiscal), QRCoder,
HtmlAgilityPack, Markdig (manuales).

## Formas de instalación

| Instalación | Cómo corre | Dirección | Datos |
|---|---|---|---|
| **Servidor** | Servicio de Windows `CFDIManager` (SYSTEM, inicio automático) en `C:\CFDIManager` | Puerto 5000, en la red o por Tailscale | Base `cfdi_manager` del PostgreSQL del equipo; Storage en `C:\CFDIManagerDatos\Storage` |
| **Escritorio, modo Servidor** | `CFDIManagerEscritorio.exe` abre la web del servidor en WebView2 | La del servidor (lista en orden; usa la primera que responde) | En el servidor |
| **Escritorio, modo Local** | La app arranca `web\CFDIManager.Web.exe --Modo=Escritorio` y PostgreSQL portátil (`pgsql`) | Web en 127.0.0.1:5180; PostgreSQL en 127.0.0.1:5433 o el siguiente libre | Carpeta de datos elegida en el asistente (por omisión `C:\CFDIManagerDatos`): `PostgreSQL`, `Storage`, `Logs` |

`--Modo=Escritorio` (`Services\ModoApp.cs`) quita HTTPS y HSTS, protege las llaves con DPAPI del equipo y, si la base
no existe, la crea con `Services\Esquema\esquema_inicial.sql`.

## Base de datos

PostgreSQL, base `cfdi_manager`. Tablas principales:

| Grupo | Tablas |
|---|---|
| Comprobantes | `cfdi` (un renglón por CFDI y empresa), `cfdi_conceptos`, `cfdi_impuestos`, `cfdi_impuestos_globales`, `cfdi_relacionados`, `cfdi_verificados`, `cfdi_importaciones`, `personas_fiscales` (emisores y receptores), `storage_archivos` (índice de la carpeta de XML) |
| Empresas | `empresas`, `empresas_sat` (e.firma, CIEC, descarga automática), `empresa_certificados`, `empresas_sinube` |
| SAT | `sat_solicitudes`, `sat_paquetes`, `sat_descargas`, `sat_metadatos`, `sat_metadatos_periodos`, `sat_cierres`, `efos_sat`, `efos_sat_cargas` |
| Usuarios | `usuarios`, `roles`, `usuario_roles`, `usuario_empresas`, `usuario_tareas_denegadas` |
| Otros | `configuracion_app` (ajustes clave–valor), `configuracion_correo`, `correos_enviados`, `sinube_envios`, `respaldos`, `proteccion_llaves` |

**Cambios a la base sin migraciones destructivas.** Las tablas y columnas nuevas se crean solas al arrancar o al usar
la pantalla, con `CREATE TABLE IF NOT EXISTS` y `ALTER TABLE … ADD COLUMN IF NOT EXISTS` en los métodos
`Asegurar…` del repositorio (por ejemplo `AsegurarTablasAdministracionXml`). Un dato que sale del XML y se agrega
después (como `cfdi.fecha_pago_nomina`) se guarda como `NULL` y se llena leyendo el XML la primera vez que se
necesita. Si cambian las tablas básicas, se regenera `esquema_inicial.sql` con
`pg_dump --schema-only --no-owner --no-privileges` para las instalaciones nuevas del modo Local.

## Storage

Carpeta de datos de archivos, fuera de la del programa. En el servidor es `C:\CFDIManagerDatos\Storage` y se define por
equipo en Herramientas → Storage (clave `carpeta_storage:<EQUIPO>` en `configuracion_app`).

| Carpeta | Contenido |
|---|---|
| `XML\<RFC>\Emitidos\|Recibidos\AAAA\MM` | Los XML de cada empresa (también `Propios` y `NoIdentificados`) |
| `Adendas` | XML con adenda bajados de SINUBE (fuera de la sincronización) |
| `Certificados\<RFC>` | e.firma (`.cer` y `.key`) |
| `Cif`, `CSF`, `ArchivosSat` | Constancias, opiniones de cumplimiento y declaraciones |
| `SatDescargas` | Paquetes de la descarga masiva del SAT |
| `Logos` | Logo por RFC para los PDF |
| `Reportes`, `ZIP`, `Renombrados`, `Azulejeras` | Archivos generados por la app |
| `Llaves` | Llaves de cifrado (si no hay `CFDI_CLAVE_MAESTRA`) |
| `Temporal`, `Diagnostico` | Trabajo temporal |

La base guarda la ruta de cada XML. **Actualizar índice** (y la pasada automática) registra los XML nuevos o
cambiados de la carpeta de la empresa; clasifica emitido o recibido por el RFC de dentro del XML, no por la carpeta.

## Procesos en segundo plano

| Proceso | Cuándo | Qué hace |
|---|---|---|
| Sincronización de Storage (`SincronizacionStorageHostedService`) | 10 s después de arrancar y cada `SincronizacionStorage:Minutos` (10) | Lee las carpetas de XML y registra lo nuevo |
| Descarga automática del SAT (`DescargaAutomaticaSatHostedService`) | Cada 30 min; ventanas en `DescargaAutomaticaSat:Horarios`; cierre de mes los días 1 a `DiasCierre` | Verifica solicitudes, descarga paquetes y pide los periodos que faltan (se pausa en Herramientas) |
| Respaldo (`RespaldoHostedService`) | Lunes a viernes a la hora de Herramientas → Respaldo | ZIP con `pg_dump` y Storage; diarios y uno mensual |
| Adendas de SINUBE (`AdendasSinubeHostedService`) | Periódico | Baja de SINUBE los XML con adenda |
| Validación SAT (`ValidacionSatSegundoPlano`) | Al pedirla | Valida estado SAT por lotes sin bloquear la pantalla |

## Seguridad

- **Usuarios**: ASP.NET Identity. Contraseña de 8+ caracteres con mayúscula, minúscula y número; bloqueo 15 min tras 5
  intentos; la sesión se cierra tras `Sesion:MinutosInactividad` (30).
- **Roles**: Administrador, Supervisor, Contador, Consulta (`Services\Seguridad\Permisos.cs`).
- **Tareas**: cada opción del menú, cada herramienta y cada reporte de Administración de XML es una tarea
  (`Services\Seguridad\Tareas.cs`); el rol es el tope y al usuario se le pueden quitar (`usuario_tareas_denegadas`).
  Las páginas usan la política `Tarea:<clave>`; el Manual técnico, el rol Administrador.
- **Empresas por usuario**: `usuario_empresas` (el Administrador ve todas).
- **Contraseñas guardadas** (CIEC, e.firma, SMTP, SINUBE): cifradas con Data Protection. Con la variable
  `CFDI_CLAVE_MAESTRA` las llaves van cifradas en `proteccion_llaves` (la misma clave en todos los equipos); sin ella,
  en `Storage\Llaves` con DPAPI (en otro equipo hay que recapturarlas).
- **Secretos del servidor** en `C:\CFDIManager\appsettings.Production.json` (contraseña de PostgreSQL y clave maestra;
  solo SYSTEM y Administradores; no se sobrescribe al actualizar).

## Configuración

| Clave (`appsettings.json`) | Para qué |
|---|---|
| `ConnectionStrings:PostgreSQL` | Conexión a la base |
| `AppSettings:CarpetaXml` | Carpeta de XML dentro de Storage (`Storage\XML`) |
| `DescargaAutomaticaSat:Horarios`, `VentanaDias`, `DiasCierre` | Descarga automática del SAT |
| `Sesion:MinutosInactividad`, `Sesion:PermitirRecordarme` | Sesión |
| `SincronizacionStorage:Minutos` | Cada cuánto se leen las carpetas de XML |

Variables de entorno: `CFDI_CLAVE_MAESTRA` (servidor). Solo para pruebas de la app de escritorio:
`CFDI_ESCRITORIO_WEB`, `CFDI_ESCRITORIO_PGSQL`, `CFDI_ESCRITORIO_ADMIN_PASSWORD`, `CFDI_ESCRITORIO_ACTUALIZACIONES`.

## Publicación

| Qué | Cómo | Detalle |
|---|---|---|
| Servicio del servidor | `pwsh -File .\herramientas\Actualizar-Servicio.ps1` | Compila, pide UAC, detiene el servicio, copia a `C:\CFDIManager` sin tocar configuración ni datos y lo arranca. Ver [Publicar el servicio](Guia_Publicacion_Servicio.md) |
| Servidor nuevo | `herramientas\Instalar-Servicio.ps1` | Instalación por primera vez |
| App de escritorio | Subir `<Version>` en `CFDIManager.Escritorio.csproj` y `pwsh -File .\herramientas\Publicar-Escritorio.ps1 -Subir` | GitHub Releases con la etiqueta `escritorio-v<versión>`; las apps instaladas la toman solas. Ver [App de escritorio](Guia_Escritorio.md) |

Para confirmar qué versión corre: menú del usuario → **Acerca de** (commit y fecha de la web; versión de la app de
escritorio y su modo).

## Herramientas del repositorio

| Script | Para qué |
|---|---|
| `herramientas\Actualizar-Servicio.ps1` | Publicar la web en el servicio |
| `herramientas\Instalar-Servicio.ps1` | Instalar el servicio en un equipo nuevo |
| `herramientas\Publicar-Escritorio.ps1` | Armar el instalador de la app de escritorio y publicarlo (`-Subir`) |
| `herramientas\Diagnostico-Equipo.ps1` | Revisar si un equipo está listo (.NET, PostgreSQL, base, Storage); solo lee |
| `herramientas\Capturas-Manual\Capturas-Manual.ps1` | Volver a tomar las capturas del manual con la empresa de demostración (datos ficticios) |

## Manuales y documentación

| Documento | Archivo | Se ve en |
|---|---|---|
| Manual de usuario | `CFDIManager.Web\Ayuda\Manual.md` (capturas en `wwwroot\ayuda\img`) | Menú del usuario → Ayuda |
| Registro de cambios (para usuarios) | `CFDIManager.Web\Ayuda\Novedades.md` | Menú del usuario → Registro de cambios |
| Manual técnico | `CFDIManager.Web\Ayuda\ManualTecnico.md` | Menú del usuario → Manual técnico (Administrador) |
| Bitácora y guías técnicas | `Documentacion\*.md` | Botones arriba de esta página |

Los `.md` van incrustados en la web: cualquier cambio se ve después de publicar el servicio.

## Bitácora de cambios

Todo cambio al sistema queda registrado el mismo día. La bitácora es la sección 8 de
[Historial_CFDIManagerBoveda.md](Historial_CFDIManagerBoveda.md#8-bitácora-de-cambios-desde-el-711), en la carpeta
`Documentacion`.

### Qué se registra

- Cambios al código (funciones nuevas, correcciones, cambios de pantalla).
- Cambios a la base de datos (tablas o columnas nuevas).
- Versiones publicadas de la app de escritorio y publicaciones del servicio cuando cambian algo para los usuarios.
- Decisiones que afectan al sistema (por ejemplo, licencias o configuración del servidor).
- Pruebas importantes y su resultado.

### Cómo se registra

1. **Commit** con el cambio y un mensaje en español que diga qué cambia y por qué. Solo se agregan los archivos que se
   cambiaron (nunca carpetas completas: en `Documentacion` hay notas locales que no deben subirse).
2. **El mismo día**, en la sección 8 del Historial, bajo un encabezado `### dd/mm: tema`, un renglón por tema:

   | Tema | Qué se hizo | Commit |
   |---|---|---|
   | Pantalla o área | Qué cambió, por qué y cómo se probó | `abc1234` |

   Lo que todavía no tiene commit se marca *sin commit* y se completa al subirlo.
3. **Commit aparte** del Historial: `Historial: commit abc1234 en la bitácora del dd/mm`.
4. Si el usuario lo nota, una línea en `Novedades.md` (Registro de cambios), en palabras del usuario.
5. Si cambia una pantalla del manual, se actualiza `Manual.md` y, si hace falta, se vuelven a tomar las capturas con
   `Capturas-Manual.ps1`.
6. `git push` y publicar: el servicio con `Actualizar-Servicio.ps1`; la app de escritorio subiendo `<Version>` y con
   `Publicar-Escritorio.ps1 -Subir`. Las versiones publicadas también van a la bitácora.

## Diagnóstico

| Dónde | Qué hay |
|---|---|
| Visor de eventos de Windows → Aplicación, origen `CFDIManager.Web` | Errores y avisos de la web (el origen `CFDIManager` son los arranques y paros del servicio) |
| `{datos}\Logs\web.log`, `postgresql.log`, `initdb.log` | App de escritorio en modo Local |
| `%LocalAppData%\CFDIManagerEscritorio\actualizaciones.log` | Cada búsqueda de actualizaciones de la app de escritorio |
| `%LocalAppData%\CFDIManagerEscritorio\configuracion.json` | Configuración de la app de escritorio por usuario (borrarlo vuelve a mostrar el asistente; los datos no se tocan) |
| Herramientas → Respaldo, Descarga SAT | Historial de respaldos y estado de la descarga automática por empresa |
| Correo → Últimos envíos | Bitácora de correos enviados |
