// Cierre de sesión por inactividad (Sesion:MinutosInactividad en appsettings.json).
// - La última actividad se comparte entre pestañas (localStorage): trabajar en una mantiene vivas las demás.
// - Un minuto antes avisa con un botón para seguir; cualquier movimiento también cuenta como actividad.
// - Mientras hay actividad renueva la cookie con /cuenta/actividad (Blazor Server no hace peticiones HTTP que la renueven).
(function () {
    const script = document.currentScript;
    const limite = Number(script?.dataset.minutos || 0) * 60000;

    if (!limite) {
        return;
    }

    const AVISO = 60000;
    const RENOVAR_CADA = 5 * 60000;
    const CLAVE_ACTIVIDAD = "cfdi-ultima-actividad";
    const CLAVE_CERRADA = "cfdi-sesion-cerrada";
    const inicio = Date.now();

    let actividadLocal = inicio;
    let ultimaRenovacion = inicio;
    let saliendo = false;
    let aviso = null;

    function leer(clave) {
        try { return Number(localStorage.getItem(clave)) || 0; } catch { return 0; }
    }

    function escribir(clave, valor) {
        try { localStorage.setItem(clave, String(valor)); } catch { /* sin almacenamiento: cuenta solo esta pestaña */ }
    }

    function ultimaActividad() {
        return Math.max(actividadLocal, leer(CLAVE_ACTIVIDAD));
    }

    function marcar() {
        actividadLocal = Date.now();
        escribir(CLAVE_ACTIVIDAD, actividadLocal);
        renovar(false);
    }

    function renovar(forzar) {
        if (!forzar && Date.now() - ultimaRenovacion < RENOVAR_CADA) {
            return;
        }

        ultimaRenovacion = Date.now();

        fetch("cuenta/actividad", { credentials: "same-origin", cache: "no-store" })
            .then(r => {
                // Sin sesión el servidor redirige al login: la sesión ya venció.
                if (!r.ok || r.redirected) {
                    irAlLogin();
                }
            })
            .catch(() => { /* sin red: se reintenta con la siguiente actividad */ });
    }

    function irAlLogin() {
        if (saliendo) {
            return;
        }

        saliendo = true;
        location.href = "cuenta/login?inactividad=1";
    }

    function salir() {
        if (saliendo) {
            return;
        }

        // Otra pestaña ya cerró la sesión: su token ya no sirve para volver a cerrarla.
        if (leer(CLAVE_CERRADA) > inicio) {
            irAlLogin();
            return;
        }

        saliendo = true;
        escribir(CLAVE_CERRADA, Date.now());

        const formulario = document.getElementById("cfdi-salir-inactividad");

        if (formulario) {
            formulario.submit();
        } else {
            location.href = "cuenta/login?inactividad=1";
        }
    }

    function mostrarAviso(segundos) {
        if (!aviso) {
            aviso = document.createElement("div");
            aviso.className = "alert alert-warning shadow-lg d-flex align-items-center gap-3 mb-0 cfdi-aviso-sesion";
            aviso.setAttribute("role", "alert");
            aviso.innerHTML =
                '<i class="bi bi-hourglass-split fs-4"></i>' +
                '<div class="flex-grow-1"><strong>Tu sesión está por cerrarse</strong>' +
                '<div class="small">Por inactividad se cerrará en <span class="cfdi-segundos"></span> s.</div></div>' +
                '<button type="button" class="btn btn-sm btn-warning">Seguir trabajando</button>';
            aviso.querySelector("button").addEventListener("click", () => {
                marcar();
                renovar(true);
                ocultarAviso();
            });
            document.body.appendChild(aviso);
        }

        aviso.querySelector(".cfdi-segundos").textContent = segundos;
    }

    function ocultarAviso() {
        aviso?.remove();
        aviso = null;
    }

    function revisar() {
        if (saliendo) {
            return;
        }

        if (leer(CLAVE_CERRADA) > inicio) {
            irAlLogin();
            return;
        }

        const resto = limite - (Date.now() - ultimaActividad());

        if (resto <= 0) {
            salir();
        } else if (resto <= AVISO) {
            mostrarAviso(Math.ceil(resto / 1000));
        } else {
            ocultarAviso();
        }
    }

    // Cada evento cuenta, pero se escribe como máximo cada 15 s para no saturar localStorage.
    let ultimaMarca = 0;

    function alActuar() {
        const ahora = Date.now();

        if (ahora - ultimaMarca > 15000 || aviso) {
            ultimaMarca = ahora;
            marcar();
        }
    }

    ["mousemove", "mousedown", "keydown", "wheel", "touchstart", "scroll"].forEach(evento =>
        window.addEventListener(evento, alActuar, { passive: true, capture: true }));

    // Abrir o recargar la página también es actividad (y evita que una marca vieja cierre la sesión recién iniciada).
    marcar();
    setInterval(revisar, 5000);
})();
