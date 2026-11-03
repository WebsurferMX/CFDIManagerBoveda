# Conciliación de XMLs SINUBE vs SAT

Pantalla **Conciliación SINUBE** (`/conciliacion-sinube`): replica *Contabilidad → Herramientas SAT →
XMLs sinube VS SAT* del portal de SINUBE. Cruza por UUID los CFDI **recibidos** del mes que hay en la
bóveda (descargados del SAT, con su estatus) contra los XML de proveedores registrados en SINUBE (con su póliza).

Código: `Services\Sinube\SinubeDatosService.cs` (cliente), `Services\Sinube\ConciliacionSinubeService.cs`
(cruce y Excel), `Components\Pages\ConciliacionSinube.razor`.

## Cómo se obtuvo

| Fuente | Qué aportó |
|---|---|
| REA, análisis estático de `C:\HerramientasSinube\HerramientasSinube.exe` v13.9.17.1640 (SHA-256 `80b58769…a23a`) | Protocolo `getpost` (`ClsSiNubeGetPost`), formato de respuesta (`ClsBlobReader`), dialecto de consulta (`frmProductoSAT.armaConsulta`), credenciales (`clsDatosSistema`: empresa, sucursal, usuario, password) |
| JavaScript público del portal (`https://sinube.app/sifec/<permutación>.cache.html`, GWT) | Nombres de las entidades `Db*`, la pantalla "Conciliación de XMLs sinube Vs SAT", la tabla completa de escapes y el significado de `statusValidadoSAT` |
| Consultas de solo lectura con el usuario de AAL090714841 (30/10/2026) | Confirmación del formato y de los campos de `DbFacturaProveedorDeXML`, `DbPolizaDetComprobantes`, `DbPoliza` |

La conciliación del portal se calcula en el servidor de SINUBE; HerramientasSinube no la tiene.

## Protocolo `getpost` (solo lectura)

`POST http://getpost.si-nube.appspot.com/getpost` (pruebas: `getpost.facturanube.appspot.com`),
`application/x-www-form-urlencoded`, cuerpo:

```
&tipo=3&emp=<empresa>&suc=<sucursal>&usu=<usuario del portal>&pas=<contraseña>&cns=<consulta>
```

Solo `&` se escapa (`%26`). `tipo=1` (listar entidades) y `tipo=2` (campos) ya responden
"Servicio no encontrado"; `SELECT *` da los campos y sus tipos. Una respuesta que empieza con
`Error:` es un error (p. ej. "El usuario '…' ya no es vigente en SiNube", "No existe la entidad … en BD").

Consulta: `SELECT campos FROM DbEntidad WHERE empresa = '<RFC>' AND … TAMPAG 1000 [CURSOR <c>] NOADMINSINUBE`.

Respuesta: filas separadas por `¬`, valores por `|`. Primera fila: `filasEnLaPágina|cursor|campo1|tipo1|campo2|tipo2…`
(cursor `&NullSiNube;` en la última página). Nulo `&NullSiNube;`; fechas en milisegundos desde 1970 (UTC).
Escapes: `&PipeSiNube;` `|`, `&EnterSiNube;` salto de línea, `&SeparadorSiNube;` `¬`, `&DiagonalSiNube;` `/`,
`&MasSiNube;` `+`, `&IgualSiNube;` `=`; las variantes `…2SiNube;`/`…3SiNube;` bajan un nivel.

## Entidades usadas

**`DbFacturaProveedorDeXML`** (XMLs de proveedores). Campos que usa la conciliación:

| Campo | Columna |
|---|---|
| `UUID`, `rfcProveedor`, `razonSocialProveedor`, `fechaFacturaXml`, `importeTotal`, `serieFacturaXml`, `folioFacturaXml` | Datos del comprobante |
| `mes` (AAAAMM, por fecha de la factura) | Periodo |
| `statusValidadoSAT` | Estatus en SINUBE: 0 Cancelado, 1 Vigente, 2 No encontrado, nulo sin validar |
| `tienePoliza`, `tipoPoliza`, `numPoliza`, `mesPoliza`, `polizaCancelada` | Póliza, Mes póliza, Mes distinto |

`tipoFactura` vale 2 en todos los XML importados; el tipo I/E/P sale del CFDI de la bóveda.
`DbPolizaDetComprobantes` liga `uuid` ↔ póliza/renglón (no se usa: `DbFacturaProveedorDeXML` ya trae la póliza).

**`DbFactura`** (facturas y notas de crédito de clientes; modo **Clientes** contra los CFDI emitidos):
`uuid` (minúsculas), `razonSocial`, `fechaFactura`, `importeTotal`, `serieFactura`, `folioFactura`, `mes`,
`statusValidadoSAT`, `canceladaPAC`, `tienePoliza`, `tipoPoliza`, `numPoliza`, `mesPoliza`. No trae RFC del cliente
(solo el número `cliente`); el RFC sale de la bóveda.

- En clientes SINUBE casi nunca valida el estatus (`statusValidadoSAT` vacío): si `canceladaPAC` es verdadero se toma
  como Cancelado.
- Los recibos de **nómina** (tipo N) no están en `DbFactura` (SINUBE los lleva en `DbNominaFactura`): se excluyen y
  la pantalla indica cuántos se omitieron.
- Las facturas sin `uuid` (sin timbrar) se ignoran.
- Octubre 2026, AAL090714841: 2,474 de 2,474 facturas de SINUBE coinciden con la bóveda (incluye 757 notas de crédito).

## Lado SAT: metadatos de la descarga masiva (3 vías)

