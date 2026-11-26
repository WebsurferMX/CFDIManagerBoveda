// Genera CFDI 4.0 ficticios para la empresa de demostración del manual (RFC de prueba publicados por el SAT).
// Uso: node generar-demo.mjs <carpeta salida> → <salida>/Emitidos|Recibidos/<año>/<mes>/<UUID>.xml
// Tres meses: los dos anteriores completos y el actual hasta ayer. Mismos importes en cada corrida (semilla fija).
import { mkdirSync, writeFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { join } from "node:path";

const salida = process.argv[2];
const EMPRESA = { rfc: "EKU9003173C9", nombre: "ESCUELA KEMPER URGATE", regimen: "601", cp: "42501" };

const CLIENTES = [
  { rfc: "XIA190128J61", nombre: "XENON INDUSTRIAL ARTICLES", regimen: "601", cp: "76343" },
  { rfc: "IIA040805DZ4", nombre: "INDUSTRIA ILUMINADORA DE ALMACENES", regimen: "601", cp: "62661" },
  { rfc: "URE180429TM6", nombre: "UNIVERSIDAD ROBOTICA ESPAÑOLA", regimen: "601", cp: "86991" },
  { rfc: "ICV060329BY0", nombre: "INMOBILIARIA CVA", regimen: "601", cp: "33826" },
  { rfc: "IVD920810GU2", nombre: "INNOVACION VALOR Y DESARROLLO", regimen: "601", cp: "63901" },
];
const PROVEEDORES = [
  { rfc: "CACX7605101P8", nombre: "XOCHILT CASAS CHAVEZ", regimen: "612", cp: "36257" },
  { rfc: "FUNK671228PH6", nombre: "KARLA FUENTE NOLASCO", regimen: "612", cp: "01160" },
  { rfc: "H&E951128469", nombre: "HERRERIA & ELECTRICOS", regimen: "601", cp: "06002" },
  { rfc: "ZUÑ920208KL4", nombre: "ZAPATERIA URTADO ÑERI", regimen: "601", cp: "34541" },
];
const EMPLEADOS = [
  { rfc: "JUFA7608212V6", nombre: "ADRIANA JUAREZ FERNANDEZ", curp: "JUFA760821MDFRRD09", num: "001", depto: "Administración", puesto: "Contadora", sd: 980 },
  { rfc: "KAHO641101B39", nombre: "OSCAR KALA HAAK", curp: "KAHO641101HDFLKS06", num: "002", depto: "Docencia", puesto: "Profesor", sd: 850 },
  { rfc: "WATM640917J45", nombre: "MARIA WATEMBER TORRES", curp: "WATM640917MDFTRR02", num: "003", depto: "Docencia", puesto: "Profesora", sd: 850 },
  { rfc: "MASO451221PM4", nombre: "MARIA OLIVIA MARTINEZ SAGAZ", curp: "MASO451221MDFRGL03", num: "004", depto: "Servicios", puesto: "Intendencia", sd: 420 },
];
const CONCEPTOS_VENTA = [
  ["86121700", "Colegiatura mensual", 6800], ["86101700", "Curso de capacitación", 14500],
  ["86132000", "Taller de robótica", 9200], ["55101500", "Material didáctico", 2350],
];
const CONCEPTOS_COMPRA = [
  ["44121600", "Papelería y consumibles", 1850], ["72101500", "Mantenimiento de instalaciones", 12400],
  ["43211500", "Equipo de cómputo", 18900], ["81112100", "Servicio de internet", 1299],
];

let semilla = 7;
const azar = () => ((semilla = (semilla * 16807) % 2147483647) / 2147483647);
const elige = lista => lista[Math.floor(azar() * lista.length)];
const m2 = n => n.toFixed(2);
const fecha = (anio, mes, dia, hora = 10) =>
  `${anio}-${String(mes).padStart(2, "0")}-${String(dia).padStart(2, "0")}T${String(hora).padStart(2, "0")}:${String(Math.floor(azar() * 59)).padStart(2, "0")}:00`;
const esc = s => s.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;");

const ENC = `<?xml version="1.0" encoding="UTF-8"?>`;
const NS = `xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"`;
const timbre = (uuid, f) =>
  `<tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" xsi:schemaLocation="http://www.sat.gob.mx/TimbreFiscalDigital http://www.sat.gob.mx/sitio_internet/cfd/TimbreFiscalDigital/TimbreFiscalDigitalv11.xsd" Version="1.1" UUID="${uuid}" FechaTimbrado="${f}" RfcProvCertif="SAT970701NN3" SelloCFD="DEMO" NoCertificadoSAT="30001000000500003456" SelloSAT="DEMO"/>`;

const guardados = [];
function guardar(tipoCarpeta, f, uuid, xml, info) {
  const [anio, mes] = f.split("-");
  const dir = join(salida, tipoCarpeta, anio, mes);
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, `${uuid}.xml`), ENC + "\n" + xml, "utf8");
  guardados.push(info);
}

