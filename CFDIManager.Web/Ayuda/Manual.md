# Manual de usuario

CFDI Manager guarda y organiza los comprobantes fiscales (CFDI) de tus empresas: los descarga del SAT, los
clasifica y te da reportes, PDF y Excel. Este manual tiene dos partes: la **configuración inicial**, en el orden en
que se hace la primera vez, y el **uso diario**, a partir de [La pantalla principal](#la-pantalla-principal).

> Las imágenes son de una empresa de demostración con datos ficticios.

## Antes de empezar

### Dos formas de usar CFDI Manager

| Forma | Para quién | Dónde quedan los datos |
|---|---|---|
| **Instalación propia** | Un despacho o empresa que trabaja solo con sus RFC | En tu PC (base de datos, XML y e.firmas) |
| **Conectado a un servidor** | Equipos de una oficina que ya tiene CFDI Manager en un servidor | En el servidor; tu PC solo los consulta |

### Qué necesitas

- Una PC con Windows 10 u 11 e internet.
- De cada empresa: su **RFC**, su **e.firma** (archivos `.cer` y `.key` con su contraseña) y, si quieres consultar
  el portal del SAT, su **contraseña del SAT (CIEC)**.
- Opcional: la **Constancia de Situación Fiscal** en PDF de cada empresa.

## Instalar CFDI Manager

1. Descarga el instalador `CFDIManager.Escritorio-win-Setup.exe` con el enlace que te compartimos.
2. Ábrelo. Windows puede mostrar "Windows protegió su PC": da clic en **Más información** → **Ejecutar de todas
   formas**.
3. Si a tu PC le faltan componentes de Microsoft (.NET, WebView2 o Visual C++), el instalador los descarga e instala
   solo. Puede tardar unos minutos la primera vez.
4. Al terminar, CFDI Manager se abre y queda un acceso directo en el escritorio.

No se necesita ser administrador de Windows.

## Primer arranque

La primera vez aparece la ventana **Configuración de CFDI Manager**. En **Modo** elige una de las dos opciones.

### Instalación propia en esta PC

1. **Carpeta de datos**: dónde se guardará todo. Puedes dejar la que propone (`C:\CFDIManagerDatos`) o elegir otra
   con el botón **…**. Abajo dice si es una **Instalación nueva** o una **Instalación existente** (por ejemplo,
   después de reinstalar).
2. **Usuario administrador**: el usuario con el que vas a entrar (por omisión `admin`).
3. **Contraseña** y **Confirmar**: al menos 8 caracteres, con una mayúscula, una minúscula y un número.
4. Da clic en **Guardar**.

CFDI Manager prepara la base de datos ("Creando la base de datos…", "Iniciando PostgreSQL…"). Tarda alrededor de
15 segundos la primera vez; después abre la pantalla para iniciar sesión.

> Guarda la contraseña del administrador en un lugar seguro. Con ella se crean los demás usuarios.

### Conectar a un servidor

1. **Servidores (uno por renglón, en orden)**: la dirección que te dio el administrador, por ejemplo
   `192.168.1.20:5000`. Si hay una segunda dirección (por ejemplo, para conectarte fuera de la oficina), escríbela
   en el siguiente renglón: CFDI Manager usa la primera que responda.
2. Da clic en **Probar**. Junto a cada dirección debe decir **responde**.
3. **Carpeta para localizar XML**: cuando uses **Localizar en carpeta**, el XML se copia aquí para abrirlo en el
   Explorador de tu PC. La información sigue en el servidor.
4. Da clic en **Guardar**.

Si no se puede conectar, aparece "No se pudo conectar al servidor" con los botones **Reintentar** y
**Configuración…**. Revisa que estés en la red de la oficina y que la dirección sea correcta.

### Opciones de la ventana

- **Al cerrar la ventana, seguir en la bandeja**: al cerrar con la X, CFDI Manager sigue trabajando junto al reloj de
  Windows (por ejemplo, con la descarga del SAT). Recomendado.
- **Iniciar con Windows**: CFDI Manager arranca solo al encender la PC.

Puedes volver a esta ventana cuando quieras: clic derecho en el ícono de CFDI Manager junto al reloj →
**Configuración…**.

## Iniciar sesión

Escribe tu **Usuario** y **Contraseña** y da clic en **Entrar**.

- Si te equivocas 5 veces, la cuenta se bloquea 15 minutos.
- Si no usas CFDI Manager por un rato (30 minutos), la sesión se cierra sola y hay que volver a entrar.
- Si el administrador te dio una **contraseña temporal**, al entrar se te pedirá elegir una propia.

### Tu menú de usuario

Arriba a la derecha aparece tu nombre. Al darle clic:

| Opción | Para qué |
|---|---|
| **Ayuda** | Este manual |
| **Contraseña** | Cambiar tu contraseña: escribe la actual, la nueva y su confirmación |
| **Idioma: Español** | Por ahora CFDI Manager solo está en español |
| **Registro de cambios** | Lo nuevo en cada versión |
| **Manual técnico** | Solo el Administrador: cómo está hecho el sistema, cómo se publica y la bitácora de cambios |
| **Acerca de** | La versión de CFDI Manager que estás usando |
| **Cerrar sesión** | Salir de tu usuario (CFDI Manager sigue abierto) |

## Dar de alta una empresa

Solo el **Administrador** puede dar de alta empresas. Entra a **Empresas** en el menú de la izquierda y da clic en
**Nueva empresa**.

### General

- **RFC**: 12 o 13 caracteres, sin espacios ni guiones.
- **Razón social**: completa, como aparece en la constancia.
- **Nombre corto**: el nombre que verás en el selector de empresa. Si lo dejas vacío, se usa la razón social.
- **Estado**: **Activa**.

Da clic en **Registrar empresa**. Al guardar se crean las carpetas de la empresa para sus XML.

### Datos fiscales

Con **Cargar CIF (PDF)** sube la Constancia de Situación Fiscal. CFDI Manager lee de ahí los regímenes, actividades,
obligaciones y el domicilio fiscal. La empresa debe estar guardada antes.

### Accesos SAT

Aquí se capturan los datos para hablar con el SAT. Después de capturarlos da clic en **Guardar cambios**.

**e.firma** (necesaria para descargar del SAT):

1. **Archivo CER**: el archivo `.cer` de la e.firma. Al cargarlo se muestra su RFC y su vencimiento.
2. **Archivo KEY**: el archivo `.key`.
3. **Contraseña de Llave**: la contraseña de la e.firma.

Al guardar se revisa que la e.firma sea del mismo RFC de la empresa, que no esté vencida y que no sea un sello
digital (CSD) en lugar de la e.firma.

**Contraseña SAT (CIEC)** (opcional; sirve para consultar el portal del SAT y bajar la constancia y opiniones):

1. **Agregar contraseña** y escríbela. El usuario es el RFC de la empresa.
2. **Validar contraseña**: aparece una imagen del SAT; escribe su texto en **Texto del captcha** y da clic en
   **Iniciar sesión**. Si todo está bien, la última prueba queda como **Correcta**.

**Descarga automática**:

- Marca **Descargar automáticamente del SAT** y elige qué bajar: **Emitidos**, **Recibidos** y **Metadatos (estado
  SAT)**.
- **Descargar desde**: la fecha desde la que quieres tus CFDI. La primera vez se piden mes por mes desde esa fecha;
  después, solo lo nuevo.

### Revisar la empresa

En la lista de **Empresas** cada renglón muestra si tiene e.firma (**FIEL OK**, **Sin FIEL** o **FIEL de otro RFC**),
cuándo vence y si tiene CIEC. El botón de estado SAT avisa si la e.firma está **Vigente**, **Próxima a vencer** (menos
de 60 días) o **Vencida**.

## Descargar tus CFDI del SAT

### Descarga automática

Arriba, junto a la empresa activa, está el interruptor **Descarga SAT automática**. Al encenderlo:

- CFDI Manager revisa cada 30 minutos y pide al SAT lo que falte, desde la fecha **Descargar desde** hasta ayer.
- Debajo del interruptor ves la última revisión: fecha, cuántos XML llegaron y cuántas solicitudes siguen en proceso.
- **Descargar ahora** revisa en ese momento sin esperar.

El interruptor no se puede encender si la empresa no tiene e.firma ("Sin e.firma"). Con CFDI Manager en la bandeja,
la descarga sigue aunque cierres la ventana.

> El SAT tarda de minutos a horas en preparar cada solicitud. Es normal que los XML no lleguen al instante.

### Descarga manual (Descargas SAT)

En **Descargas SAT**, pestaña **Con e.firma (descarga masiva)**:

1. **Probar certificados** y **Probar conexión SAT** confirman que la e.firma funciona.
2. En **Nueva solicitud** elige **Fecha inicial**, **Fecha final**, **Emitidos** o **Recibidos**, y qué bajar:
   **XML (CFDI)** o **Metadatos (lista y estatus)**.
3. Da clic en **Consultar SAT**.
4. En **Solicitudes** sigue el estado. Con **Verificar cada minuto** marcado se revisa solo:

| Estado | Qué significa |
|---|---|
| **Aceptada** / **En proceso** | El SAT la está preparando |
| **Terminada** | Lista: da clic en **Descargar e importar** |
| **Descargada** | Los XML ya están en CFDI Manager |
| **Error** / **Rechazada** / **Vencida** | El SAT no la entregó; vuelve a pedirla |

Los XML recibidos que están cancelados no los entrega el SAT; consúltalos pidiendo **Metadatos**.

La pestaña **Con CIEC (portal SAT)** busca en el portal del SAT (máximo 500 CFDI por consulta). Para periodos largos
conviene la descarga con e.firma.

## Usuarios y permisos

Solo el **Administrador** entra a **Usuarios**. Da clic en **Nuevo usuario**.

1. **Datos generales**: **Usuario** (con el que entrará; no se puede cambiar después), **Nombre completo** y **Correo
   electrónico**.
2. **Acceso**: **Generar contraseña temporal** (recomendado: el usuario elige la suya al entrar) o **Escribir la
   contraseña**. Con el correo configurado puedes marcar **Enviar correo de bienvenida**.
3. **Rol**:

| Rol | Qué puede hacer |
|---|---|
| **Administrador** | Todo: usuarios, empresas y todas las operaciones |
| **Supervisor** | Configura y opera sus empresas asignadas |
| **Contador** | Importa, descarga y exporta CFDI de sus empresas asignadas |
| **Consulta** | Solo consulta y reportes de sus empresas asignadas |

4. **Tareas**: las opciones del menú que verá. Las que desmarques no le aparecerán. **Resumen** siempre está.
5. **Empresas asignadas**: las empresas que podrá consultar. El Administrador tiene todas.
6. Da clic en **Crear usuario**. Si generaste una contraseña temporal, se muestra una sola vez: compártela por un
   medio seguro.

Desde la lista puedes **Editar**, **Restablecer contraseña** o desbloquear a un usuario que se equivocó varias veces.

## Correo

Para enviar CFDI por correo, bienvenidas y contraseñas. Entra a **Correo** (solo Administrador):

1. Activa **Habilitado**.
2. Usa un botón de **Valores comunes** (**Microsoft 365**, **Gmail / Workspace** o **SSL 465**) o captura
   **Servidor**, **Puerto** y **Seguridad**.
3. **Usuario** y **Contraseña** de la cuenta de correo. En Microsoft 365 y Gmail con verificación en dos pasos usa una
   *contraseña de aplicación*.
4. **Correo remitente** y **Nombre del remitente**.
5. Da clic en **Guardar** y después, en **Enviar correo de prueba**, escribe un correo y da clic en **Probar**. Revisa
   la bandeja de entrada (y la de spam).

## Respaldo

En instalación propia todo vive en tu PC: si el disco falla, se pierde. Configura el respaldo desde el primer día.
Entra a **Herramientas** → **Respaldo** (solo Administrador):

1. **Carpeta destino**: de preferencia una carpeta de OneDrive o Google Drive, o un disco externo, para que el
   respaldo quede fuera de la PC.
2. **Hora (lunes a viernes)**: por omisión 02:00. La PC debe estar encendida y CFDI Manager abierto o en la bandeja.
3. **Diarios a conservar**: por omisión 5.
4. Da clic en **Guardar**. Con **Respaldar ahora** haces uno en ese momento.

Cada respaldo es un ZIP con la base de datos y los archivos. Además de los diarios, se guarda uno por mes que no se
borra.

## El ícono junto al reloj

Clic derecho en el ícono de CFDI Manager junto al reloj de Windows:

| Opción | Para qué |
|---|---|
| **Abrir CFDI Manager** | Muestra la ventana |
| **Recargar** | Vuelve a cargar la pantalla |
| **Configuración…** | Cambia el modo, los servidores o las opciones de la ventana |
| **Buscar actualizaciones** | Revisa si hay una versión nueva |
| **Salir** | Cierra CFDI Manager por completo y detiene la descarga automática |

### Actualizaciones

CFDI Manager revisa solo si hay una versión nueva al abrirse y cada 6 horas. Cuando la encuentra, avisa y la instala
al salir, o en ese momento con **Instalar la versión… y reiniciar** en el menú del ícono. Tus datos no se tocan.

## La pantalla principal

Arriba de cada pantalla está la barra con lo que usas todo el tiempo:

| Elemento | Para qué |
|---|---|
| **Empresa activa** | Elige con qué empresa trabajas. Escribe parte del RFC o del nombre para buscarla. Todas las pantallas muestran solo esa empresa |
| **Descarga SAT automática** | Enciende o apaga la descarga del SAT de la empresa activa (ver [Descargar tus CFDI del SAT](#descargar-tus-cfdi-del-sat)) |
| **XML indexados** | Cuántos XML de la empresa tiene registrados CFDI Manager |
| **Actualizar índice** | Lee otra vez la carpeta de XML de la empresa y registra los nuevos. Se hace solo cada 10 minutos; úsalo si acabas de copiar XML a la carpeta |
| Luna / sol | Cambia entre tema claro y oscuro |

A la izquierda está el menú. Solo ves las opciones que el administrador te asignó; con el botón ☰ lo haces más angosto.

## Resumen

Es la primera pantalla: los totales del periodo de la empresa activa. Cambia **Fecha inicial** y **Fecha final** y da
clic en **Consultar**.

![Resumen fiscal](ayuda/img/resumen.png)

- Arriba, los cuatro totales: **Facturación neta** (facturas menos notas de crédito), **Compras netas**, **Ventas −
  compras** y **CFDI del periodo**.
- **Emitidos (Facturación y ventas)**: lo que la empresa facturó. **Recibidos (Compras y gastos)**: lo que le
  facturaron.
- La barra de colores muestra cuántos CFDI hay de cada tipo, y las tarjetas su importe: **Facturas**, **Nómina**,
  **Notas de crédito**, **Traslados** y **Pagos**.

### Proyección de IVA

En la pestaña **Proyección de IVA** ves un cálculo estimado del IVA del periodo:

![Proyección de IVA](ayuda/img/proyeccion-iva.png)

- **Emitidos**: el **IVA trasladado** a tus clientes y lo que ellos te retuvieron.
- **Recibidos**: el **IVA acreditable** de tus proveedores y lo que tú les retuviste.
- **Proyección**: **IVA cobrado** − **IVA acreditable** = **IVA por pagar** (o **Saldo a favor de IVA**), más las
  **Retenciones por enterar** = **Total estimado a pagar**.

Toma en cuenta las facturas pagadas en una sola exhibición (PUE), las notas de crédito y los complementos de pago. Es
una estimación para planear; la declaración la hace tu contador.

## CFDI de ingresos y egresos

**CFDI de ingresos** muestra lo que la empresa emitió y **CFDI de egresos** lo que recibió. Las dos funcionan igual.

![CFDI emitidos](ayuda/img/cfdi-ingresos.png)

1. Filtra por **Tipo CFDI** (facturas, nómina, notas de crédito, traslados o pagos), por fechas, o escribe en
   **Búsqueda** un UUID, RFC, razón social, serie o folio. Da clic en **Consultar**.
2. Las tarjetas de colores también filtran: da clic en **Notas de crédito** para ver solo esas.
3. En cada renglón, los íconos de **Acciones** abren el XML, generan el PDF o consultan el estado en el SAT.
4. Da clic en un renglón para ver su detalle:

![Detalle de un CFDI](ayuda/img/cfdi-detalle.png)

### Estado SAT

La columna **Estado SAT** dice si el CFDI está vigente o cancelado según el SAT, y cuándo se revisó. Para revisar
varios a la vez usa **Validar pendientes SAT**; con **Lote SAT** eliges cuántos revisa en cada vuelta. El avance se ve
en la tarjeta **Validación SAT por lote** (vigentes, cancelados, no encontrados y errores).

## Administración de XML

Es la pantalla para trabajar con los XML: buscarlos, revisarlos, sacar reportes, PDF, ZIP y enviarlos por correo.

![Administración de XML](ayuda/img/administracion-xml.png)

### Elegir qué XML ver

1. **Tipo**: **Emitidos** o **Recibidos**.
2. **Año** y **Mes**. Con <kbd>Ctrl</kbd> + clic eliges varios meses, o enciende **Acumular** para sumar los meses
   anteriores.
3. **Día**: **Todo el mes** o un día (con <kbd>Ctrl</kbd> + clic, varios).
4. Las pestañas de la tabla separan por tipo: **Facturas**, **Nómina 1.2**, **Notas de crédito**, **Traslados** y
   **Pagos**.
5. En las pestañas de **Nómina** aparece además la **Fecha de pago** de cada recibo.
6. **Buscar por** encuentra un UUID, RFC, nombre, serie o folio dentro de lo filtrado.
7. **Filtrar** (abajo de la tabla) deja solo **Vigentes**, **Cancelados**, **Sin validar en SAT**, **Duplicados**,
   **PPD** o **Relacionados**.

Al dar clic en un renglón, arriba aparece la **vista previa**: el XML y la primera hoja del PDF (doble clic para
abrirlos). Abajo, **Conceptos**, **Impuestos**, **Impuestos locales** y **Complemento** muestran el detalle.

### Marcar comprobantes

Las herramientas trabajan sobre los renglones **marcados**; si no marcas ninguno, sobre **todo lo filtrado**.

- <kbd>Ctrl</kbd> + clic marca o desmarca un renglón.
- <kbd>Shift</kbd> + clic marca un rango.
- La casilla del encabezado marca todo el filtro, en todas las páginas.

Con **Columnas** eliges qué columnas ver.

### Herramientas

El panel **Herramientas**, a la derecha de la pantalla:

| Herramienta | Qué hace |
|---|---|
| **Exportar a Excel** | El reporte elegido en **Nvo. reporte de** (general, conceptos, agrupado por cliente o proveedor, por mes, Pareto 80-20…) |
| **Imagen del resumen** | Una imagen con los totales para pegar en un correo o en WhatsApp |
| **Enviar por correo** | Manda los reportes en Excel y la imagen del resumen |
| **Enviar CFDI** | Manda a cada cliente o proveedor el PDF y XML de sus comprobantes (hasta 200 a la vez) |
| **Abrir XML** / **Generar PDF** | El XML o el PDF del renglón elegido |
| **Valida Estado SAT** | Revisa en el SAT si siguen vigentes |
| **Localizar XML** | Abre el Explorador de Windows con el archivo marcado |
| **Importar XML** | Agrega XML desde tu PC o desde una carpeta |
| **Descargar XML (ZIP)** / **Descargar PDF (ZIP)** | Todos los XML o PDF en un ZIP |
| **Agrupaciones** | Totales por emisor, receptor, mes, uso de CFDI, método de pago o tipo |
| **Renombrar XML's** | Copias de los XML con el nombre que elijas (por ejemplo Serie_Folio_UUID), en un ZIP; los originales no cambian |
| **Catálogo de logos** | El logo que aparece en los PDF de cada RFC emisor |
| **Verifica series** | Folios faltantes o repetidos de cada serie |
| **Leer complemento** | El complemento del XML (pagos, nómina…) en forma de árbol |
| **Dirección de datos** | En qué carpeta están los XML de la empresa |

Si no ves alguna, el administrador no te la asignó.

### Imagen del resumen

Elige **Solo emitidos**, **Solo recibidos** o **Emitidos y recibidos**, y después **Copiar para correo**, **Copiar para
WhatsApp** o **Descargar imagen**. Pégala con <kbd>Ctrl</kbd> + <kbd>V</kbd> en el correo o en el chat.

![Imagen del resumen](ayuda/img/imagen-resumen.png)

## Visor de nóminas

En **Administración de XML** → **Visor de nóminas** ves los acumulados de la nómina que timbró la empresa, por fecha
de pago. Los recibos cancelados no se cuentan.

![Visor de nóminas](ayuda/img/visor-nominas.png)

1. Elige el **Ejercicio** y el **Mes** (o **Todo el año**).
2. **Acumulado anual**: sueldos, ISR retenido, subsidio, PTU y neto pagado, con el detalle de la nómina y el
   **ISR retenido y enterado**.
3. **Detalle mensual**: los mismos datos mes por mes. Aquí capturas el **ISR enterado** de cada mes y das clic en
   **Guardar ISR enterado**; así ves la diferencia contra lo retenido.
4. **Trabajadores**: un renglón por trabajador. Da clic en uno para ver sus recibos, con el PDF y XML de cada uno:

![Recibos de un trabajador](ayuda/img/visor-nominas-trabajador.png)

Con **Excel** descargas todo el visor.

## Importar XML

Si tienes XML que no llegaron por la descarga del SAT (por ejemplo, de otro sistema), en **Importaciones**:

![Importación de XML](ayuda/img/importaciones.png)

1. **Elegir archivos**: hasta 500 XML por vez.
2. **Clasificar e importar**. CFDI Manager decide solo si cada XML es emitido o recibido según el RFC.
3. Los contadores dicen cuántos fueron **Correctos**, **Emitidos**, **Recibidos**, **Actualizados** (ya existían),
   **No identificados** (de otro RFC) y **Errores**. La tabla de abajo da el detalle de cada archivo.

Para importar una carpeta completa con subcarpetas usa **Administración de XML** → **Importar XML** → **Importar
carpeta**.

## Exportaciones

Para sacar un Excel de un periodo sin entrar a Administración de XML:

![Exportaciones](ayuda/img/exportaciones.png)

1. **Fecha inicial** y **Fecha final**.
2. **Reporte a generar**: **Emitidos y recibidos**, **Solo CFDI emitidos** o **Solo CFDI recibidos**.
3. **Tipo de CFDI**: todos o uno.
4. **Generar Excel**. Emitidos y recibidos salen en archivos separados, con una hoja de resumen y una por tipo de
   CFDI. Desde el resultado puedes **Enviar por correo**.

## EFOS / EDOS

Revisa si tus proveedores o clientes están en la lista del artículo 69-B del SAT (empresas que facturan operaciones
simuladas).

![EFOS / EDOS](ayuda/img/efos.png)

1. La primera vez, y de vez en cuando para tenerla al día, da clic en **Descargar del SAT** para bajar la lista.
2. CFDI Manager cruza la lista con tus CFDI y muestra el semáforo: **Riesgo alto**, **Riesgo medio**, **Riesgo bajo** o
   **Sin coincidencias**.
3. Las tarjetas separan por situación: **Presunto**, **Definitivo**, **Desvirtuado** y **Sentencia favorable**.
4. La tabla **Proveedores en la lista 69-B** dice qué hacer con cada uno en **Acción requerida**. Con 🔍 ves sus CFDI.

> Un proveedor en situación **Definitivo** es el caso grave: sus CFDI no tienen efectos fiscales. Revísalo con tu
> contador.

## SINUBE

Si tu empresa usa SINUBE para la contabilidad, CFDI Manager le envía las facturas y concilia lo que tiene cada uno.
El administrador captura primero el acceso en **Importación a SINUBE** → **Herramientas** (**Probar acceso** y
**Guardar**).

### Importación a SINUBE

1. Elige la pestaña: **Importar facturas de Clientes**, **Importar facturas de Proveedores** o **Importar facturas de
   Clientes sin folio**.
2. Elige el **Periodo**.
3. **Revisar en SINUBE**: compara con lo que SINUBE ya tiene. Las tarjetas dicen cuántos **Ya en SINUBE**, cuántos
   **Por subir** y cuáles tienen el folio ocupado.
4. **Importar a SINUBE**. Al terminar, la tabla de resultados dice qué se insertó, qué ya existía y qué tuvo error.

Si un folio ya existe en SINUBE con otra factura, en **Herramientas** → **Series alternas cuando el folio ya existe**
se indica qué serie usar en su lugar.

### Conciliación SINUBE

Cruza por UUID lo que tiene el SAT, los XML de CFDI Manager y lo registrado en SINUBE:

1. Elige **Proveedores** o **Clientes**, el **Año** y el **Mes**, y da clic en **Carga y analiza**.
2. Las tarjetas filtran la tabla: **Conciliados**, **Faltan en SINUBE**, **No están en el SAT**, **Falta XML en
   bóveda**, **Estatus distinto**, **Sin póliza** y **Mes distinto**.
3. Con **Descargar XML faltantes** bajas del SAT los que no están en CFDI Manager.
4. **Excel** o **Enviar por correo** para compartir el resultado.

## XML Azulejeras (Clientes)

Utilería para las empresas que facturan en mostrador al público en general: cambia la serie y el nombre del cliente de
esas facturas según la sucursal, para subirlas a SINUBE. Solo aparece en las empresas que la tienen configurada.

1. Elige el periodo (**Desde** y **Hasta**) y da clic en **Analizar**.
2. Marca las series a convertir y da clic en **Generar XML Azulejeras**.
3. En **Importación a SINUBE**, **Revisar en SINUBE** y después **Subir a SINUBE**.

## Problemas frecuentes

| Problema | Qué hacer |
|---|---|
| "No se pudo conectar al servidor" | Revisa la red y la dirección en **Configuración…** → **Probar** |
| "Usuario o contraseña incorrectos" | Revisa mayúsculas; tras 5 intentos espera 15 minutos o pide al administrador que te desbloquee |
| "No tienes empresas asignadas" | El administrador debe asignarte empresas en **Usuarios** |
| No se enciende la descarga automática | La empresa necesita e.firma en **Empresas** → **Accesos SAT** |
| Los importes se ven cortados | Aleja la vista con <kbd>Ctrl</kbd> + rueda del mouse o <kbd>Ctrl</kbd> <kbd>-</kbd> |
| No aparecen los avisos de Windows | Revisa que **No molestar** esté apagado en Configuración de Windows → Sistema → Notificaciones |
