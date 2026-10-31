# Proceso de importación de facturas XML (clientes y proveedores) a SINUBE

Fuente: análisis estático (REA) de `C:\HerramientasSinube\HerramientasSinube.exe` v13.9.17.1640
(.NET 4.6.2, SHA-256 `e105867c…4543`) y de la captura de pantalla de la versión 2026.6.11.
No se ejecutó la herramienta; el comportamiento descrito sale del IL y de las cadenas del ejecutable.

| Pantalla | Clase | Menú |
|---|---|---|
| Importar facturas de Xml (V2) — **clientes / emitidas** | `frmImportarFacturasDeXmlV2` | Facturas de clientes |
| Importación de facturas de proveedor desde XML (EP V2) — **proveedores / recibidas** | `frmFacturasProveedorXMLV2` | Facturas de proveedor |

---

## 0. Requisito previo (una sola vez): Parámetros → Opciones

Las dos pantallas validan esto antes de procesar y se detienen si falta:

| Campo en Opciones | Uso | Mensaje si falta |
|---|---|---|
| **Empresa** | Debe ser el **RFC de la empresa** en SINUBE (se compara con el RFC emisor de las facturas de clientes y se usa como nombre de carpeta en proveedores) | "Debe capturar la empresa en Parametros->Opciones" |
| **Sucursal** | Sucursal destino (p. ej. `Matriz`) | "Debe capturar la sucursal…" |
| **Contraseña para importaciones SINUBE** | Se envía en la importación de clientes | "Debe capturar el password…" |
| **Ruta para descargar** / "Usar ruta predeterminada SINUBE" | Carpeta base del modo automático de proveedores | — |

Los valores se guardan cifrados en `%APPDATA%\…\Parametros.xml`.

---

## 1. Facturas de CLIENTES (CFDI emitidos)

### 1.1 Preparar la carpeta
1. Junta en **una sola carpeta** los XML emitidos que vas a subir (p. ej. un mes): `…\Emitidas\2026\10`.
   - Solo se leen los `*.xml` de esa carpeta, **sin subcarpetas**.
   - Todos deben ser de la misma empresa (RFC emisor = Empresa de Opciones).
2. No incluyas recibos de nómina ni XML sin timbrar (se rechazan).

### 1.2 Configurar la pantalla
1. Menú **Facturas de clientes → Importar facturas de Xml (V2)**.
2. **Ruta Xmls**: botón de carpeta → selecciona la carpeta del paso 1.1.
3. Elige cómo se identifican los productos (opciones excluyentes):

| Opción | Parámetro enviado | Cuándo usarla |
|---|---|---|
| Usar NoIdentificacion del CFDI (predeterminada) | `omiteNoId=0` | El catálogo de productos de SINUBE usa los mismos códigos que el `NoIdentificacion` del XML |
| Omitir noIdentificacion y utilizar un solo código genérico | `omiteNoId=1` | No te interesa el detalle por producto |
| Usar código prod. SAT a 6 posiciones | `usarCodSat6Pos=1` | Solo CFDI 3.3/4.0 |
| Usar código prod. SAT como viene en el CFDI | `usarCodSatComoVieneCfdi=1` | Solo CFDI 3.3/4.0 |

4. Casillas opcionales:

| Casilla | Parámetro | Efecto |
|---|---|---|
| Usar atributo comprobante.folio para obtener serie/folio | `serieFolioDeFolio=1` | Cuando el folio viene como `FAC123` |
| Agrupar X codigoSAT en facturas de más de 200 conceptos | `agruparxcodsat=1` | Facturas grandes (no global) |
| Sustituir noIdentificacion por prod. en cat. de clientes | `usarProdCtes=1` | Usa el producto del catálogo del cliente |

5. Deja **sin marcar** "Pruebas (facturanube)" y "Pruebas (si-nube-fb)" (envían a ambientes de prueba).

### 1.3 Importar
Pulsa **Importar**. Para cada XML la herramienta:

1. **Valida** el XML (si falla, lo mueve a `\errores` y escribe el motivo en la tabla):
   - XML legible y con atributo `Version` 3.3 / 4.0 (o `version` en 3.2)
   - `Fecha`, nodo `Emisor` con `Rfc`, nodo `Receptor`, nodo `Conceptos`
   - `Complemento/tfd:TimbreFiscalDigital` presente (si falta: "El CFDI no está timbrado")
   - Sin `nomina12:Nomina` (si lo trae: "Este CFDI es de un recibo de nómina")
   - RFC emisor = Empresa de Opciones (si no: "El RFC del archivo no coincide con el configurado en Opciones")
2. **Revisa IEPS**: si algún concepto tiene una `ClaveProdServ` de combustibles, bebidas alcohólicas o tabacos, consulta en SINUBE si ese producto existe sin tasa IEPS. Si hay alguno, pregunta "¿Desea continuar?" y "¿Desea aplicar este criterio para las demás facturas?".
3. **Envía** el XML a SINUBE (ver §3).
4. **Resultado**:
   - Respuesta `SINUBE_OK…` → se cuenta como insertada y el XML se mueve a `\procesados`.
   - Respuesta terminada en `[TQ]` → "XML muy grande. Actualizándose en segundo plano"; reintenta cada 5 s hasta 9 veces.
   - Error de red → hasta 4 reintentos y luego se reporta.
5. Al final: "El proceso terminó correctamente". Con **Copiar resultado** copias la tabla (Error, Serie, Folio, RFC receptor, Mensaje).

Para volver a importar un XML rechazado: corrige la causa y regrésalo de `\errores` a la carpeta principal.

---

## 2. Facturas de PROVEEDORES (CFDI recibidos)