function comprobante({ tipo, serie, folio, f, emisor, receptor, conceptos, metodo = "PUE", forma = "03", uso = "G03", relacion }) {
  const uuid = randomUUID().toUpperCase();
  let subtotal = 0, iva = 0;
  const xmlConceptos = conceptos.map(([clave, desc, precio, cant]) => {
    const importe = precio * cant; const imp = +(importe * 0.16).toFixed(2);
    subtotal += importe; iva += imp;
    return `<cfdi:Concepto ClaveProdServ="${clave}" Cantidad="${cant}" ClaveUnidad="E48" Unidad="Servicio" Descripcion="${esc(desc)}" ValorUnitario="${m2(precio)}" Importe="${m2(importe)}" ObjetoImp="02"><cfdi:Impuestos><cfdi:Traslados><cfdi:Traslado Base="${m2(importe)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="${m2(imp)}"/></cfdi:Traslados></cfdi:Impuestos></cfdi:Concepto>`;
  }).join("");
  const total = subtotal + iva;
  const rel = relacion ? `<cfdi:CfdiRelacionados TipoRelacion="01"><cfdi:CfdiRelacionado UUID="${relacion}"/></cfdi:CfdiRelacionados>` : "";
  const xml = `<cfdi:Comprobante ${NS} xsi:schemaLocation="http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd" Version="4.0" Serie="${serie}" Folio="${folio}" Fecha="${f}" Sello="DEMO" FormaPago="${metodo === "PPD" ? "99" : forma}" NoCertificado="30001000000500003416" Certificado="DEMO" SubTotal="${m2(subtotal)}" Moneda="MXN" Total="${m2(total)}" TipoDeComprobante="${tipo}" Exportacion="01" MetodoPago="${metodo}" LugarExpedicion="${emisor.cp}">${rel}<cfdi:Emisor Rfc="${esc(emisor.rfc)}" Nombre="${esc(emisor.nombre)}" RegimenFiscal="${emisor.regimen}"/><cfdi:Receptor Rfc="${esc(receptor.rfc)}" Nombre="${esc(receptor.nombre)}" DomicilioFiscalReceptor="${receptor.cp}" RegimenFiscalReceptor="${receptor.regimen}" UsoCFDI="${tipo === "E" ? "G02" : uso}"/><cfdi:Conceptos>${xmlConceptos}</cfdi:Conceptos><cfdi:Impuestos TotalImpuestosTrasladados="${m2(iva)}"><cfdi:Traslados><cfdi:Traslado Base="${m2(subtotal)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="${m2(iva)}"/></cfdi:Traslados></cfdi:Impuestos><cfdi:Complemento>${timbre(uuid, f)}</cfdi:Complemento></cfdi:Comprobante>`;
  return { uuid, xml, total, subtotal, iva };
}

function pago({ folio, f, emisor, receptor, factura }) {
  const uuid = randomUUID().toUpperCase();
  const base = +(factura.total / 1.16).toFixed(2); const iva = +(factura.total - base).toFixed(2);
  const xml = `<cfdi:Comprobante ${NS} xmlns:pago20="http://www.sat.gob.mx/Pagos20" xsi:schemaLocation="http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd http://www.sat.gob.mx/Pagos20 http://www.sat.gob.mx/sitio_internet/cfd/Pagos/Pagos20.xsd" Version="4.0" Serie="P" Folio="${folio}" Fecha="${f}" Sello="DEMO" NoCertificado="30001000000500003416" Certificado="DEMO" SubTotal="0" Moneda="XXX" Total="0" TipoDeComprobante="P" Exportacion="01" LugarExpedicion="${emisor.cp}"><cfdi:Emisor Rfc="${esc(emisor.rfc)}" Nombre="${esc(emisor.nombre)}" RegimenFiscal="${emisor.regimen}"/><cfdi:Receptor Rfc="${esc(receptor.rfc)}" Nombre="${esc(receptor.nombre)}" DomicilioFiscalReceptor="${receptor.cp}" RegimenFiscalReceptor="${receptor.regimen}" UsoCFDI="CP01"/><cfdi:Conceptos><cfdi:Concepto ClaveProdServ="84111506" Cantidad="1" ClaveUnidad="ACT" Descripcion="Pago" ValorUnitario="0" Importe="0" ObjetoImp="01"/></cfdi:Conceptos><cfdi:Complemento><pago20:Pagos Version="2.0"><pago20:Totales TotalTrasladosBaseIVA16="${m2(base)}" TotalTrasladosImpuestoIVA16="${m2(iva)}" MontoTotalPagos="${m2(factura.total)}"/><pago20:Pago FechaPago="${f}" FormaDePagoP="03" MonedaP="MXN" TipoCambioP="1" Monto="${m2(factura.total)}"><pago20:DoctoRelacionado IdDocumento="${factura.uuid}" Serie="A" Folio="${factura.folio}" MonedaDR="MXN" EquivalenciaDR="1" NumParcialidad="1" ImpSaldoAnt="${m2(factura.total)}" ImpPagado="${m2(factura.total)}" ImpSaldoInsoluto="0.00" ObjetoImpDR="02"><pago20:ImpuestosDR><pago20:TrasladosDR><pago20:TrasladoDR BaseDR="${m2(base)}" ImpuestoDR="002" TipoFactorDR="Tasa" TasaOCuotaDR="0.160000" ImporteDR="${m2(iva)}"/></pago20:TrasladosDR></pago20:ImpuestosDR></pago20:DoctoRelacionado><pago20:ImpuestosP><pago20:TrasladosP><pago20:TrasladoP BaseP="${m2(base)}" ImpuestoP="002" TipoFactorP="Tasa" TasaOCuotaP="0.160000" ImporteP="${m2(iva)}"/></pago20:TrasladosP></pago20:ImpuestosP></pago20:Pago></pago20:Pagos>${timbre(uuid, f)}</cfdi:Complemento></cfdi:Comprobante>`;
  return { uuid, xml };
}