La conciliación cruza tres fuentes por UUID: **SAT** (existencia y estatus oficiales), **bóveda** (XML local) y
**SINUBE** (registro y póliza). Es el mismo origen que usa el portal de SINUBE en su modo "Usando solicitud de
descarga masiva" (`DbDescargaMasivaSolicitud`).

| Fuente SAT evaluada | ~2,500 CFDI | Decisión |
|---|---|---|
| Consulta de estatus CFDI uno por uno (SOAP) | 10–20 min | No: una llamada por UUID |
| Portal SAT con CIEC | lento, con captcha | No |
| **Descarga masiva, solicitud de Metadata** | el SAT tarda minutos en prepararla; luego instantáneo | **Sí** |

Rendimiento:

- Los metadatos se piden **una vez por mes y dirección** (`SatMetadatosService`) y se guardan con `COPY` binario en
  `sat_metadatos` (foto completa del mes: refleja cancelaciones); `sat_metadatos_periodos` registra la solicitud,
  cuántos CFDI y hasta cuándo cubre. Conciliar después ya no consulta al SAT.
- Si existe una solicitud de metadata del mes (pendiente o descargada, también las hechas en Descargas SAT) se
  reutiliza; no se duplican solicitudes.
- La consulta a SINUBE (la parte por red, 1–3 s) corre en paralelo con la lectura de la bóveda y de los metadatos; el
  cruce es por diccionario de UUID.
- `LeerMetadatos(..., marcarImportados: false)` evita una consulta por UUID al leer los paquetes.
- Sin metadatos del mes, la bóveda hace de SAT (como antes) y la pantalla ofrece "Solicitar al SAT"; la verificación
  se hace cada 15 s y se puede dejar de esperar (la solicitud sigue y se retoma después).

## Descargar los XML que faltan en la bóveda

Botón "Descargar N XML faltantes" (`SatMetadatosService.DescargarXmlFaltantesAsync`):

- Hasta 10 faltantes: una solicitud por UUID. Más: una sola solicitud de XML del mes, de cuyo paquete solo se
  importan los UUID faltantes (`DescargarAsync(..., soloUuids)`; el SAT nombra cada XML `<uuid>.xml` en minúsculas).
- **Recibidos cancelados no se pueden descargar**: el SAT responde "5012 No se permite la descarga de xml que se
  encuentren cancelados" (comprobado el 31/10/2026); la pantalla ya no los ofrece. Los emitidos se piden con todos
  los estados.
- Prueba del 31/10/2026, AAL090714841, octubre: proveedores 10 XML importados (~3 min, por UUID); clientes 138 XML
  importados en 41 s (una solicitud del mes).

## Tener todos los XML al cierre del mes

El SAT no entrega el XML de un recibido ya cancelado, así que hay que descargarlo mientras sigue vigente.
`DescargaAutomaticaSatHostedService` (empresas con "Descargar automáticamente del SAT"):

| Capa | Qué hace | Configuración (`appsettings.json`, sección `DescargaAutomaticaSat`) |
|---|---|---|
| Horarios + ventana móvil | En cada horario pide XML emitidos y recibidos de los últimos días hasta ese momento (atrapa lo que el SAT publica con retraso). Una vez por horario; sobrevive reinicios (se busca en las solicitudes con `Origen = "Ventana"`). | `Horarios` ("07:00,10:00,13:00,16:00,19:00,22:00" desde el 2/11/2026; antes "07:00,21:00"), `VentanaDias` (3) |
| Solo lo nuevo | Las descargas automáticas omiten sin abrir los UUID que ya están en la bóveda (`DescargarAsync(..., soloNuevos: true)`, un solo query, sin distinguir mayúsculas). | — |
| Cierre de mes | Días 1 a N: metadatos del mes anterior, descarga de XML faltantes y registro en `sat_cierres`; aviso por correo a los administradores. También manual en Descargas SAT → Cierre de mes. | `DiasCierre` (3) |
| Interruptor | Apaga el servicio en una instancia (p. ej. una segunda que comparte la base). | `Habilitada` (true) |

Prueba del 31/10/2026: ventana de BTM y GUM importó 7 CFDI que faltaban y omitió 544 existentes; cierre de octubre de
GUM: 1,961 emitidos y 417 recibidos en el SAT, 14 XML descargados, 10 recibidos cancelados sin XML, 0 pendientes.

## Reglas de la conciliación

- **Conciliada**: está en el SAT, en la bóveda y en SINUBE, sin estatus distinto y con total igual (±0.01).
- **No aparece en el SAT**: está en la bóveda o en SINUBE pero no en los metadatos del mes.
- **Falta el XML en la bóveda**: el SAT lo tiene y la bóveda no (hay que descargarlo).
- El estatus del SAT sale de los metadatos; sin ellos, del estatus validado en la bóveda. El total se compara SAT vs
  SINUBE (en complementos de pago los metadatos traen 0 y no se compara).
- **Estatus distinto**: los dos estatus son conocidos (Vigente/Cancelado/No encontrado) y no coinciden, o el SAT lo
  tiene cancelado y SINUBE no ("Cancelado en el SAT, activo en SINUBE").
- **Mes distinto**: tiene póliza y `mesPoliza` ≠ periodo consultado.
- **No contabilizadas**: sin póliza vigente.

## Requisitos

En **Exportación a SINUBE → Acceso** de la empresa: empresa (RFC), sucursal, **usuario vigente del portal** y su
contraseña. La misma contraseña la usa la exportación (`validaUsuarioSinube`, importación de XML).
