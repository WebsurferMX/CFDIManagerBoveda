// Puente con CFDI Manager Escritorio (WPF + WebView2). En el navegador window.chrome.webview no existe y todo
// sigue como siempre; dentro de la app, el portapapeles y "Localizar en carpeta" los hace Windows.
// Mensajes: { id, tipo, ...datos } → la app responde { id, ok, resultado | error }.
window.cfdiEscritorio = (() => {
    const webview = window.chrome?.webview;
    const pendientes = new Map();
    let siguiente = 1;

    webview?.addEventListener("message", evento => {
        const respuesta = evento.data;
        const pendiente = pendientes.get(respuesta?.id);
        if (!pendiente) return;

        pendientes.delete(respuesta.id);
        respuesta.ok ? pendiente.resolve(respuesta.resultado) : pendiente.reject(new Error(respuesta.error ?? "Error de la app."));
    });

    const pedir = (tipo, datos) => {
        if (!webview) return Promise.reject(new Error("No es la app de escritorio."));

        const id = siguiente++;
        return new Promise((resolve, reject) => {
            pendientes.set(id, { resolve, reject });
            webview.postMessage({ id, tipo, ...datos });
        });
    };

    const aBase64 = blob => new Promise((resolve, reject) => {
        const lector = new FileReader();
        lector.onload = () => resolve(String(lector.result).split(",", 2)[1] ?? "");
        lector.onerror = () => reject(lector.error);
        lector.readAsDataURL(blob);
    });

    const local = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname);

    return {
        disponible: () => !!webview,

        // Dentro de la app y conectada a un servidor de otro equipo: el Explorador del servidor no le sirve al usuario.
        remoto: () => !!webview && !local,

        copiarTexto: texto => pedir("copiarTexto", { texto }),

        // html: para correo (solo el <img>); sin html: el PNG (WhatsApp).
        copiarImagen: (base64, html) => pedir("copiarImagen", { base64, html }),

        // Baja el archivo con la sesión de la página, lo guarda en la carpeta local de la app (misma estructura que
        // en Storage) y lo selecciona en el Explorador de esta PC.
        localizar: async (url, rutaRelativa) => {
            const respuesta = await fetch(url, { credentials: "same-origin" });
            if (!respuesta.ok) throw new Error(await respuesta.text() || `Error ${respuesta.status}`);

            const base64 = await aBase64(await respuesta.blob());
            return pedir("guardarYMostrar", { ruta: rutaRelativa, base64 });
        },

        notificar: (titulo, mensaje) => pedir("notificar", { titulo, mensaje }),

        // Para "Acerca de": { version, modo } de la app; {} en una app anterior a 0.1.4; null en el navegador.
        informacion: () => webview ? pedir("informacion", {}).catch(() => ({})) : Promise.resolve(null)
    };
})();
