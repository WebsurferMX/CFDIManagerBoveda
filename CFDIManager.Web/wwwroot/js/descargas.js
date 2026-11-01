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

// Archivos grandes que el servidor entrega por URL (ZIP de XML y PDF).
window.descargarUrl = (url) => {
    const enlace = document.createElement("a");

    enlace.href = url;
    enlace.download = "";

    document.body.appendChild(enlace);
    enlace.click();
    document.body.removeChild(enlace);
};
