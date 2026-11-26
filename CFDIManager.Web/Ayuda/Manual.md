# Manual de usuario

CFDI Manager guarda y organiza los comprobantes fiscales (CFDI) de tus empresas: los descarga del SAT, los
clasifica y te da reportes, PDF y Excel. Este manual empieza por la configuración inicial, en el orden en que se
hace la primera vez.

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
| **Registro de cambios** | Lo nuevo en cada versión |
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

## Problemas frecuentes

| Problema | Qué hacer |
|---|---|
| "No se pudo conectar al servidor" | Revisa la red y la dirección en **Configuración…** → **Probar** |
| "Usuario o contraseña incorrectos" | Revisa mayúsculas; tras 5 intentos espera 15 minutos o pide al administrador que te desbloquee |
| "No tienes empresas asignadas" | El administrador debe asignarte empresas en **Usuarios** |
| No se enciende la descarga automática | La empresa necesita e.firma en **Empresas** → **Accesos SAT** |
| Los importes se ven cortados | Aleja la vista con <kbd>Ctrl</kbd> + rueda del mouse o <kbd>Ctrl</kbd> <kbd>-</kbd> |
| No aparecen los avisos de Windows | Revisa que **No molestar** esté apagado en Configuración de Windows → Sistema → Notificaciones |
