# CFDIManagerBoveda: historial de implementación

Cómo nació CFDIManagerBoveda y qué se le fue agregando, del 27 al 31 de octubre de 2026. Reconstruido de las sesiones
de Claude Code de los dos equipos donde se trabajó (este equipo, usuario `websu`, y **ERNESTO-CGA**). Fechas y horas
en hora local.

Documentos de detalle en esta carpeta:

| Documento | Tema |
|---|---|
| [Proceso_Importacion_XML_SINUBE.md](Proceso_Importacion_XML_SINUBE.md) | Protocolo de HerramientasSinube para subir XML de clientes y proveedores (base de "Exportación a SINUBE") |
| [Conciliacion_SINUBE_vs_SAT.md](Conciliacion_SINUBE_vs_SAT.md) | Conciliación SAT / bóveda / SINUBE, descarga de XML faltantes, descarga automática por horarios y cierre de mes |

## Resumen

```
CFDIManager (websu)            MiBovedaCFDI (ERNESTO-CGA)
Blazor + PostgreSQL            Razor Pages + EF, base mi_boveda_cfdi
cfdi_manager                   réplica web de MiAdminXML
        \                              /
         \  29/10: clon de CFDIManager + piezas de la Administración de XML de MiBovedaCFDI
          \                          /
           CFDIManagerBoveda  (base cfdi_manager, XML en CFDIManager.Web\Storage\XML)
                     |
           30/10: se trae a este equipo (CFDIManagerBoveda.rar) y se sigue aquí
```

- **CFDIManager** es la app original (Blazor Server .NET 10, PostgreSQL `cfdi_manager`, proyecto `CFDIParser`).
  Sigue en `Desktop\CFDIManager` con su git; no se tocó después de la unión.
- **MiBovedaCFDI** (ERNESTO-CGA) es la versión web de la Administración de XML de MiAdminXML, con descarga SAT y
  reportes. Quedó como app anterior; ya no se le hacen cambios.
- **CFDIManagerBoveda** es la app principal: un clon de CFDIManager al que se le llevó lo que solo tenía MiBovedaCFDI.

## 1. Antecedentes en CFDIManager (este equipo, 27 y 28/10)

Sesiones en `.claude\projects\c--Users-websu-rea` y `c--Users-websu-Desktop-CFDIManager`. Todo quedó en commits del
git de `Desktop\CFDIManager` y en su `CLAUDE.md`.

| Fecha | Cambio | Commit |
|---|---|---|
| 27/10 | Réplica propia de MiAdminXML (la original está protegida, no se descompiló) y su pantalla de Administración de XML | — |
| 27/10 | Descarga masiva del SAT integrada en **Descargas SAT** de CFDIManager | `4c6c044` |
| 27/10 | **Administración de XML** como opción del menú, leyendo de PostgreSQL y de `Storage\XML\<RFC>\Emitidos\|Recibidos\año\mes` | `4c6c044` |
| 27/10 | `CFDIParser` integrado al mismo repositorio | `4c6c044` |
| 27/10 | Advertencias de compilación corregidas | `0705c2c` |
| 27/10 | **EFOS/EDOS** funcional con el listado 69-B completo y los XML de Storage | `88d2a8d` |
| 27/10 | **Storage como fuente de verdad**: `SincronizacionStorageHostedService` indexa la carpeta al arrancar y cada 10 min; CFDI entre empresas del grupo se registran en ambas | `bf5ee04` |
| 27/10 | `CLAUDE.md` con decisiones de diseño y pendientes | `785acb8` |
| 28/10 | Modo oscuro en todas las pantallas (`herramientas\modo_oscuro.js` genera la sección oscura de cada `.razor.css`) | `8bf218a` |
| 28/10 | Validación de la e.firma al guardar en Empresas (RFC, no CSD, vigencia, contraseña, `.key` ↔ `.cer`); caso FIEL de CARNAVAL en TRADE MARKET 57 | `922e37d` |

## 2. MiBovedaCFDI (ERNESTO-CGA, 28/10)

