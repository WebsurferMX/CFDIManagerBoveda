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
    // En CFDI Manager Escritorio copia Windows, también por http://IP.
    if (window.cfdiEscritorio?.disponible()) {
        try {
            await cfdiEscritorio.copiarTexto(texto);
            return true;
        } catch {
            // Se intenta la alternativa del navegador.
        }
    }

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

// Copia un PNG al portapapeles (para pegarlo en correo o WhatsApp). Solo en HTTPS o localhost; por http://IP
// el navegador no lo permite y regresa false (entonces se muestra la imagen para copiarla con clic derecho).
// paraCorreo: solo HTML, el <img> con su tamaño a la vista en los atributos width/height (Outlook web los
// respeta y son los que cambia al redimensionarla con las esquinas) y max-width:100% para que no se salga del
// correo. Sin width:100% en el estilo: eso la deja fija al ancho del correo y no se puede cambiar de tamaño.
// Si también fuera el PNG, Outlook pegaría el PNG a sus píxeles reales. Si no (WhatsApp), solo el PNG.
// En CFDI Manager Escritorio lo copia Windows (sirve también por http://IP).
window.copiarImagenBase64 = async (contenidoBase64, anchoVista, paraCorreo) => {
    try {
        const bytes = Uint8Array.from(atob(contenidoBase64), c => c.charCodeAt(0));
        let html = null;
        if (paraCorreo) {
            // Ancho y alto reales del PNG (IHDR, big endian) para guardar la proporción.
            const vista = new DataView(bytes.buffer);
            const anchoPng = vista.getUint32(16), altoPng = vista.getUint32(20);
            const altoVista = Math.round(altoPng * anchoVista / anchoPng);
            html = `<img src="data:image/png;base64,${contenidoBase64}" alt="Resumen de CFDI"`
                + ` width="${anchoVista}" height="${altoVista}" style="max-width:100%;height:auto">`;
        }

        if (window.cfdiEscritorio?.disponible()) {
            await cfdiEscritorio.copiarImagen(contenidoBase64, html);
            return true;
        }

        if (!navigator.clipboard?.write || !window.isSecureContext || typeof ClipboardItem === "undefined")
            return false;

        const tipos = html !== null
            ? { "text/html": new Blob([html], { type: "text/html" }) }
            : { "image/png": new Blob([bytes], { type: "image/png" }) };

        await navigator.clipboard.write([new ClipboardItem(tipos)]);
        return true;
    } catch {
        return false;
    }
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
