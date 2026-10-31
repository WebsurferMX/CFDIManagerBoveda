// Genera reglas de modo oscuro para hojas .razor.css a partir de sus propios colores.
// Uso: node modo_oscuro.js <archivo.css|archivo.razor> ...
// Agrega (o reemplaza) una sección entre marcadores al final de cada hoja.
// En un .razor procesa el bloque <style> global.
const fs = require("fs");

const INICIO = "/* ===== MODO OSCURO (generado) ===== */";
const FIN = "/* ===== FIN MODO OSCURO ===== */";
const PREFIJO = 'html[data-theme="dark"] ';
const PROPS = /^(color|background|background-color|background-image|border|border-(top|right|bottom|left)|border(-(top|right|bottom|left))?-color|box-shadow|outline|outline-color|fill|stroke|caret-color|text-decoration-color|accent-color)$/;
const FONDOS_PAGINA = new Set(["#f4f6f9", "#f4f7fb"]);
const COLOR = /#[0-9a-fA-F]{3,8}\b|rgba?\([^)]*\)|\b(?:white|black)\b/gi;

function parsear(c) {
    c = c.toLowerCase();
    if (c === "white") return [255, 255, 255, 1];
    if (c === "black") return [0, 0, 0, 1];
    if (c.startsWith("#")) {
        let h = c.slice(1);
        if (h.length === 3 || h.length === 4) h = [...h].map(x => x + x).join("");
        const v = [0, 2, 4].map(i => parseInt(h.substr(i, 2), 16));
        return [...v, h.length === 8 ? parseInt(h.substr(6, 2), 16) / 255 : 1];
    }
    const n = c.match(/[\d.]+%?/g) || [];
    if (n.length < 3) return null;
    const v = n.slice(0, 3).map(x => parseFloat(x));
    let a = 1;
    if (n.length > 3) a = parseFloat(n[3]) / (n[3].endsWith("%") ? 100 : 1);
    return [...v, a];
}

const hex = (r, g, b) => "#" + [r, g, b].map(x => Math.round(x).toString(16).padStart(2, "0")).join("");
const formato = (r, g, b, a) => a >= 0.999 ? hex(r, g, b) : `rgb(${Math.round(r)} ${Math.round(g)} ${Math.round(b)} / ${Math.round(a * 100)}%)`;

function aHsl(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
    if (max === min) return [0, 0, l];
    const d = max - min, s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    let h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [h * 60, s, l];
}

function aRgb(h, s, l) {
    const k = n => (n + h / 30) % 12, a = s * Math.min(l, 1 - l);
    const f = n => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
    return [f(0) * 255, f(8) * 255, f(4) * 255];
}

function oscurecer(texto, tipo) {
    const p = parsear(texto);
    if (!p) return texto;
    const [r, g, b, a] = p;
    if (tipo === "fondo" && a >= 0.999 && FONDOS_PAGINA.has(hex(r, g, b))) return "#09111f";
    const [h, s, l] = aHsl(r, g, b);
    const neutro = s < 0.15 || (l > 0.9 && s < 0.35 && Math.abs(h - 215) < 20);
    let h2, s2, l2;
    if (tipo === "texto") {
        if (l >= 0.6) return texto;
        l2 = Math.max(0.72, 1 - l * 0.55);
        s2 = neutro ? 0.25 : Math.min(s, 0.85);
        h2 = neutro ? 215 : h;
    } else if (tipo === "sombra") {
        if (l <= 0.3) return formato(0, 0, 0, Math.min(0.6, Math.max(a * 2.5, 0.25)));
        if (l <= 0.6) return texto;
        l2 = 0.12 + (1 - l) * 0.45;
        s2 = neutro ? 0.42 : Math.min(s, 0.5);
        h2 = neutro ? 217 : h;
    } else if (tipo === "fondo") {
        if (l <= 0.3 && a <= 0.15) return formato(148, 163, 184, Math.min(1, a * 1.6));
        if (l <= 0.6) return texto;
        l2 = 0.12 + (1 - l) * 0.45;
        s2 = neutro ? 0.42 : Math.min(s, 0.5);
        h2 = neutro ? 217 : h;
    } else {
        if (l <= 0.6) return texto;
        l2 = 0.2 + (1 - l) * 0.5;
        s2 = neutro ? 0.33 : Math.min(s, 0.45);
        h2 = neutro ? 215 : h;
    }
    return formato(...aRgb(h2, s2, l2), a);
}

