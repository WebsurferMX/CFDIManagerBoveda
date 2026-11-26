// Capturas del manual (CFDIManager.Web\wwwroot\ayuda\img): da de alta la empresa de demostración, espera a que se
// indexen sus XML y fotografía cada pantalla en tema claro. Lo llama Capturas-Manual.ps1.
// Uso: node capturas.mjs <carpeta de imágenes> <carpeta XML de la empresa (Storage\XML\EKU9003173C9)> <carpeta demo>
import { cpSync, readdirSync } from "node:fs";
import { conectar, entrar } from "./cdp-lib.mjs";

const [img, carpetaEmpresa, demo] = process.argv.slice(2);
const c = await conectar();
await entrar(c, "admin", process.env.CFDI_ESCRITORIO_ADMIN_PASSWORD);

const pasos = [];
const paso = async (nombre, f) => {
  try { await f(); pasos.push(`ok    ${nombre}`); }
  catch (e) { pasos.push(`FALLA ${nombre}: ${e.message}`); }
};
const tablaFila = expr => c.evalua(`(() => { const f = [...document.querySelectorAll('tbody tr')].find(r => ${expr}); f?.querySelector('td:nth-child(2)').click(); return !!f; })()`);

// ---------- empresa de demostración y sus XML ----------
await paso("alta de la empresa", async () => {
  await c.ir("configuracion/empresas", 3000);
  if (await c.evalua("document.body.innerText.includes('EKU9003173C9')")) return;
  await c.clic("button", "Nueva empresa"); await c.espera(1500);
  await c.escribe("input[placeholder='AAA010101AAA']", "EKU9003173C9");
  await c.escribe("input[placeholder='Razón social completa']", "ESCUELA KEMPER URGATE");
  await c.escribe("input[placeholder='Nombre para mostrar en el selector']", "Escuela Kemper (demo)");
  await c.espera(800);
  if (!await c.clic("button", "Registrar empresa")) throw new Error("sin botón Registrar empresa");
  await c.espera(3000);
});

await paso("copiar XML e indexar", async () => {
  cpSync(demo, carpetaEmpresa, { recursive: true });
  const total = readdirSync(demo, { recursive: true }).filter(n => n.endsWith(".xml")).length;
  await c.evalua("location.reload(); true"); await c.espera(5000);
  await c.clic("button", "Actualizar índice");
  for (let i = 0; i < 45; i++) {
    await c.espera(2000);
    const t = await c.evalua("[...document.querySelectorAll('*')].find(e => e.children.length === 0 && /XML indexados/.test(e.textContent))?.textContent ?? ''");
    if (t.includes(`${total} XML`)) return;
  }
  throw new Error(`no se indexaron los ${total} XML`);
});

const temaInicial = await c.evalua("document.documentElement.getAttribute('data-theme')");
if (temaInicial === "dark") { await c.clic("button[title*='modo claro']"); await c.espera(800); }

// ---------- capturas ----------
await paso("resumen.png", async () => {
  await c.ir("", 2000); await c.clic("button", "Consultar"); await c.espera(3000);
  await c.foto(`${img}/resumen.png`);
});
await paso("proyeccion-iva.png", async () => {
  await c.clic("a, button", "Proyección de IVA"); await c.espera(2000);
  await c.foto(`${img}/proyeccion-iva.png`);
});
await paso("cfdi-ingresos.png", async () => {
  await c.ir("cfdi/emitidos", 3000); await c.clic("button", "Consultar"); await c.espera(2500);
  await c.foto(`${img}/cfdi-ingresos.png`);
});
await paso("cfdi-detalle.png", async () => {
  await tablaFila("/^A \\d/.test(r.textContent.trim())"); await c.espera(2000);
  await c.evalua("(() => { const k = [...document.querySelectorAll('.card')].filter(x => x.textContent.includes('Ruta XML')).pop(); k.id = 'captura'; k.scrollIntoView({ block: 'center' }); })()");
  await c.espera(800);
  await c.foto(`${img}/cfdi-detalle.png`, "#captura");
});
await paso("administracion-xml.png", async () => {
  await c.ir("administracion-xml", 5000);
  await c.evalua("scrollTo(0, 0); [...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes('Factura'))?.querySelector('td:nth-child(3)').click(); true");
  await c.espera(3500);
  await c.foto(`${img}/administracion-xml.png`);
});
await paso("imagen-resumen.png", async () => {
  await c.clic(".herramientas button, .herramientas .mud-button-root", "Imagen del resumen"); await c.espera(3500);
  await c.evalua("[...document.querySelectorAll('.mud-dialog')].pop().id = 'captura-dialogo'; true");
  await c.foto(`${img}/imagen-resumen.png`, "#captura-dialogo");
  await c.evalua("document.querySelector('#captura-dialogo button[aria-label], #captura-dialogo .mud-dialog-title button')?.click(); true");
  await c.espera(800);
});
await paso("visor-nominas.png", async () => {
  await c.ir("administracion-xml/nominas", 6000);
  await c.foto(`${img}/visor-nominas.png`);
});
await paso("visor-nominas-trabajador.png", async () => {
  await c.clic("a, button", "Trabajadores"); await c.espera(1500);
  await c.evalua("scrollTo(0, 0); document.querySelector('tbody tr').click(); true"); await c.espera(2500);
  await c.evalua("document.querySelector('.vn-modal .modal-content').id = 'captura-ficha'; true");
  await c.foto(`${img}/visor-nominas-trabajador.png`, "#captura-ficha");
  await c.evalua("document.querySelector('.vn-modal .btn-close')?.click(); true"); await c.espera(800);
});
await paso("importaciones.png", async () => { await c.ir("importaciones", 3000); await c.foto(`${img}/importaciones.png`); });
await paso("exportaciones.png", async () => { await c.ir("exportaciones", 3000); await c.foto(`${img}/exportaciones.png`); });
await paso("efos.png", async () => { await c.ir("efos", 3000); await c.foto(`${img}/efos.png`); });

if (temaInicial === "dark") { await c.clic("button[title*='modo oscuro']"); await c.espera(500); }
c.cerrar();
console.log(pasos.join("\n"));
if (pasos.some(p => p.startsWith("FALLA"))) process.exitCode = 1;