function nomina({ folio, f, empleado, inicio, fin, pagoF }) {
  const uuid = randomUUID().toUpperCase();
  const dias = 15; const sueldo = +(empleado.sd * dias).toFixed(2);
  const isr = +(sueldo * 0.09).toFixed(2); const imss = +(sueldo * 0.025).toFixed(2);
  const deducciones = +(isr + imss).toFixed(2); const neto = +(sueldo - deducciones).toFixed(2);
  const xml = `<cfdi:Comprobante ${NS} xmlns:nomina12="http://www.sat.gob.mx/nomina12" xsi:schemaLocation="http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd http://www.sat.gob.mx/nomina12 http://www.sat.gob.mx/sitio_internet/cfd/nomina/nomina12.xsd" Version="4.0" Serie="N" Folio="${folio}" Fecha="${f}" Sello="DEMO" NoCertificado="30001000000500003416" Certificado="DEMO" SubTotal="${m2(sueldo)}" Descuento="${m2(deducciones)}" Moneda="MXN" Total="${m2(neto)}" TipoDeComprobante="N" Exportacion="01" MetodoPago="PUE" LugarExpedicion="${EMPRESA.cp}"><cfdi:Emisor Rfc="${EMPRESA.rfc}" Nombre="${EMPRESA.nombre}" RegimenFiscal="${EMPRESA.regimen}"/><cfdi:Receptor Rfc="${empleado.rfc}" Nombre="${empleado.nombre}" DomicilioFiscalReceptor="42501" RegimenFiscalReceptor="605" UsoCFDI="CN01"/><cfdi:Conceptos><cfdi:Concepto ClaveProdServ="84111505" Cantidad="1" ClaveUnidad="ACT" Descripcion="Pago de nómina" ValorUnitario="${m2(sueldo)}" Importe="${m2(sueldo)}" Descuento="${m2(deducciones)}" ObjetoImp="01"/></cfdi:Conceptos><cfdi:Complemento><nomina12:Nomina Version="1.2" TipoNomina="O" FechaPago="${pagoF}" FechaInicialPago="${inicio}" FechaFinalPago="${fin}" NumDiasPagados="${dias}" TotalPercepciones="${m2(sueldo)}" TotalDeducciones="${m2(deducciones)}"><nomina12:Emisor RegistroPatronal="B5510768108"/><nomina12:Receptor Curp="${empleado.curp}" NumSeguridadSocial="04078873454" FechaInicioRelLaboral="2019-01-07" Antigüedad="P360W" TipoContrato="01" TipoJornada="01" TipoRegimen="02" NumEmpleado="${empleado.num}" Departamento="${empleado.depto}" Puesto="${empleado.puesto}" RiesgoPuesto="1" PeriodicidadPago="04" SalarioBaseCotApor="${m2(empleado.sd * 1.0452)}" SalarioDiarioIntegrado="${m2(empleado.sd * 1.0452)}" ClaveEntFed="HID"/><nomina12:Percepciones TotalSueldos="${m2(sueldo)}" TotalGravado="${m2(sueldo)}" TotalExento="0.00"><nomina12:Percepcion TipoPercepcion="001" Clave="P001" Concepto="Sueldo" ImporteGravado="${m2(sueldo)}" ImporteExento="0.00"/></nomina12:Percepciones><nomina12:Deducciones TotalOtrasDeducciones="${m2(imss)}" TotalImpuestosRetenidos="${m2(isr)}"><nomina12:Deduccion TipoDeduccion="002" Clave="D002" Concepto="ISR" Importe="${m2(isr)}"/><nomina12:Deduccion TipoDeduccion="001" Clave="D001" Concepto="IMSS" Importe="${m2(imss)}"/></nomina12:Deducciones></nomina12:Nomina>${timbre(uuid, f)}</cfdi:Complemento></cfdi:Comprobante>`;
  return { uuid, xml };
}

