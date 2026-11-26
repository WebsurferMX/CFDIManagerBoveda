// Manejo de la app de escritorio por CDP (WebView2 con --remote-debugging-port), para las capturas del manual.
import { writeFileSync } from "node:fs";

export async function conectar(puerto = 9333) {
  const lista = await (await fetch(`http://127.0.0.1:${puerto}/json`)).json();
  const ws = new WebSocket(lista.find(t => t.type === "page").webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
  let n = 0; const pend = new Map();
  ws.onmessage = m => { const d = JSON.parse(m.data); pend.get(d.id)?.(d); };
  const cmd = (method, params = {}) => new Promise(r => { const id = ++n; pend.set(id, r); ws.send(JSON.stringify({ id, method, params })); });

  const evalua = async expr => {
    const d = await cmd("Runtime.evaluate", { expression: expr, awaitPromise: true, returnByValue: true });
    if (d.result?.exceptionDetails) throw new Error(d.result.exceptionDetails.exception?.description ?? d.result.exceptionDetails.text);
    return d.result?.result?.value;
  };
  const espera = ms => new Promise(r => setTimeout(r, ms));

  // La carpeta de datos de prueba se muestra como la de una instalación normal (C:\CFDIManagerDatos).
  const datos = process.env.CAPTURAS_DATOS ?? "";
  const ocultarRuta = () => datos ? evalua(`(() => {
      const buscar = ${JSON.stringify(datos)};
      const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); let n;
      while ((n = w.nextNode())) if (n.nodeValue.includes(buscar)) n.nodeValue = n.nodeValue.split(buscar).join("C:\\\\CFDIManagerDatos");
    })()`) : null;

  // Foto de la vista o de un elemento (selector CSS). Las coordenadas del recorte son del documento: se suma el
  // desplazamiento. captureBeyondViewport: false para que los diálogos (position: fixed) no se muevan.
  const foto = async (archivo, selector, margen = 8) => {
    await ocultarRuta();
    const params = { format: "png", captureBeyondViewport: false };
    if (selector) {
      const r = await evalua(`(() => { const e = document.querySelector(${JSON.stringify(selector)}); if (!e) return null;
        const b = e.getBoundingClientRect(); return { x: b.x + scrollX, y: b.y + scrollY, w: b.width, h: b.height }; })()`);
      if (!r) throw new Error("No existe " + selector);
      params.clip = { x: Math.max(0, r.x - margen), y: Math.max(0, r.y - margen), width: r.w + margen * 2, height: r.h + margen * 2, scale: 1 };
    }
    const d = await cmd("Page.captureScreenshot", params);
    writeFileSync(archivo, Buffer.from(d.result.data, "base64"));
  };

  // Clic en el primer elemento (selector) cuyo texto contenga "texto".
  const clic = (selector, texto = "") => evalua(`(() => {
    const e = [...document.querySelectorAll(${JSON.stringify(selector)})].find(x => x.textContent.includes(${JSON.stringify(texto)}));
    if (!e) return false; e.click(); return true; })()`);

  // Escribe en un input de Blazor (dispara input y change).
  const escribe = (selector, valor) => evalua(`(() => {
    const e = document.querySelector(${JSON.stringify(selector)}); if (!e) return false;
    e.value = ${JSON.stringify(valor)};
    e.dispatchEvent(new Event("input", { bubbles: true })); e.dispatchEvent(new Event("change", { bubbles: true }));
    return true; })()`);

  const ir = async (ruta, ms = 2500) => { await evalua(`Blazor.navigateTo(${JSON.stringify(ruta)}); true`); await espera(ms); };

  return { cmd, evalua, espera, foto, clic, escribe, ir, cerrar: () => ws.close() };
}

export async function entrar(c, usuario, password) {
  if ((await c.evalua("location.pathname")).includes("login")) {
    await c.evalua(`document.getElementById('usuario').value=${JSON.stringify(usuario)};
      document.getElementById('password').value=${JSON.stringify(password)};
      document.getElementById('password').form.requestSubmit(); true`);
    await c.espera(6000);
  }
}