Sesiones en `.claude\projects\c--Users-Ernesto-Hern-ndez-Desktop-MiBovedaCFDI`.

- Front de Administración de XML igual a la ventana de MiAdminXML: filtros que se aplican solos, selección con
  Ctrl/Shift, panel Básica / Plus-Pro, totales por tipo.
- **Descarga SAT** con e.firma, portada del proyecto de Node: mensajes al SAT idénticos byte a byte a los de la
  librería de Node.
- **Exportar a Excel "General"** con el formato de `AZU010730A22 Emitidas AGO 2026.xlsx` y dashboard de ventas por
  cliente (skill `dashboard-ventas-cfdi-compacto`): hojas Dashboard, Resumen Clientes, Factura, NCredito, Nómina,
  Pago y Traslado. Un renglón por UUID.
- **Valida Estado SAT** con barra de avance por tipo, 3 consultas a la vez, cada celda pintada al llegar su respuesta.
  Valida todo el filtro, no solo la página.
- Pestaña Todos, conteo por tipo, paginado de 200 en el servidor.
- Pantalla **Empresas** con FIEL y descarga continua (`SatSincronizador`).
- Se unificaron datos: importación de `cfdi_manager` a `mi_boveda_cfdi` (5 empresas, 8,482 CFDI) y **una sola ruta XML
  general** para todas las empresas (`<ruta>\<RFC>`), sin ruta por empresa.

## 3. La unión: nace CFDIManagerBoveda (ERNESTO-CGA, 29/10)

Sesión `880cc7bc` (proyecto MiBovedaCFDI). Petición: clonar CFDIManager y cambiar su Administración de XML por la de
MiBovedaCFDI, actualizando las rutas del otro equipo.

**Decisión:** una comparación detallada mostró que la Administración de XML de CFDIManager ya era casi una adaptación a
Blazor de la de MiBovedaCFDI. En lugar de reescribirla se **llevó solo lo que le faltaba** y se conservó lo que
CFDIManager hacía mejor (PDF con logo, acumulado de meses real, panel de relacionados, importar carpeta, filtro y
totales por estado SAT).

Lo que se llevó de MiBovedaCFDI a CFDIManagerBoveda:

1. **Excel General** con dashboard (`Services\AdministracionXml\ReporteGeneral.cs`).
2. **Valida Estado SAT** de todo el filtro, 3 a la vez, con avance por tipo y botón Cancelar.
3. **Selección y navegación:** casilla del encabezado = todo el filtro, Ctrl/Shift, paginador de 200 de lo más
   reciente a lo más antiguo, pestañas Todos / Facturas / Pagos / Nómina 1.1 / Nómina 1.2.
4. **Detalles:** pestaña Complemento, filtros Duplicados y PPD, "Guardar a Excel" sin sobrescribir (agrega "(2)"),
   columnas visibles recordadas.

Datos y configuración:

- **Base:** `cfdi_manager` (se eligió sobre `mi_boveda_cfdi`). Contraseña en la variable de entorno
  `CFDI_POSTGRES_PASSWORD`; si no existe, en user secrets (`CFDIManagerBoveda-Web`).
- **Ruta XML general:** `AppSettings:CarpetaXml` en `appsettings.json`; `RutaXmlService` arma `<ruta>\<RFC>`. En
  Empresas la ruta ya no se edita. Primero apuntó a `onefactureXMLs\MiBoveda`; el mismo día se corrigió a
  **`Storage\XML`** (relativa a `CFDIManager.Web`), que es la ubicación correcta.
- Se reescribieron en la base las rutas `C:\Users\websu\...` del otro equipo.
- Se dieron de alta AAL090714841, ODI120704BW1 y ODI191122390 (8 empresas en total).

## 4. Primeras mejoras en CFDIManagerBoveda (ERNESTO-CGA, 29/10)

Sesión `cf5d9688`.