let folioA = 1001, folioNC = 51, folioP = 201, folioN = 3001;
const hoy = new Date();
const diasDelMes = (a, m) => new Date(a, m, 0).getDate();
const meses = [-2, -1, 0].map(d => {
  const f = new Date(hoy.getFullYear(), hoy.getMonth() + d, 1);
  const anio = f.getFullYear(), mes = f.getMonth() + 1;
  return [anio, mes, d === 0 ? Math.max(2, hoy.getDate() - 1) : diasDelMes(anio, mes), d === 0];
});
for (const [anio, mes, ultimo, actual] of meses) {
  const ppd = [];
  // Ventas: 14 facturas al mes.
  for (let i = 0; i < 14; i++) {
    const dia = 1 + Math.floor(azar() * (ultimo - 1)); const f = fecha(anio, mes, dia);
    const [clave, desc, precio] = elige(CONCEPTOS_VENTA); const cant = 1 + Math.floor(azar() * 4);
    const metodo = i % 4 === 0 ? "PPD" : "PUE"; const cliente = elige(CLIENTES); const folio = folioA++;
    const c = comprobante({ tipo: "I", serie: "A", folio, f, emisor: EMPRESA, receptor: cliente, conceptos: [[clave, desc, precio, cant]], metodo });
    guardar("Emitidos", f, c.uuid, c.xml, "I");
    if (metodo === "PPD") ppd.push({ ...c, folio, cliente, dia });
    if (i === 3) { // Nota de crédito sobre esta factura.
      const fn = fecha(anio, mes, Math.min(ultimo, dia + 2));
      const n = comprobante({ tipo: "E", serie: "NC", folio: folioNC++, f: fn, emisor: EMPRESA, receptor: cliente, conceptos: [["84111506", "Descuento por pronto pago", +(precio * cant * 0.05).toFixed(2), 1]], relacion: c.uuid });
      guardar("Emitidos", fn, n.uuid, n.xml, "E");
    }
  }
  // Pagos de las facturas PPD (salvo la última, que queda pendiente).
  for (const fac of ppd.slice(0, -1)) {
    const fp = fecha(anio, mes, Math.min(ultimo, fac.dia + 5), 12);
    const p = pago({ folio: folioP++, f: fp, emisor: EMPRESA, receptor: fac.cliente, factura: fac });
    guardar("Emitidos", fp, p.uuid, p.xml, "P");
  }
  // Nómina quincenal.
  for (const [ini, fin, diaPago] of [[1, 15, 15], [16, diasDelMes(anio, mes), Math.min(ultimo, 28)]]) {
    if (actual && ini === 16) continue; // La segunda quincena del mes en curso aún no se paga.
    for (const e of EMPLEADOS) {
      const f = fecha(anio, mes, diaPago, 9);
      const d = s => `${anio}-${String(mes).padStart(2, "0")}-${String(s).padStart(2, "0")}`;
      const n = nomina({ folio: folioN++, f, empleado: e, inicio: d(ini), fin: d(fin), pagoF: d(diaPago) });
      guardar("Emitidos", f, n.uuid, n.xml, "N");
    }
  }
  // Compras: 8 facturas recibidas al mes.
  for (let i = 0; i < 8; i++) {
    const f = fecha(anio, mes, 1 + Math.floor(azar() * (ultimo - 1)));
    const [clave, desc, precio] = elige(CONCEPTOS_COMPRA); const prov = elige(PROVEEDORES);
    const c = comprobante({ tipo: "I", serie: "F", folio: 500 + Math.floor(azar() * 9000), f, emisor: prov, receptor: EMPRESA, conceptos: [[clave, desc, precio, 1 + Math.floor(azar() * 2)]] });
    guardar("Recibidos", f, c.uuid, c.xml, "I");
  }
}
const cuenta = guardados.reduce((a, t) => (a[t] = (a[t] ?? 0) + 1, a), {});
console.log(`CFDI generados: ${guardados.length}`, cuenta);
