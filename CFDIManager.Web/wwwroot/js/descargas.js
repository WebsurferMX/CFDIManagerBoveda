window.descargarArchivoBase64 = (
    nombreArchivo,
    tipoContenido,
    contenidoBase64
) => {
    const enlace = document.createElement("a");

    enlace.download = nombreArchivo;
    enlace.href =
        `data:${tipoContenido};base64,${contenidoBase64}`;

    document.body.appendChild(enlace);
    enlace.click();
    document.body.removeChild(enlace);
};

// Copia al portapapeles. navigator.clipboard solo existe en HTTPS o localhost; entrando por http://IP se usa
// un textarea temporal con execCommand. Regresa true si se copió.
window.copiarTexto = async (texto) => {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(texto);
            return true;
        }
    } catch {
        // Sin permiso: se intenta la alternativa.
    }

    const area = document.createElement("textarea");
    area.value = texto;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.top = "0";
    area.style.left = "0";
    area.style.opacity = "0";

    // Dentro del modal abierto: el foco atrapado por el diálogo impediría seleccionar fuera de él.
    const contenedor = document.activeElement?.closest(".modal, dialog") ?? document.body;
    contenedor.appendChild(area);
    area.focus();
    area.select();

    let copiado = false;
    try {
        copiado = document.execCommand("copy");
    } catch {
        copiado = false;
    }

    contenedor.removeChild(area);
    return copiado;
};

// Archivos grandes que el servidor entrega por URL (ZIP de XML y PDF).
window.descargarUrl = (url) => {
    const enlace = document.createElement("a");

    enlace.href = url;
    enlace.download = "";

    document.body.appendChild(enlace);
    enlace.click();
    document.body.removeChild(enlace);
};