function tipoDe(prop) {
    if (["color", "fill", "stroke", "caret-color", "text-decoration-color", "accent-color"].includes(prop)) return "texto";
    if (prop === "box-shadow") return "sombra";
    if (prop.startsWith("background")) return "fondo";
    return "borde";
}

function transformarValor(prop, valor) {
    const tipo = tipoDe(prop);
    // No tocar url(...) (SVG embebidos)
    return valor.split(/(url\([^)]*\))/).map(pa =>
        pa.startsWith("url(") ? pa : pa.replace(COLOR, m => oscurecer(m, tipo))).join("");
}

function bloques(css, i) {
    const res = [];
    while (i < css.length) {
        const j = css.indexOf("{", i), k = css.indexOf("}", i);
        if (j === -1 || (k !== -1 && k < j)) return [res, k === -1 ? css.length : k + 1];
        const sel = css.slice(i, j).trim();
        if (sel.startsWith("@")) {
            const [hijos, sig] = bloques(css, j + 1);
            res.push([sel, hijos]);
            i = sig;
        } else {
            const fin = css.indexOf("}", j);
            res.push([sel, css.slice(j + 1, fin)]);
            i = fin + 1;
        }
    }
    return [res, css.length];
}

function prefijar(sel) {
    return sel.split(",").map(s => s.trim()).filter(s => s && !s.startsWith("html")).map(s => PREFIJO + s);
}

function generar(items, sangria) {
    const out = [];
    for (const [sel, cuerpo] of items) {
        if (Array.isArray(cuerpo)) {
            if (!/^@(media|supports)/.test(sel)) continue;
            const hijos = generar(cuerpo, sangria + "    ");
            if (hijos.length) out.push(`${sangria}${sel} {\n${hijos.join("\n")}\n${sangria}}\n`);
            continue;
        }
        const decls = [];
        for (const d of cuerpo.split(";")) {
            const i = d.indexOf(":");
            if (i === -1) continue;
            const prop = d.slice(0, i).trim().toLowerCase(), valor = d.slice(i + 1).trim().replace(/\s+/g, " ");
            if (!PROPS.test(prop)) continue;
            const nuevo = transformarValor(prop, valor);
            if (nuevo !== valor) decls.push(`${sangria}    ${prop}: ${nuevo};`);
        }
        const sels = prefijar(sel);
        if (decls.length && sels.length)
            out.push(`${sels.map(s => sangria + s).join(",\n")} {\n${decls.join("\n")}\n${sangria}}\n`);
    }
    return out;
}

function procesarCss(css) {
    const base = css.split(INICIO)[0].trimEnd();
    const [items] = bloques(base.replace(/\/\*[\s\S]*?\*\//g, ""), 0);
    const reglas = generar(items, "");
    if (!reglas.length) return base + "\n";
    return `${base}\n\n${INICIO}\n\n${reglas.join("\n")}${FIN}\n`;
}

for (const ruta of process.argv.slice(2)) {
    const bytes = fs.readFileSync(ruta);
    const bom = bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf;
    let texto = bytes.toString("utf8").replace(/^﻿/, "");
    const crlf = texto.includes("\r\n");
    texto = texto.replace(/\r\n/g, "\n");
    let nuevo;
    if (ruta.endsWith(".razor")) {
        const m = /(<style>)([\s\S]*?)(<\/style>)/.exec(texto);
        const ini = m.index + m[1].length;
        const css = procesarCss(m[2]).trimEnd();
        const corte = css.indexOf(INICIO);
        const generado = corte === -1 ? "" : css.slice(corte).replace(/^(?!$)/gm, "    ");
        nuevo = texto.slice(0, ini) + (corte === -1 ? css : css.slice(0, corte) + generado) + "\n" + texto.slice(ini + m[2].length);
    } else {
        nuevo = procesarCss(texto);
    }
    if (crlf) nuevo = nuevo.replace(/\n/g, "\r\n");
    fs.writeFileSync(ruta, (bom ? "﻿" : "") + nuevo);
    console.log(ruta, nuevo.split(PREFIJO).length - 1, "selectores");
}