### 2.1 Elegir el modo

**Modo automático** (combo *Periodo* + casillas Ingreso/Egreso/Pago). Busca exactamente esta estructura:

```
<Ruta de Opciones>\<RFC Empresa>\<Año>\RECIBIDAS\INGRESO\*.xml
<Ruta de Opciones>\<RFC Empresa>\<Año>\RECIBIDAS\EGRESO\*.xml
<Ruta de Opciones>\<RFC Empresa>\<Año>\RECIBIDAS\PAGO\*.xml
```
El combo *Periodo* muestra las subcarpetas numéricas (años) de `<Ruta>\<RFC>`.

> ⚠️ La bóveda actual (`BovedaCFDI\<RFC>\Recibidas\<Año>\<Mes>`) **no** tiene esa estructura, así que el modo automático no encuentra archivos. Usa el **modo manual**.

**Modo manual** (recomendado con la bóveda): **Elegir carpeta** → selecciona la carpeta del mes, p. ej. `BovedaCFDI\AAL090714841\Recibidas\2026\09`. Se leen todos los `*.xml` de esa carpeta (sin subcarpetas). Al elegir carpeta ya no aplica el modo automático.

### 2.2 Importar
1. Menú **Facturas de proveedor → Importación de facturas de proveedor desde XML (EP V2)**.
2. Elige el modo (2.1) y deja sin marcar las casillas de *Pruebas*.
3. Pulsa **Importar**. Para cada XML:
   1. **Valida**: nodo `Comprobante` (si no: "El comprobante no es de tipo CFDI/CFD o está mal formado"), versión 3.2/3.3/4.0, RFC del emisor, fecha, nodo `Conceptos` y timbre fiscal (3.3/4.0). Si falla: "XML inválido", se mueve a `\errores`.
   2. Si el XML pesa más de ~900,000 caracteres, **vacía el nodo Conceptos** antes de enviarlo y lo marca "(TAMAÑO EXCEDIDO. XML REDUCIDO)".
   3. **Envía** a SINUBE (ver §3); hasta 4 intentos si falla la red.
   4. **Resultado**:
      - Respuesta que empieza con `0_` → "CORRECTO", se mueve a `\procesados`.
      - Respuesta con "El comprobante ya existe" → se informa y también se mueve a `\procesados`.
      - Cualquier otra → "ERROR: …" y el XML se queda en la carpeta.
4. La tabla muestra Error, serie, Folio, Mensaje, RFC, Emisor; **Copiar resultado** la copia.

---

## 3. Detalle técnico del envío (referencia)

Ambas pantallas usan el mismo método (`ClsUtilerias.doPOST`):

```
POST  http://ep.si-nube.appspot.com/blob?par=<Base64(UTF-8(parámetros))>
Content-Type: text/xml        Timeout: 40 s
Cuerpo: el XML del CFDI (UTF-8, líneas unidas con CRLF)
```
`parámetros` son pares `clave=valor` separados por salto de línea (`\n`).

| | Clientes | Proveedores |
|---|---|---|
| Tipo | `tipo=5` | `tipo=49` |
| Credenciales | `suc=<Sucursal>`, `pwd=<Contraseña>`, `empresa=<Empresa>` | `tkn=_herramsin`, `emp=<Empresa>`, `suc=<Sucursal>`, `usr=<Empresa>` |
| Otros | `difzh=<zona horaria, p. ej. -06>`, `esRespaldo=0/1`, `omiteNoId`, `usarCodSat6Pos`, `serieFolioDeFolio`, `agruparxcodsat`, `usarCodSatComoVieneCfdi`, `usarProdCtes`, `taskqueue=1` | `difzh=<zona horaria>` |
| Éxito | empieza con `SINUBE_OK` | empieza con `0_` |

- Los acentos y la ñ de sucursal y contraseña se mandan como entidades HTML (`&aacute;`, `&ntilde;`…).
- Consulta IEPS (solo clientes): `tipo=6`, `emp`, `pwd`, `prod=<NoIdentificacion>`; si responde `0`, el producto no tiene IEPS en SINUBE.
- Ambientes: producción `ep.si-nube.appspot.com`; pruebas `ep.facturanube.appspot.com` y `ep.si-nube-fb.appspot.com`.

---

## 4. Lista rápida

**Clientes**
- [ ] Opciones: Empresa (RFC), Sucursal, Contraseña
- [ ] XML emitidos del periodo en una carpeta, sin nómina
- [ ] Ruta Xmls → carpeta; opción de producto; *Pruebas* sin marcar
- [ ] Importar → revisar la tabla → revisar `\errores`

**Proveedores**
- [ ] Opciones: Empresa (RFC), Sucursal
- [ ] Modo manual → carpeta `Recibidas\<Año>\<Mes>`
- [ ] *Pruebas* sin marcar → Importar
- [ ] Revisar tabla: CORRECTO / ya existe / ERROR → revisar `\errores`

---

### Anexo: evidencia del análisis
- Inventario de tipos del ensamblado: `ev_081a55e8…`
- Miembros, cadenas y llamadas de `frmImportarFacturasDeXmlV2`, `frmFacturasProveedorXMLV2`, `ClsUtilerias`, `frmOpciones`: `ev_611e00eb…`
- Métodos clave: `frmImportarFacturasDeXmlV2.comunica` (0x06000344), `frmFacturasProveedorXMLV2.comunica` (0x0600013b), `ClsUtilerias.doPOST` (0x06000226)
- Sin confirmar (no se observó una ejecución real): el texto exacto que devuelve el servidor y los efectos que tiene en SINUBE cada parámetro opcional.