| Tema | Qué se hizo |
|---|---|
| Descarga automática SAT | Empresas → Acceso SAT: "Descargar automáticamente", Emitidos, Recibidos, Metadatos, "Descargar desde". `DescargaAutomaticaSatHostedService` pide los meses faltantes, una solicitud por mes. Columnas nuevas en `empresas_sat`. |
| Switch en la barra superior | `DescargaSatSwitch.razor` junto al selector de empresa, con "Descargar ahora" y estado del último ciclo. |
| Vista previa | El recuadro del comprobante se partió: datos, XML y PDF lado a lado; doble clic abre el XML o el PDF. |
| Base limpia | Se vaciaron las tablas de CFDI para empezar con descargas limpias del SAT (empresas, e.firma y EFOS se conservaron). |
| Descargas SAT | Corrección del select Emitidos/Recibidos; resultado "N descargados: nuevos / ya existían"; botones **Cancelar** y **Eliminar** solicitud. |
| Actualizar índice | `ActualizarIndice.razor` en la barra superior; al abrir Administración de XML la base se iguala con la carpeta. |
| Tema | `CambiarTema.razor` (claro/oscuro, recordado por navegador); grises en modo claro. |
| Iconos | Bootstrap Icons 1.11.3 local (`wwwroot\lib\bootstrap-icons`) en herramientas y barra superior. |
| Orden por columna | Clic en el encabezado ordena en PostgreSQL sobre todo el filtro (▲ ▼ y orden normal). |
| CIEC | Empresas guarda la CIEC cifrada (`Storage\Llaves`) y la valida; Descargas SAT tiene pestañas **Con e.firma** / **Con CIEC** (`PortalSatCiec.cs`, `DescargaCiec.razor`). |
| Datos fiscales | Pestaña que lee la Constancia de Situación Fiscal en PDF (regímenes, actividades, obligaciones, domicilio). |

## 5. SINUBE, reportes y Empresas (ERNESTO-CGA, 29 y 30/10)

Sesión `dd64d403`.

- **Exportación a SINUBE** (menú): análisis con REA de `C:\HerramientasSinube` (documentado en
  [Proceso_Importacion_XML_SINUBE.md](Proceso_Importacion_XML_SINUBE.md)). Pestañas Importar facturas de Clientes,
  de Proveedores, **de Clientes sin folio** (foliadores de SINUBE) y Herramientas (acceso, ambiente, nombres de XML,
  historial de envíos). Lee de `Storage\XML` por periodo; no mueve archivos a `procesados`/`errores`.
