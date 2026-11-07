using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CFDIParser.Models;
using Cat = CFDIManager.Web.Services.AdministracionXml.ReporteGeneral.Catalogos;

namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Llena una plantilla de impresión de SINUBE (DbReporte.plantillaHTML) con los datos de un CFDI: variables #nombre#,
/// el detalle de conceptos entre los renglones id="iniciodetalle"/"findetalle" (con sus impuestos entre
/// "iniciodetalleimpuestosconcepto"/"findetalleimpuestosconcepto") y, en los pagos, un bloque por pago entre
/// "multi_documento_ini"/"multi_documento_fin" con sus documentos entre "iniciodetallecomplementopagos"/"findetallecomplementopagos".
/// Las variables que no se conocen quedan vacías.
/// </summary>
public static partial class PlantillaSinube
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private static readonly Dictionary<string, string> Tipos = new()
    {
        ["I"] = "Ingreso", ["E"] = "Egreso", ["T"] = "Traslado", ["P"] = "Pago", ["N"] = "Nómina"
    };

    private static readonly Dictionary<string, string> NombresImpuesto = new() { ["001"] = "ISR", ["002"] = "IVA", ["003"] = "IEPS" };

    [GeneratedRegex("#([a-z][a-z0-9_]*)#")]
    private static partial Regex Variable();

    // QR del formato: /resources/reportes/qr.png o, como indica el reporteador, src="qr".
    [GeneratedRegex("""src\s*=\s*["'](?:/resources/reportes/qr\.png|qr)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImagenQr();

    /// <summary>Documento HTML completo (para convertir a PDF) con la plantilla llena.</summary>
    public static string Llenar(string plantilla, CfdiDto c, XDocument xml, DatosClientePdf cliente, byte[] qrPng)
    {
        string html = plantilla;

        // Pagos: un bloque por pago con sus documentos relacionados.
        List<XElement> pagos = xml.Descendants().Where(e => e.Name.LocalName == "Pago" && e.Parent?.Name.LocalName == "Pagos").ToList();
        html = Repetir(html, "div", "multi_documento_ini", "multi_documento_fin", c.TipoComprobante == "P" ? pagos : [], (seg, pago) =>
        {
            List<XElement> doctos = pago.Elements().Where(e => e.Name.LocalName == "DoctoRelacionado").ToList();
            seg = Repetir(seg, "tr", "iniciodetallecomplementopagos", "findetallecomplementopagos", doctos,
                (s, d) => Reemplazar(s, VariablesDocto(d)));
            return Reemplazar(seg, VariablesPago(pago));
        });

        // Conceptos con sus impuestos (el nodo del XML para pedimentos y a cuenta de terceros).
        List<XElement> nodos = xml.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Conceptos")?
            .Elements().Where(e => e.Name.LocalName == "Concepto").ToList() ?? [];
        var conceptos = c.Conceptos.Select((k, n) => (Concepto: k, Nodo: nodos.ElementAtOrDefault(n), Renglon: n + 1)).ToList();
        html = Repetir(html, "tr", "iniciodetalle", "findetalle", conceptos, (seg, k) =>
        {
            seg = k.Concepto.Impuestos.Count == 0
                ? Quitar(seg, "tr", "detalleimpuestosconcepto")
                : Repetir(seg, "tr", "iniciodetalleimpuestosconcepto", "findetalleimpuestosconcepto", k.Concepto.Impuestos,
                    (s, i) => Reemplazar(s, VariablesImpuesto(i)));
            return Reemplazar(seg, VariablesConcepto(k.Concepto, k.Nodo, k.Renglon));
        });

        Dictionary<string, string> variables = VariablesComprobante(c, cliente);
        foreach (var (clave, valor) in VariablesXml(c, xml))
            variables[clave] = valor;

        // Direcciones de SINUBE: fiscal y de envío del cliente; matriz de la empresa (fiscal y expedido).
        Direccion(variables, "receptor_", "", cliente.Fiscal);
        Direccion(variables, "receptor_", "_envio", cliente.Envio);
        Direccion(variables, "emisor_", "_fiscal", cliente.Emisor);
        Direccion(variables, "emisor_", "_expedido", cliente.Emisor);
        if (cliente.Fiscal is null)
            variables["receptor_codigopostal"] = c.DomicilioFiscalReceptor;

        // Variantes de ticket y a 150 caracteres: el mismo valor (el corte lo hace el ancho de la celda).
        foreach (string largo in new[] { "cfdi_sellocfd", "cfdi_sellosat", "cfdi_cadenaoriginaltimbre" })
        {
            variables[largo + "_ticket"] = variables[largo];
            variables[largo + "150"] = variables[largo];
        }

        html = Variable().Replace(Reemplazar(html, variables), "");
        html = ImagenQr().Replace(html, $"src=\"data:image/png;base64,{Convert.ToBase64String(qrPng)}\" width=\"100\" height=\"100\"");

        return "<html><head><meta charset=\"utf-8\"><style>" +
               "@page { size: letter; margin: 10mm; } " +
               "body { margin: 0; font-family: arial, tahoma, sans-serif; -webkit-print-color-adjust: exact; print-color-adjust: exact; } " +
               "</style></head><body>" + html + "</body></html>";
    }

    // ---------- Variables ----------

    private static void Direccion(Dictionary<string, string> v, string prefijo, string sufijo, CFDIManager.Web.Services.Sinube.DireccionSinube? d)
    {
        if (d is null)
            return;

        v[$"{prefijo}calle{sufijo}"] = d.Calle;
        v[$"{prefijo}noexterior{sufijo}"] = d.NoExterior;
        v[$"{prefijo}nointerior{sufijo}"] = d.NoInterior;
        v[$"{prefijo}colonia{sufijo}"] = d.Colonia;
        v[$"{prefijo}localidad{sufijo}"] = d.Localidad;
        v[$"{prefijo}referencia{sufijo}"] = d.Referencia;
        v[$"{prefijo}municipio{sufijo}"] = d.Municipio;
        v[$"{prefijo}estado{sufijo}"] = d.Estado;
        v[$"{prefijo}pais{sufijo}"] = d.Pais;
        v[$"{prefijo}codigopostal{sufijo}"] = d.CodigoPostal;
    }

    private static string Dinero(decimal? v) => (v ?? 0).ToString("C2", Mx);

    // SINUBE imprime la hora en 24 h seguida de AM/PM (p. ej. "03/11/2026 13:12:03 PM").
    private static string Fecha(DateTime? f) => f is DateTime d ? d.ToString("dd/MM/yyyy HH:mm:ss tt", CultureInfo.InvariantCulture) : "";

    private static string Tasa6(decimal t) => t.ToString("0.000000", CultureInfo.InvariantCulture);

    private static string TasaPorcentaje(ImpuestoDto i) =>
        i.TipoFactor.Equals("Cuota", StringComparison.OrdinalIgnoreCase)
            ? i.Tasa.ToString("0.######", Mx)
            : (i.Tasa * 100).ToString("0.0#####", CultureInfo.InvariantCulture) + "%";

    private static bool EsRetencion(ImpuestoDto i) => i.Tipo.StartsWith("RET", StringComparison.OrdinalIgnoreCase);

    private static bool EsExento(ImpuestoDto i) => i.TipoFactor.Equals("Exento", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> VariablesComprobante(CfdiDto c, DatosClientePdf cliente)
    {
        string selloCfd = string.IsNullOrEmpty(c.SelloCfd) ? c.Sello : c.SelloCfd;
        List<ImpuestoDto> traslados = c.Impuestos.Where(i => !EsRetencion(i)).ToList();
        ImpuestoDto? iva = traslados.FirstOrDefault(i => i.Impuesto == "002" && !EsExento(i));
        ImpuestoDto? ieps = traslados.FirstOrDefault(i => i.Impuesto == "003" && !EsExento(i));
        string letra = ImporteConLetra.Convertir(c.Total, c.Moneda);

        return new(StringComparer.Ordinal)
        {
            ["comprobante_version"] = c.Version,
            ["comprobante_serie"] = c.Serie,
            ["comprobante_folio"] = c.Folio,
            ["comprobante_fecha"] = Fecha(c.FechaEmision),
            ["comprobante_lugarexpedicion"] = c.LugarExpedicion,
            ["comprobante_nocertificado"] = c.NoCertificado,
            ["comprobante_tipodecomprobante"] = c.TipoComprobante,
            ["comprobante_tipodecomprobante2"] = Tipos.GetValueOrDefault(c.TipoComprobante, ""),
            ["comprobante_exportacion_4"] = c.Exportacion,
            ["comprobante_cancelada"] = "",
            ["comprobante_subtotal"] = Dinero(c.SubTotal),
            ["comprobante_descuento"] = Dinero(c.Descuento),
            ["comprobante_total"] = Dinero(c.Total),
            ["comprobante_totalconletra"] = letra,
            ["comprobante_totalconletraing"] = letra,
            ["comprobante_condicionesdepago"] = c.CondicionesDePago,
            ["comprobante_formadepago"] = c.FormaPago,
            ["comprobante_formadepago_descripcion"] = Cat.Forma.GetValueOrDefault(c.FormaPago.Trim(), ""),
            ["comprobante_metododepago"] = c.MetodoPago,
            ["comprobante_metododepago_descripcion"] = Cat.Metodo.GetValueOrDefault(c.MetodoPago.Trim(), ""),
            ["comprobante_tipocambio"] = c.TipoCambio?.ToString("0.######", CultureInfo.InvariantCulture) ?? "",
            ["comprobante_totalieps"] = Dinero(traslados.Where(i => i.Impuesto == "003").Sum(i => i.Importe)),
            ["iepstrasltasa1"] = Tasa6(ieps?.Tasa ?? 0),
            ["ivatrasladado"] = Dinero(traslados.Where(i => i.Impuesto == "002").Sum(i => i.Importe)),
            ["ivatrasladadoporcentaje"] = Tasa6(iva?.Tasa ?? 0),
            ["ivatrasladadoporcentajetxt"] = $"IVA {((iva?.Tasa ?? 0) * 100).ToString("0.##", CultureInfo.InvariantCulture)}%",

            ["emisor_rfc"] = c.RFCEmisor,
            ["emisor_nombre"] = c.NombreEmisor,
            ["emisor_regimen_3"] = c.RegimenFiscalEmisor,
            ["emisor_regimen_descripcion_3"] = Cat.Regimen.GetValueOrDefault(c.RegimenFiscalEmisor.Trim(), ""),

            ["receptor_rfc"] = c.RFCReceptor,
            ["receptor_nombre"] = c.NombreReceptor,
            ["receptor_domiciliofiscal_4"] = c.DomicilioFiscalReceptor,
            ["receptor_codigopostal"] = c.DomicilioFiscalReceptor,
            ["receptor_regimen_4"] = c.RegimenFiscalReceptor,
            ["receptor_regimen_descripcion_4"] = Cat.Regimen.GetValueOrDefault(c.RegimenFiscalReceptor.Trim(), ""),
            ["receptor_usocfdi_3"] = c.UsoCfdi,
            ["receptor_usocfdi_descripcion_3"] = Cat.Uso.GetValueOrDefault(c.UsoCfdi.Trim(), ""),
            ["receptor_pais"] = c.ResidenciaFiscal,
            ["receptor_numregidtrib_3"] = c.NumRegIdTrib,

            ["datoscliente_nomcomercial"] = cliente.NombreComercial,
            ["datoscliente_codigoaux"] = cliente.CodigoAux,
            ["referencia"] = cliente.Referencia,
            ["datoscliente_refbancaria"] = cliente.RefBancaria,
            ["receptor_correoe"] = cliente.Correo,

            ["tiporelacion_3"] = string.Join(", ", c.Relacionados.Select(r => r.TipoRelacion).Where(t => t != "").Distinct()),
            ["relacioncfdis_3"] = string.Join(", ", c.Relacionados.Select(r => r.UUID)),

            ["cfdi_uuid"] = c.UUID.ToLowerInvariant(),
            ["cfdi_fechatimbrado"] = Fecha(c.FechaTimbrado),
            ["cfdi_nocertificadosat"] = c.NoCertificadoSat,
            ["cfdi_sellocfd"] = selloCfd,
            ["cfdi_sellosat"] = c.SelloSat,
            ["cfdi_cadenaoriginaltimbre"] =
                $"||{c.VersionTimbre}|{c.UUID}|{c.FechaTimbrado:yyyy-MM-ddTHH:mm:ss}|{c.RfcProvCertif}|{selloCfd}|{c.NoCertificadoSat}||"
        };
    }

    private static readonly Dictionary<string, string> ObjetosImpuesto = new()
    {
        ["01"] = "No objeto de impuesto",
        ["02"] = "Sí objeto de impuesto",
        ["03"] = "Sí objeto del impuesto y no obligado al desglose",
        ["04"] = "Sí objeto del impuesto y no causa impuesto",
        ["05"] = "Sí objeto del impuesto, IVA crédito PODEBI",
        ["06"] = "Sí objeto del IVA, No traslado IVA",
        ["07"] = "No traslado del IVA, Sí desglose IEPS",
        ["08"] = "No traslado del IVA, No desglose IEPS"
    };

    private static readonly Dictionary<string, string> TiposRelacion = new()
    {
        ["01"] = "Nota de crédito de los documentos relacionados",
        ["02"] = "Nota de débito de los documentos relacionados",
        ["03"] = "Devolución de mercancía sobre facturas o traslados previos",
        ["04"] = "Sustitución de los CFDI previos",
        ["05"] = "Traslados de mercancías facturados previamente",
        ["06"] = "Factura generada por los traslados previos",
        ["07"] = "CFDI por aplicación de anticipo"
    };

    private static string Num(decimal v, string formato) => v.ToString(formato, CultureInfo.InvariantCulture);

    private static Dictionary<string, string> VariablesConcepto(ConceptoDto k, XElement? nodo, int renglon)
    {
        decimal descuento = k.Descuento ?? 0;
        decimal descUnitario = k.Cantidad == 0 ? 0 : descuento / k.Cantidad;
        decimal ieps = k.Impuestos.Where(i => !EsRetencion(i) && i.Impuesto == "003").Sum(i => i.Importe);
        decimal iva = k.Impuestos.Where(i => !EsRetencion(i) && i.Impuesto == "002").Sum(i => i.Importe);
        decimal porUnidad(decimal total) => k.Cantidad == 0 ? 0 : total / k.Cantidad;
        string valorXml = nodo is null ? Num(k.ValorUnitario, "0.######") : A(nodo, "ValorUnitario");
        List<XElement> pedimentos = nodo?.Elements().Where(e => e.Name.LocalName == "InformacionAduanera").ToList() ?? [];
        XElement? terceros = nodo?.Elements().FirstOrDefault(e => e.Name.LocalName == "ACuentaTerceros");
        XElement? predial = nodo?.Elements().FirstOrDefault(e => e.Name.LocalName == "CuentaPredial");
        ImpuestoDto? tasaIva = k.Impuestos.FirstOrDefault(i => !EsRetencion(i) && i.Impuesto == "002");

        return new(StringComparer.Ordinal)
        {
            ["renglon"] = renglon.ToString(CultureInfo.InvariantCulture),
            ["claveprodserv_3"] = k.ClaveProdServ,
            ["claveunidad_3"] = k.ClaveUnidad,
            ["unidad"] = k.Unidad,
            ["noidentificacion"] = k.NoIdentificacion,
            ["producto"] = k.NoIdentificacion,
            ["cantidad"] = k.Cantidad.ToString("0.######", Mx),
            ["cantidadsindecimales"] = Num(Math.Round(k.Cantidad), "0"),
            ["descripcion"] = k.Descripcion,
            ["precio"] = Dinero(k.ValorUnitario),
            ["precio4"] = k.ValorUnitario.ToString("$#,##0.0000", CultureInfo.InvariantCulture),
            ["precio6"] = k.ValorUnitario.ToString("$#,##0.000000", CultureInfo.InvariantCulture),
            ["valorunitario"] = Dinero(k.ValorUnitario),
            ["valorunitario4"] = k.ValorUnitario.ToString("$#,##0.0000", CultureInfo.InvariantCulture),
            ["valorunitario6"] = k.ValorUnitario.ToString("$#,##0.000000", CultureInfo.InvariantCulture),
            ["valorunitarioxml"] = valorXml,
            ["valorunitarioxml4"] = Num(k.ValorUnitario, "0.0000"),
            ["valorunitarioxml6"] = Num(k.ValorUnitario, "0.000000"),
            ["valorunitario_ieps_3"] = Dinero(k.ValorUnitario + porUnidad(ieps)),
            ["valorunitario_iva_3"] = Dinero(k.ValorUnitario + porUnidad(iva)),
            ["valorunitario_ieps_iva_3"] = Dinero(k.ValorUnitario + porUnidad(ieps + iva)),
            ["descunitcalculado"] = Dinero(descUnitario),
            ["pu_menos_descunitcalculado"] = Dinero(k.ValorUnitario - descUnitario),
            ["descuento_3"] = Dinero(descuento),
            ["descuento_3sindecimales"] = Num(Math.Round(descuento), "#,##0"),
            ["porcentajedescuento"] = k.Importe == 0 ? "0" : Num(descuento / k.Importe * 100, "0.##"),
            ["importe_3"] = Dinero(k.Importe),
            ["importe_conieps_3"] = Dinero(k.Importe + ieps),
            ["subtotal"] = Dinero(k.Importe - descuento),
            ["tipoiva"] = tasaIva is null ? "" : EsExento(tasaIva) ? "Exento" : TasaPorcentaje(tasaIva),
            ["cuentapredial"] = k.CuentaPredial != "" ? k.CuentaPredial : predial is null ? "" : A(predial, "Numero"),
            ["aduananumpedimento1_3"] = pedimentos.ElementAtOrDefault(0) is { } p1 ? A(p1, "NumeroPedimento") : "",
            ["aduananumpedimento2_3"] = pedimentos.ElementAtOrDefault(1) is { } p2 ? A(p2, "NumeroPedimento") : "",
            ["aduananumpedimento3_3"] = pedimentos.ElementAtOrDefault(2) is { } p3 ? A(p3, "NumeroPedimento") : "",
            ["objetoimp_4"] = k.ObjetoImp,
            ["objetoimp_4desc"] = ObjetosImpuesto.GetValueOrDefault(k.ObjetoImp, ""),
            ["acuentaterceros_rfc_4"] = terceros is null ? "" : A(terceros, "RfcACuentaTerceros"),
            ["acuentaterceros_nombre_4"] = terceros is null ? "" : A(terceros, "NombreACuentaTerceros"),
            ["acuentaterceros_regimenfiscal_4"] = terceros is null ? "" : A(terceros, "RegimenFiscalACuentaTerceros"),
            ["acuentaterceros_domiciliofiscal_4"] = terceros is null ? "" : A(terceros, "DomicilioFiscalACuentaTerceros")
        };
    }

    /// <summary>Variables del comprobante que se leen directo del XML (impuestos por tasa, locales, global, relacionados, complementos).</summary>
    private static Dictionary<string, string> VariablesXml(CfdiDto c, XDocument xml)
    {
        var v = new Dictionary<string, string>(StringComparer.Ordinal);
        XElement? raiz = xml.Root;
        if (raiz is null)
            return v;

        decimal descuento = c.Descuento ?? 0;
        List<ImpuestoDto> traslados = c.Impuestos.Where(i => !EsRetencion(i)).ToList();
        List<ImpuestoDto> retenciones = c.Impuestos.Where(EsRetencion).ToList();
        decimal ieps = traslados.Where(i => i.Impuesto == "003").Sum(i => i.Importe);
        decimal iva = traslados.Where(i => i.Impuesto == "002").Sum(i => i.Importe);
        decimal ivaRet = retenciones.Where(i => i.Impuesto == "002").Sum(i => i.Importe);
        decimal isrRet = retenciones.Where(i => i.Impuesto == "001").Sum(i => i.Importe);
        decimal tc = c.TipoCambio is > 0 ? c.TipoCambio.Value : 1;
        string sello = string.IsNullOrEmpty(c.Sello) ? c.SelloCfd : c.Sello;

        v["comprobante_fechaiso8601"] = c.FechaEmision.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        v["comprobante_sello"] = sello;
        v["comprobante_sello_ticket"] = sello;
        v["comprobante_sello150"] = sello;
        v["comprobante_certificado"] = c.Certificado;
        v["comprobante_confirmacion_3"] = c.Confirmacion;
        v["comprobante_moneda"] = c.Moneda;
        v["comprobante_subtotalsinieps"] = Dinero(c.SubTotal);
        v["comprobante_subtotalmasieps"] = Dinero(c.SubTotal + ieps);
        v["comprobante_subtotalmenosdesc"] = Dinero(c.SubTotal - descuento);
        v["comprobante_subtotalmenosdescyret"] = Dinero(c.SubTotal - descuento - ivaRet - isrRet);
        v["comprobante_subtotalmasiva"] = Dinero(c.SubTotal - descuento + iva);
        v["comprobante_subtotalmasivaconletra"] = ImporteConLetra.Convertir(c.SubTotal - descuento + iva, c.Moneda);
        v["comprobante_descuentoporcentaje"] = c.SubTotal == 0 ? "0" : Num(descuento / c.SubTotal * 100, "0.##");
        v["comprobante_totaltxt"] = Num(c.Total, "#,##0.00");
        v["comprobante_totalpesos"] = Dinero(c.Total * tc);
        v["ivaretenido"] = Dinero(ivaRet);
        v["isrretenido"] = Dinero(isrRet);
        v["ivatrasladadopesos"] = Dinero(iva * tc);
        v["ivatrasladado16"] = Dinero(traslados.Where(i => i.Impuesto == "002" && i.Tasa == 0.16m).Sum(i => i.Importe));
        v["ivatrasladado8"] = Dinero(traslados.Where(i => i.Impuesto == "002" && i.Tasa == 0.08m).Sum(i => i.Importe));
        v["receptor_residenciafiscal_3"] = c.ResidenciaFiscal;
        v["sumatoria_cantidades"] = c.Conceptos.Sum(k => k.Cantidad).ToString("#,##0.######", CultureInfo.InvariantCulture);
        v["sumatoria_cantidadessindecimales"] = Num(Math.Round(c.Conceptos.Sum(k => k.Cantidad)), "#,##0");

        // IEPS trasladado por tasa (hasta 5).
        int n = 0;
        foreach (var g in traslados.Where(i => i.Impuesto == "003" && !EsExento(i)).GroupBy(i => (i.Tasa, i.TipoFactor)).Take(5))
        {
            n++;
            v[$"iepstraslimporte{n}"] = Dinero(g.Sum(i => i.Importe));
            v[$"iepstrasltasa{n}"] = Tasa6(g.Key.Tasa);
            v[$"iepstrasltasatxt{n}"] = $"IEPS {TasaPorcentaje(new ImpuestoDto { Tasa = g.Key.Tasa, TipoFactor = g.Key.TipoFactor })}";
        }

        // Impuestos locales (complemento implocal).
        XElement? locales = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "ImpuestosLocales");
        if (locales is not null)
        {
            void Locales(string elemento, string prefijo, string nombre, string tasa, string importe, string total)
            {
                int i = 0;
                foreach (XElement e in locales.Elements().Where(e => e.Name.LocalName == elemento).Take(5))
                {
                    i++;
                    v[$"{prefijo}importe{i}"] = DineroXml(e, importe);
                    v[$"{prefijo}tasa{i}"] = A(e, tasa);
                    v[$"{prefijo}nombre{i}"] = A(e, nombre);
                }
                v[$"{prefijo}importetotal"] = DineroXml(locales, total);
            }

            Locales("RetencionesLocales", "implocret", "ImpLocRetenido", "TasadeRetencion", "Importe", "TotaldeRetenciones");
            Locales("TrasladosLocales", "imploctras", "ImpLocTrasladado", "TasadeTraslado", "Importe", "TotaldeTraslados");
        }

        // Factura global (público en general).
        if (raiz.Elements().FirstOrDefault(e => e.Name.LocalName == "InformacionGlobal") is { } global)
        {
            v["global_anio"] = A(global, "Año");
            v["global_meses"] = A(global, "Meses");
            v["global_periodicidad"] = A(global, "Periodicidad");
        }

        // CFDI relacionados: hasta 3 grupos (en 4.0 puede haber uno por tipo de relación).
        int r = 0;
        foreach (XElement grupo in raiz.Elements().Where(e => e.Name.LocalName == "CfdiRelacionados").Take(3))
        {
            r++;
            string sufijo = r == 1 ? "" : $"_{r}";
            string tipo = A(grupo, "TipoRelacion");
            v[$"tiporelacion_3{sufijo}"] = tipo;
            v[$"tiporelacion_descripcion_3{sufijo}"] = TiposRelacion.GetValueOrDefault(tipo, "");
            v[$"relacioncfdis_3{sufijo}"] = string.Join(", ", grupo.Elements().Where(e => e.Name.LocalName == "CfdiRelacionado").Select(e => A(e, "UUID")));
        }

        // Leyendas fiscales (hasta 3).
        int l = 0;
        foreach (XElement leyenda in raiz.Descendants().Where(e => e.Name.LocalName == "Leyenda").Take(3))
        {
            l++;
            v[$"leyendasfiscales_disposicionfiscal_{l}"] = A(leyenda, "disposicionFiscal");
            v[$"leyendasfiscales_norma_{l}"] = A(leyenda, "norma");
            v[$"leyendasfiscales_textoleyenda_{l}"] = A(leyenda, "textoLeyenda");
        }

        // Comercio exterior: datos generales.
        if (raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "ComercioExterior") is { } cce)
        {
            v["exp_version"] = A(cce, "Version");
            v["exp_tipooper"] = A(cce, "TipoOperacion");
            v["exp_clavpedim"] = A(cce, "ClaveDePedimento");
            v["exp_incoterm"] = A(cce, "Incoterm");
            v["exp_certorigen"] = A(cce, "CertificadoOrigen");
            v["exp_numcertorigen"] = A(cce, "NumCertificadoOrigen");
            v["exp_tipocambio"] = A(cce, "TipoCambioUSD");
            v["exp_totaldlls"] = A(cce, "TotalUSD");
            v["exp_subdivision"] = A(cce, "Subdivision");
        }

        return v;
    }

    private static Dictionary<string, string> VariablesImpuesto(ImpuestoDto i) => new(StringComparer.Ordinal)
    {
        ["tipoimpuesto"] = EsRetencion(i) ? "Retención" : "Traslado",
        ["impuesto"] = i.Impuesto,
        ["tipofactor"] = i.TipoFactor,
        ["tasaocuota"] = EsExento(i) ? "" : TasaPorcentaje(i),
        ["base"] = Dinero(i.Base),
        ["importeimp"] = EsExento(i) ? "" : Dinero(i.Importe)
    };

    private static string A(XElement e, string nombre) => e.Attribute(nombre)?.Value ?? "";

    private static string DineroXml(XElement e, string nombre) =>
        decimal.TryParse(A(e, nombre), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? Dinero(v) : A(e, nombre);

    private static Dictionary<string, string> VariablesPago(XElement p)
    {
        string forma = A(p, "FormaDePagoP");
        DateTime? fecha = DateTime.TryParse(A(p, "FechaPago"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f) ? f : null;
        XElement? impuestos = p.Elements().FirstOrDefault(e => e.Name.LocalName == "ImpuestosP");

        return new(StringComparer.Ordinal)
        {
            ["pago_fechapago"] = Fecha(fecha),
            ["pago_formadepagop"] = forma == "" ? "" : $"{forma} - {Cat.Forma.GetValueOrDefault(forma, "")}".TrimEnd(' ', '-'),
            ["pago_monedap"] = A(p, "MonedaP"),
            ["pago_tipocambiop"] = A(p, "TipoCambioP"),
            ["pago_monto"] = DineroXml(p, "Monto"),
            ["pago_numoperacion"] = A(p, "NumOperacion"),
            ["pago_rfcemisorctaord"] = A(p, "RfcEmisorCtaOrd"),
            ["pago_nombancoordext"] = A(p, "NomBancoOrdExt"),
            ["pago_ctaordenante"] = A(p, "CtaOrdenante"),
            ["pago_rfcemisorctaben"] = A(p, "RfcEmisorCtaBen"),
            ["pago_ctabeneficiario"] = A(p, "CtaBeneficiario"),
            ["pago_tipocadpago"] = A(p, "TipoCadPago"),
            ["pago_certpago"] = A(p, "CertPago"),
            ["pago_cadpago"] = A(p, "CadPago"),
            ["pago_sellopago"] = A(p, "SelloPago"),
            ["pago_impuestosp_r"] = TablaImpuestos(impuestos, "RetencionP", "Retenciones"),
            ["pago_impuestosp_t"] = TablaImpuestos(impuestos, "TrasladoP", "Traslados")
        };
    }

    private static Dictionary<string, string> VariablesDocto(XElement d)
    {
        XElement? impuestos = d.Elements().FirstOrDefault(e => e.Name.LocalName == "ImpuestosDR");

        return new(StringComparer.Ordinal)
        {
            ["pago_iddocumento"] = A(d, "IdDocumento"),
            ["pago_serie"] = A(d, "Serie"),
            ["pago_folio"] = A(d, "Folio"),
            ["pago_monedadr"] = A(d, "MonedaDR"),
            ["pago_tipocambiodr"] = A(d, "EquivalenciaDR") is { Length: > 0 } eq ? eq : A(d, "TipoCambioDR"),
            ["pago_objetoimp"] = A(d, "ObjetoImpDR"),
            ["pago_numparcialidad"] = A(d, "NumParcialidad"),
            ["pago_impsaldoant"] = DineroXml(d, "ImpSaldoAnt"),
            ["pago_imppagado"] = DineroXml(d, "ImpPagado"),
            ["pago_impsaldoinsoluto"] = DineroXml(d, "ImpSaldoInsoluto"),
            ["pago_det_impuestosdr_r"] = TablaImpuestos(impuestos, "RetencionDR", "Retenciones"),
            ["pago_det_impuestosdr_t"] = TablaImpuestos(impuestos, "TrasladoDR", "Traslados")
        };
    }

    /// <summary>Tabla chica con los traslados o retenciones del pago o del documento (ya en HTML).</summary>
    private static string TablaImpuestos(XElement? impuestos, string elemento, string titulo)
    {
        List<XElement> filas = impuestos?.Descendants().Where(e => e.Name.LocalName == elemento).ToList() ?? [];
        if (filas.Count == 0)
            return "";

        string sufijo = elemento.EndsWith("DR") ? "DR" : "P";
        var sb = new StringBuilder();
        sb.Append("<table style=\"font-size: 9px; float: right; background-color: #eee;\" cellspacing=\"2\"><tbody><tr>");
        foreach (string t in new[] { titulo, "Impuesto", "Tipo Factor", "Tasa o Cuota", "Base", "Importe" })
            sb.Append($"<td style=\"padding: 1px 8px; color: #888; font-weight: bold; text-align: center;\">{t}</td>");
        sb.Append("</tr>");

        foreach (XElement f in filas)
        {
            string impuesto = A(f, "Impuesto" + sufijo);
            sb.Append("<tr>");
            foreach (string v in new[]
                     {
                         "", $"{impuesto} {NombresImpuesto.GetValueOrDefault(impuesto, "")}".Trim(), A(f, "TipoFactor" + sufijo),
                         A(f, "TasaOCuota" + sufijo), DineroXml(f, "Base" + sufijo), DineroXml(f, "Importe" + sufijo)
                     })
                sb.Append($"<td style=\"padding: 1px 8px; text-align: right;\">{WebUtility.HtmlEncode(v)}</td>");
            sb.Append("</tr>");
        }

        return sb.Append("</tbody></table>").ToString();
    }

    // Variables que ya traen HTML (tablas de impuestos del pago).
    private static readonly HashSet<string> ConHtml =
        ["pago_impuestosp_r", "pago_impuestosp_t", "pago_det_impuestosdr_r", "pago_det_impuestosdr_t"];

    // Sellos, cadenas y UUID sin espacios: se cortan en cualquier carácter para que no ensanchen la tabla.
    private static readonly HashSet<string> Largos =
    [
        "cfdi_sellocfd", "cfdi_sellosat", "cfdi_cadenaoriginaltimbre", "cfdi_sellocfd_ticket", "cfdi_sellosat_ticket",
        "cfdi_cadenaoriginaltimbre_ticket", "cfdi_sellocfd150", "cfdi_sellosat150", "cfdi_cadenaoriginaltimbre150",
        "comprobante_sello", "comprobante_sello_ticket", "comprobante_sello150", "comprobante_certificado",
        "pago_cadpago", "pago_sellopago", "pago_certpago", "pago_iddocumento", "relacioncfdis_3", "relacioncfdis_3_2", "relacioncfdis_3_3"
    ];

    private static string Reemplazar(string html, Dictionary<string, string> valores) =>
        Variable().Replace(html, m =>
        {
            string nombre = m.Groups[1].Value;
            if (!valores.TryGetValue(nombre, out string? v))
                return m.Value;
            if (ConHtml.Contains(nombre))
                return v;
            return Largos.Contains(nombre) && v != ""
                ? $"<span style=\"word-break: break-all;\">{WebUtility.HtmlEncode(v)}</span>"
                : WebUtility.HtmlEncode(v);
        });

    // ---------- Bloques que se repiten ----------

    /// <summary>
    /// Repite lo que hay entre el elemento &lt;tag id="inicio"&gt; y &lt;tag id="fin"&gt; (ambos se quitan) una vez por
    /// elemento. Sin marcas, la plantilla queda igual.
    /// </summary>
    private static string Repetir<T>(string html, string tag, string idInicio, string idFin, IReadOnlyList<T> elementos, Func<string, T, string> llenar)
    {
        if (Elemento(html, tag, idInicio, 0) is not (int ini, int finIni))
            return html;
        if (Elemento(html, tag, idFin, finIni) is not (int fin, int finFin))
            return html;

        string segmento = html[finIni..fin];
        var sb = new StringBuilder(html.Length + segmento.Length * elementos.Count);
        sb.Append(html, 0, ini);
        foreach (T e in elementos)
            sb.Append(llenar(segmento, e));
        sb.Append(html, finFin, html.Length - finFin);
        return sb.ToString();
    }

    private static string Quitar(string html, string tag, string id) =>
        Elemento(html, tag, id, 0) is (int ini, int fin) ? html[..ini] + html[fin..] : html;

    /// <summary>Inicio y fin (después de la etiqueta de cierre) del elemento &lt;tag … id="id"&gt;, contando los anidados.</summary>
    private static (int Inicio, int Fin)? Elemento(string html, string tag, string id, int desde)
    {
        Match m = new Regex($"""<{tag}\b[^>]*\bid\s*=\s*["']{Regex.Escape(id)}["'][^>]*>""", RegexOptions.IgnoreCase).Match(html, desde);
        if (!m.Success)
            return null;

        var etiquetas = new Regex($@"<(/?){tag}\b[^>]*>", RegexOptions.IgnoreCase);
        int nivel = 0;
        for (Match e = etiquetas.Match(html, m.Index); e.Success; e = e.NextMatch())
        {
            nivel += e.Groups[1].Value == "/" ? -1 : 1;
            if (nivel == 0)
                return (m.Index, e.Index + e.Length);
        }

        return (m.Index, m.Index + m.Length);
    }
}
