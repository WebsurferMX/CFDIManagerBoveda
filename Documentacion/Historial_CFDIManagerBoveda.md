# CFDIManagerBoveda: historial de implementación

Cómo nació CFDIManagerBoveda y qué se le fue agregando, del 27 de octubre al 7 de noviembre de 2026. Reconstruido de las
sesiones de Claude Code de los equipos donde se trabajó (usuario `websu`, **ERNESTO-CGA** y, desde el 31/10,
**CFDIManagerPC-CGA**) y de los commits en GitHub. Fechas y horas en hora local.

Documentos de detalle en esta carpeta:

| Documento | Tema |
|---|---|
| [Proceso_Importacion_XML_SINUBE.md](Proceso_Importacion_XML_SINUBE.md) | Protocolo de HerramientasSinube para subir XML de clientes y proveedores (base de "Importación a SINUBE") y reglas de folios repetidos, series alternas y traslados propios |
| [Guia_Publicacion_Servicio.md](Guia_Publicacion_Servicio.md) | Publicar la app, instalar y actualizar el servicio de Windows |
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
           30/10: se trae al equipo websu (CFDIManagerBoveda.rar)
                     |
           31/10: se copia con git a CFDIManagerPC-CGA, servidor (servicio de Windows, GitHub)
```

- **CFDIManager** es la app original (Blazor Server .NET 10, PostgreSQL `cfdi_manager`, proyecto `CFDIParser`).
  Sigue en `Desktop\CFDIManager` con su git; no se tocó después de la unión.
- **MiBovedaCFDI** (ERNESTO-CGA) es la versión web de la Administración de XML de MiAdminXML, con descarga SAT y
  reportes. Quedó como app anterior; ya no se le hacen cambios.
- **CFDIManagerBoveda** es la app principal: un clon de CFDIManager al que se le llevó lo que solo tenía MiBovedaCFDI.

## 1. Antecedentes en CFDIManager (equipo websu, 27 y 28/10)

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

El 30/10 el proyecto se empaquetó (`CFDIManagerBoveda.rar`) y se trajo al equipo websu.

## 6. En el equipo websu (30 y 31/10)

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

El 31/10 el proyecto se copió con su git a **CFDIManagerPC-CGA** (usuario de Windows `CFDI Manager`), que queda como
servidor. Desde ahí se sigue en GitHub (`WebsurferMX/CFDIManagerBoveda`, rama `main`).

## 7. Servidor CFDIManagerPC-CGA (31/10 al 7/11)

### 31/10: servicio de Windows y cifrado

| Tema | Qué se hizo | Commit |
|---|---|---|
| Usuarios | Catálogo arriba y formulario en tres columnas | `f4e0b0f` |
| Estado SAT | Estado SAT desde metadatos, columna Última validación; Empresas ordenadas y tarjetas con profundidad | `0b4e1fc` |
| Validación y sesión | Validación SAT en segundo plano por empresa (sigue aunque se cambie de pantalla). Cierre de sesión tras 30 min sin uso con aviso un minuto antes; "Recordarme" apagado por omisión. Icono de la pestaña | `7f0c5fe` |
| Vista previa | PDF como imagen PNG generada del XML; recuadro **Relacionados** en ambas direcciones ("Sustituido por") | `ebd026f` |
| PDF | Se quita "PDFs masivos"; lo reemplaza Descargar PDF (ZIP) | `9d91de2` |
| Equipo nuevo | Script de diagnóstico: .NET, PostgreSQL, base, Storage y rutas de XML, con el SQL para corregirlas | `57dec70` |
| Servicio de Windows | Servicio `CFDIManager` en `C:\CFDIManager`, puerto 5000, instalador `herramientas\Instalar-Servicio.ps1` (Firewall, contraseña de PostgreSQL y clave maestra en `appsettings.Production.json`) | `5362251` |
| e.firma | En `Storage\Certificados\{RFC}\{RFC}.cer/.key` (se mueven solas al arrancar); contraseña cifrada con prefijo `dp:` | `5362251`, `0a2d01b` |
| Llaves de cifrado | Tabla `proteccion_llaves`, cifrada con `CFDI_CLAVE_MAESTRA`: se leen en cualquier equipo con la misma clave | `5362251` |
| Empresas | Columna CIEC con verificación por SAT Móvil; corrige el alta (error 42P08) | `5362251` |

### 1/11: Herramientas, SAT y XML Azulejeras

| Tema | Qué se hizo | Commit |
|---|---|---|
| Descargas SAT | Eliminar varias solicitudes o todas (los CFDI importados se conservan). Índice `cfdi (empresa_id, UPPER(uuid))`: el arranque bajó de 40 s a 0.06 s; con 40 s el servicio no alcanzaba a iniciar | `dd3748d` |
| Herramientas (solo Administrador) | **Periodo de trabajo** (automático o mes fijo) con el que abren las pantallas. **Re-sincronizar con la bóveda** por empresa, en segundo plano: rehace los datos desde el XML, importa los que falten y retira los que ya no están | `447c580` |
| Declaraciones SAT | Vuelve a obtener el captcha (mensual y anual) tras el cambio del login del SAT | `08fb72d` |
| Descarga masiva | El rechazo 5002 ("solicitudes agotadas") se reintenta con la fecha inicial de 1 a 10 segundos antes | `dfbd0b4` |
| Descarga automática | Interruptor general de pausa y tabla por empresa para encenderla o apagarla | `b202ec1` |
| Empresa activa | Selector con buscador por RFC, nombre o razón social; teclas ↑ ↓ Enter Esc | `b202ec1` |
| Administración de XML | Filtro **Relacionados** (los dos lados). Asistente **Renombrar XML's** (campos, orden, separador, ejemplo en vivo) que descarga un ZIP; los originales no se tocan | `b7c7739`, `8e45e07`, `5300e48` |
| Carpeta Storage | Se define por equipo en Herramientas (o `AppSettings:CarpetaStorage`); valida, copia los datos y aplica al reiniciar | `5300e48` |
| XML Azulejeras (Clientes) | Analizar emitidos por serie y tipo, generar las series a público general (la nómina nunca), **Revisar en SINUBE** y subir solo lo pendiente; anticipos en tanda aparte. Corrige que solo se leían 25 emitidos | `c3ddb0d`, `fa1e67f` |
| Respaldo | Lunes a viernes, `pg_dump` + Storage en ZIP a la carpeta elegida (p. ej. OneDrive); conserva N diarios y el último día hábil del mes en Mensuales | `c3ddb0d` |
| SINUBE | "Exportación a SINUBE" pasa a **Importación a SINUBE**; botón Revisar en SINUBE para clientes y proveedores e "Importar N" solo con los pendientes | `c3ddb0d`, `d515efd` |
| Empresas | Columna Usuarios (solo Administrador), orden y búsqueda por usuario; reporte Excel "Empresas asignadas por usuario" | `d515efd`, `0e01aa3` |

### 1 y 2/11: Resumen fiscal y diseño

| Tema | Qué se hizo | Commit |
|---|---|---|
| Resumen fiscal | Tarjeta **Impuestos**: IVA trasladado/acreditable, IVA retenido e ISR retenido de emitidos y recibidos, por flujo (facturas PUE − notas de crédito + complementos de pago) | `d193559` |
| Previo de IVA | IVA cobrado (trasladado − retenido por clientes) − IVA acreditable = **IVA por pagar** o **saldo a favor**; aparte, retenciones de IVA por enterar | `d193559` |
| Base de datos | Columnas `pago_iva_trasladado`, `pago_iva_retenido`, `pago_isr_retenido` en `cfdi`, leídas del nodo Totales del complemento de pagos la primera vez que se consulta el periodo; la re-sincronización las limpia | `d193559` |
| Letra | **Segoe UI** en toda la app (variables de Bootstrap y Tabler en `app.css`) | `01d98f9` |
| Administración de XML | Totales como **KPI** siempre abajo en una fila, cada uno del ancho de su contenido. Nómina 1.1 (tarjeta y pestaña) solo si el filtro trae alguna. Detalle de hasta 3 partidas con scroll. Rejilla más ligera; Estado SAT y Tipo como pastillas de color; la última validación pasa al tooltip de Estado SAT | `6f8e227`, `01d98f9` |
| Selector de empresa | La lista muestra los nombres completos | `6f8e227` |
| Recarga | F5, reconexión fallida o URL escrita regresan siempre al **Resumen** | `759051d` |

Tablas y columnas nuevas de esta etapa: `proteccion_llaves`, `configuracion_app` y las columnas `pago_*` de `cfdi`.
Se crean solas al arrancar.

### 2/11 (noche): cancelados, descarga cada 3 horas y series alternas

| Tema | Qué se hizo | Commit |
|---|---|---|
| Herramientas | Pestañas; **Recuperar cancelados** dentro de Herramientas, de ingresos (clientes) y egresos | `4ff733a`, `518f632` |
| Descarga automática | Cada 3 horas (07:00, 10:00, 13:00, 16:00, 19:00, 22:00) | `d7f8324` |
| Importación a SINUBE | Nómina aparte (no se sube) y revisión al terminar; **series alternas** con reintento cuando el folio ya existe, primero en pagos y luego en facturas y notas de clientes ("+R" agrega a la serie: NC- → NC-R); confirmar los que SINUBE dice que ya existen | `812cc10`, `837ec87`, `ce47edb`, `4cf846a`, `c22b735` |
| Empresas | Pestaña **Analizar**: barrido de XML pendientes de SINUBE por empresa (clientes y proveedores) | `bf7674f`, `4f3cc16` |
| XML Azulejeras | ASN conserva la serie y pone CLIENTE MOSTRADOR | `9518012` |

### 3/11: reportes con dashboard, DIOT, visor de nóminas y Resumen

| Tema | Qué se hizo | Commit |
|---|---|---|
| SINUBE | Los pagos de clientes se leen de `DbRecepcionPagosXML` (serie con la que se subieron) en Analizar, Revisión, Importación y Conciliación | `2672965` |
| Reportes de Administración de XML | Cada reporte con su propio dashboard leído de su hoja como tabla: agrupado por receptor (ventas) y por emisor (cuentas por pagar) con esquema y "Total …"; **por mes** (solo con varios meses) con tendencia; **Conceptos** con resumen por clave SAT; **Uso CFDI**, **Método de pago** y **Tipo** con tabla, resumen y dashboard | `574351f`, `61c06b7`, `1f3e6ba`, `79ea0b0`, `dc7433c` |
| DIOT | Acuses de DIOT en Declaraciones (tarjeta propia, formato nuevo y anterior, sin duplicados); comparación del IVA pagado del acuse contra la bóveda con Excel por proveedor | `d3373d2`, `7f147b3` |
| Visor de nóminas | Submenú de Administración de XML (Todos / Visor de nóminas): acumulado anual o del mes, detalle mensual, trabajadores, ISR enterado capturable, subsidio neto (causado − ajustes) y Excel con dashboard | `fd07944`, `b041fc7`, `e1777a6`, `4506d81`, `63f2477` |
| Storage | Fuera de la carpeta del programa: `C:\CFDIManagerDatos\Storage` (rutas de la base actualizadas). QuestPDF sin carpeta de descubrimiento de fuentes: con más de 100,000 archivos bloqueaba los PDF | `a3cbc6a`, `43e8b3b`, `7713c2c` |
| Navegación | Empresas abre con la empresa activa; Declaraciones y Cierre de mes con el periodo de trabajo; **Forzar ventana ahora** de la descarga SAT; barra superior fija | `746c212`, `eeb3b33`, `98c3bdb` |
| Administración de XML | Un solo grupo Herramientas; abre en Todos; avisos de éxito que se ocultan solos; **Relacionados** como pestaña que abre su tarjeta; **Buscar en todo el año** si la búsqueda no encuentra nada en el mes; "Bóveda en red" pasa a **Dirección de datos** | `f104eaf`, `ebb7e82`, `5ffdbc2`, `f3286a9` |
| Resumen | Pestañas **Resumen** y **Proyección de IVA** (IVA cobrado − acreditable + retenciones = total estimado a pagar); KPI en grafito con franja e icono del color del tipo, versión clara y oscura | `87763aa` … `7ebef2e` |
| Empresas → Analizar | Selector de empresas a analizar (búsqueda, todas o ninguna) | `c4117e2`, `88bacdc` |

### 4/11: pagos, imagen del resumen, revisión en SINUBE y Azulejeras

| Tema | Qué se hizo | Commit |
|---|---|---|
| Pagos en los reportes | **Resumen Pagos** (un renglón por pago) y **Detalle Pagos** (uno por factura pagada, agrupado por pago con su "Total pago" y el monto solo en el primer renglón), en el reporte general y en Exportaciones. Textos de más de 32,767 caracteres se recortan (tronaba Exportaciones) | `9b74f13`, `292da30` |
| Imagen del resumen | Herramienta que genera un PNG con los totales de emitidos y recibidos del periodo: solo un lado o ambos, tema claro u oscuro | `74c42bf` … `ad0c3c1` |
| Revisar en SINUBE | Desglose por tipo (facturas, notas, nómina, pagos, traslados). Aviso y botón **Quitar marca** para los marcados como "ya en SINUBE" que SINUBE no tiene con su UUID (folio repetido); ya no cuentan como subidos | `aabe21c`, `400f0d1`, `1bcb061` |
| Traslados propios | Emisor = receptor = la empresa: se suben a SINUBE como cliente **y** proveedor (casilla Traslado en proveedores) y se ven en emitidos y recibidos | `2bbb187`, `2b8e1b1` |
| XML Azulejeras | Series alternas propias (comodín `*` = última letra: 72AA → 72AAA, 76EE → 76EEE). Convierte también la serie de las facturas a clientes con RFC (sin cambiar su nombre). Regla de la 50E de Azulejera BM (conserva la serie). Ya no descarga ZIP | `4b6d91e`, `c4e93ef`, `1ad08c4` |

### 5 al 7/11: CIEC, proveedores, correo y Enviar CFDI

| Tema | Qué se hizo | Commit |
|---|---|---|
| Descarga con CIEC | Muestra el motivo de cada error (lo que contestó el portal o la importación) y reintenta una vez. El SAT no entrega por e.firma XML de más de 6 años: para esos meses se usa la CIEC | `9294389` |
| Importación a SINUBE | Series alternas también para **proveedores** cuando el folio ya existe (solo después de Revisar en SINUBE) | `8f35a73` |
| Correo | Pie editable en Configuración → Correo; tarjeta fluida hasta 760 px; Enviar por correo lleva dentro la **imagen del resumen** con las tarjetas KPI de la pantalla | `e316f56`, `4daf8bb`, `125725f` |
| PDF | Representación impresa con el **formato de SINUBE** (Serie-Folio, impuestos por concepto, importe con letra, sellos y QR); logo del Catálogo de logos, importable desde la dirección o el HTML del formato de SINUBE | `2d81a08` |
| **Enviar CFDI** | Herramienta de Administración de XML: PDF y XML de los marcados (o el activo), un correo por contraparte. Propone el último correo usado con ese RFC o el del catálogo de clientes de SINUBE (`DbCliente`, con su nombre comercial para el PDF); CC recordado por empresa y copia para quien envía | `2d81a08`, `725dc0e` |

Versiones etiquetadas en GitHub: **v0.1.0** (imagen del resumen con KPI) y **v0.2.0** (Enviar CFDI), ambas del 7/11.

## Dónde está cada cosa

| Qué | Dónde |
|---|---|
| Código | GitHub `WebsurferMX/CFDIManagerBoveda`, rama `main`; en el servidor, `Desktop\CFDIManagerBoveda` (`CFDIManager.Web` + `CFDIParser`) |
| App publicada | Servicio de Windows `CFDIManager` en `C:\CFDIManager`, puerto 5000. La contraseña de PostgreSQL y la clave maestra están en `C:\CFDIManager\appsettings.Production.json` (no se sobrescribe al actualizar) |
| Acceso remoto | Tailscale (cuenta WebsurferMX): `http://100.104.146.61:5000`; el cliente debe estar en la misma red de Tailscale |
| App original | `Desktop\CFDIManager`, con git y `CLAUDE.md` (hasta el 28/10) |
| Versiones | Etiquetas de git `v0.1.0` y `v0.2.0` (GitHub) |
| Datos (Storage) | `C:\CFDIManagerDatos\Storage`, fuera de la carpeta del programa; se define por equipo en Herramientas (`carpeta_storage:<EQUIPO>`) |
| XML | `Storage\XML\<RFC>\Emitidos\|Recibidos\AAAA\MM` |
| Logos del PDF | `Storage\Logos\<RFC>.png\|.jpg` (Catálogo de logos de Administración de XML) |
| Ajustes en `configuracion_app` | `correo_pie` (pie de los correos), `correo_cfdi:<empresa>:<RFC>` (último correo de Enviar CFDI por RFC), `correo_cfdi_cc:<empresa>` (CC de Enviar CFDI), `isr_enterado:<empresa>:<año>` (visor de nóminas) |
| Llaves de cifrado (CIEC, SMTP, SINUBE) | Tabla `proteccion_llaves`, cifradas con `CFDI_CLAVE_MAESTRA` (debe ser la misma en todos los equipos). Sin ella se usa `Storage\Llaves` con DPAPI y en otro equipo hay que volver a capturar las contraseñas |
| e.firma | `Storage\Certificados\{RFC}\{RFC}.cer/.key` |
| Sesiones de Claude Code | `websu`: `C:\Users\websu\.claude\projects\`; ERNESTO-CGA: `C:\Users\Ernesto Hernández\.claude\projects\` (copia en `Desktop\projects.rar`); servidor: `C:\Users\CFDI Manager\.claude\projects\` |

## Pendientes conocidos

- Captcha de la CIEC y de declaraciones: hoy se escribe a mano.
- Cierre de mes: cuando el SAT solo no ha terminado la solicitud, mostrar "Pendiente del SAT" y no "Error" (hoy manda
  correo de error); avisar antes de pedir por e.firma XML de más de 6 años (el SAT los rechaza).
- Enviar CFDI: el formato SINUBE no muestra el detalle del complemento de pagos ni de nómina; revisar cómo llegan los
  correos en Outlook de escritorio.
- SINUBE: la consulta de `DbEmpresa` regresa una contraseña en texto plano (campo `pwd`); no se usa ni se guarda.
- Migrar a Bootstrap 6 cuando salga estable.
- De `CLAUDE.md` de CFDIManager: limpiar servicios sin uso (`CfdiPdfService`, `ImportacionXmlService`), revisar la
  licencia de QuestPDF y el estado de pago PPD.