- **Menú:** iconos alineados y más grandes; se quitó Respaldos.
- **CIEC:** dos correcciones del portal del SAT (radio de búsqueda por fechas y un `</div>` suelto que ocultaba las
  filas); diagnóstico en `Storage\Diagnostico\SatCiec\`; barra de avance.
- **Administración de XML:** filtros y resumen en tarjetas separadas, totalizador más visible con `$`, pestañas del
  grid con el color de su tipo, pestañas Notas de crédito y Traslados.
- **Orden fijo de comprobantes en toda la app:** Facturas, Nómina 1.1, Nómina 1.2, Notas de crédito, Traslados,
  Pagos. Colores: facturas azul, nómina verde, notas de crédito rojo, traslados ámbar, pagos morado.
- **Reporte 80-20 (Pareto)** con dashboard para clientes (emitidos) y proveedores (recibidos), clases A/B/C
  (`Services\AdministracionXml\Reporte8020.cs`).
- **Empresas** al estilo onefacture: columnas Regímenes y Opinión; un solo marco con páginas Empresas, General,
  Datos fiscales, Accesos SAT y Declaraciones; se quitó la columna Ruta XML de la lista.
- **Descargas de documentos del SAT:** Opinión de cumplimiento y Constancia (CSF/CIF) con la CIEC sin captcha (API de
  SAT Móvil, `SatMovilCliente.cs`); declaraciones mensuales (desde 2025) y anual con captcha
  (`PortalSatDeclaraciones.cs`). Opinión IMSS se carga en PDF. Archivos en `Storage\ArchivosSat\<empresa>\`.

El 30/10 el proyecto se empaquetó (`CFDIManagerBoveda.rar`) y se trajo a este equipo.

## 6. En este equipo (30 y 31/10)

Sesión `de4c4db4` de `.claude\projects\c--Users-websu-Desktop-CFDIManagerBoveda`.

| Tema | Qué se hizo |
|---|---|
| Usuarios y seguridad | Login con ASP.NET Identity sobre PostgreSQL (stores propios). Roles Administrador, Supervisor, Contador y Consulta. Empresas asignadas por usuario. Bloqueo tras 5 intentos, cambio de contraseña obligatorio, revalidación de sesión. Pantalla **Usuarios** con eliminar y restablecer contraseña. |
| Tareas por usuario | Cada opción del menú es una tarea (`Services\Seguridad\Tareas.cs`); se le pueden quitar a un Supervisor, Contador o Consulta y desaparece del menú y de la URL. |
| Correo | **Configuración de correo** (SMTP con contraseña cifrada), correo de bienvenida y de restablecimiento, bitácora `correos_enviados`, envío de reportes por correo desde Administración de XML y Exportaciones. |
| Administración de XML | Rediseño con Bootstrap 5.3 (la 6 sigue en alfa). Si el XML no está en la ruta guardada (rutas de otro equipo), se busca en la carpeta local (`RutaXmlLocal`). Filtro por **Día**. |
| Conciliación SINUBE | Análisis con REA del protocolo de SINUBE; conciliación de 3 vías SAT / bóveda / SINUBE para proveedores y clientes; descarga de XML faltantes. Ver [Conciliacion_SINUBE_vs_SAT.md](Conciliacion_SINUBE_vs_SAT.md). |
| XML al cierre del mes | Descarga automática por horarios (07:00 y 21:00) con ventana de 3 días y **cierre de mes** (días 1 a 3) con historial en Descargas SAT y aviso por correo. |
| Fechas | Todas en dd/mm/aaaa (componente `FechaInput`), cultura es-MX; los filtros inician el día 1 del mes en curso. |

Tablas nuevas de esta etapa: `usuarios`, `roles`, `usuario_roles`, `usuario_empresas`, `usuario_tareas_denegadas`,
`configuracion_correo`, `correos_enviados`, `sat_metadatos`, `sat_metadatos_periodos`, `sat_cierres`. Todas se crean
solas al arrancar.

## Dónde está cada cosa

| Qué | Dónde |
|---|---|
| Código | `Desktop\CFDIManagerBoveda` (`CFDIManager.Web` + `CFDIParser`). Sin git todavía. |
| App original | `Desktop\CFDIManager`, con git y `CLAUDE.md` (hasta el 28/10) |
| XML | `CFDIManager.Web\Storage\XML\<RFC>\Emitidos\|Recibidos\AAAA\MM` |
| Llaves de cifrado (CIEC, SMTP, SINUBE) | `CFDIManager.Web\Storage\Llaves`, protegidas con DPAPI del usuario de Windows: en otro equipo o usuario hay que volver a capturar las contraseñas |
| Sesiones de Claude Code de este equipo | `C:\Users\websu\.claude\projects\` |
| Sesiones de ERNESTO-CGA | `C:\Users\Ernesto Hernández\.claude\projects\` (copia en `Desktop\projects.rar`) |

## Pendientes conocidos

- Poner CFDIManagerBoveda en git (la carpeta no tiene repositorio).
- Encender la descarga automática en AAL090714841 (hoy solo BTM y GUM la tienen).
- Captcha de la CIEC y de declaraciones: hoy se escribe a mano.
- SINUBE: marcar aparte el rechazo por folio duplicado (A-1 contra 1) para reenviarlo por "Clientes sin folio".
- Nómina: ligarla con el visor de nómina del SAT (hoy es informativa en la conciliación).
- Migrar a Bootstrap 6 cuando salga estable.
- De `CLAUDE.md` de CFDIManager: limpiar servicios sin uso (`CfdiPdfService`, `ImportacionXmlService`), revisar la
  licencia de QuestPDF y el estado de pago PPD.
