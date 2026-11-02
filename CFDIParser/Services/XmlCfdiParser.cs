using System.Globalization;
using System.Xml.Linq;
using CFDIParser.Models;

namespace CFDIParser.Services;

public class XmlCfdiParser
{
    public CfdiDto Parse(string rutaXml)
    {
        if (string.IsNullOrWhiteSpace(rutaXml))
        {
            throw new ArgumentException(
                "La ruta del XML no puede estar vacía.",
                nameof(rutaXml));
        }

        if (!File.Exists(rutaXml))
        {
            throw new FileNotFoundException(
                "No se encontró el archivo XML.",
                rutaXml);
        }

        XDocument documento = XDocument.Load(
            rutaXml,
            LoadOptions.PreserveWhitespace |
            LoadOptions.SetLineInfo);

        XElement comprobante = documento.Root
            ?? throw new InvalidOperationException(
                "El XML no contiene un elemento raíz.");

        if (!EsElemento(comprobante, "Comprobante"))
        {
            throw new InvalidOperationException(
                "El XML no corresponde a un CFDI válido.");
        }

        XElement? emisor = Hijo(comprobante, "Emisor");
        XElement? receptor = Hijo(comprobante, "Receptor");
        XElement? impuestosGlobales = Hijo(comprobante, "Impuestos");
        XElement? timbre = Descendiente(comprobante, "TimbreFiscalDigital");

        var cfdi = new CfdiDto
        {
            Version = Atributo(comprobante, "Version"),
            Serie = Atributo(comprobante, "Serie"),
            Folio = Atributo(comprobante, "Folio"),
            FechaEmision = FechaObligatoria(
                Atributo(comprobante, "Fecha"),
                "Fecha"),
            TipoComprobante = Atributo(comprobante, "TipoDeComprobante"),
            Moneda = Atributo(comprobante, "Moneda"),
            TipoCambio = DecimalOpcional(Atributo(comprobante, "TipoCambio")),
            SubTotal = DecimalObligatorio(
                Atributo(comprobante, "SubTotal"),
                "SubTotal"),
            Descuento = DecimalOpcional(Atributo(comprobante, "Descuento")),
            Total = DecimalObligatorio(
                Atributo(comprobante, "Total"),
                "Total"),
            FormaPago = Atributo(comprobante, "FormaPago"),
            MetodoPago = Atributo(comprobante, "MetodoPago"),
            CondicionesDePago = Atributo(comprobante, "CondicionesDePago"),
            LugarExpedicion = Atributo(comprobante, "LugarExpedicion"),
            Exportacion = Atributo(comprobante, "Exportacion"),
            NoCertificado = Atributo(comprobante, "NoCertificado"),
            Certificado = Atributo(comprobante, "Certificado"),
            Sello = Atributo(comprobante, "Sello"),
            Confirmacion = Atributo(comprobante, "Confirmacion"),

            RFCEmisor = Atributo(emisor, "Rfc"),
            NombreEmisor = Atributo(emisor, "Nombre"),
            RegimenFiscalEmisor = Atributo(emisor, "RegimenFiscal"),
            FacAtrAdquirente = Atributo(emisor, "FacAtrAdquirente"),

            RFCReceptor = Atributo(receptor, "Rfc"),
            NombreReceptor = Atributo(receptor, "Nombre"),
            DomicilioFiscalReceptor = Atributo(receptor, "DomicilioFiscalReceptor"),
            RegimenFiscalReceptor = Atributo(receptor, "RegimenFiscalReceptor"),
            UsoCfdi = Atributo(receptor, "UsoCFDI"),
            ResidenciaFiscal = Atributo(receptor, "ResidenciaFiscal"),
            NumRegIdTrib = Atributo(receptor, "NumRegIdTrib"),

            TotalImpuestosTrasladados = DecimalOpcional(
                Atributo(impuestosGlobales, "TotalImpuestosTrasladados")),
            TotalImpuestosRetenidos = DecimalOpcional(
                Atributo(impuestosGlobales, "TotalImpuestosRetenidos")),

            UUID = Atributo(timbre, "UUID"),
            FechaTimbrado = FechaOpcional(Atributo(timbre, "FechaTimbrado")),
            RfcProvCertif = Atributo(timbre, "RfcProvCertif"),
            NoCertificadoSat = Atributo(timbre, "NoCertificadoSAT"),
            VersionTimbre = Atributo(timbre, "Version"),
            SelloCfd = Atributo(timbre, "SelloCFD"),
            SelloSat = Atributo(timbre, "SelloSAT"),
            MontoTotalPagos = LeerMontoTotalPagos(comprobante)
        };

        cfdi.Conceptos = LeerConceptos(comprobante);
        cfdi.Impuestos = LeerImpuestosGlobales(impuestosGlobales);
        cfdi.Relacionados = LeerRelacionados(comprobante);

        return cfdi;
    }

    private static List<ConceptoDto> LeerConceptos(XElement comprobante)
    {
        XElement? conceptosNodo = Hijo(comprobante, "Conceptos");
        var conceptos = new List<ConceptoDto>();

        if (conceptosNodo is null)
        {
            return conceptos;
        }

        foreach (XElement nodo in Hijos(conceptosNodo, "Concepto"))
        {
            var concepto = new ConceptoDto
            {
                ClaveProdServ = Atributo(nodo, "ClaveProdServ"),
                NoIdentificacion = Atributo(nodo, "NoIdentificacion"),
                Cantidad = DecimalObligatorio(
                    Atributo(nodo, "Cantidad"),
                    "Concepto.Cantidad"),
                ClaveUnidad = Atributo(nodo, "ClaveUnidad"),
                Unidad = Atributo(nodo, "Unidad"),
                Descripcion = Atributo(nodo, "Descripcion"),
                ValorUnitario = DecimalObligatorio(
                    Atributo(nodo, "ValorUnitario"),
                    "Concepto.ValorUnitario"),
                Importe = DecimalObligatorio(
                    Atributo(nodo, "Importe"),
                    "Concepto.Importe"),
                Descuento = DecimalOpcional(Atributo(nodo, "Descuento")),
                ObjetoImp = Atributo(nodo, "ObjetoImp"),
                CuentaPredial = LeerCuentaPredial(nodo)
            };

            concepto.Impuestos = LeerImpuestosConcepto(nodo);
            conceptos.Add(concepto);
        }

        return conceptos;
    }

    private static List<ImpuestoDto> LeerImpuestosConcepto(XElement concepto)
    {
        XElement? impuestosNodo = Hijo(concepto, "Impuestos");
        var impuestos = new List<ImpuestoDto>();

        if (impuestosNodo is null)
        {
            return impuestos;
        }

        XElement? traslados = Hijo(impuestosNodo, "Traslados");
        if (traslados is not null)
        {
            foreach (XElement traslado in Hijos(traslados, "Traslado"))
            {
                impuestos.Add(MapearImpuesto(traslado, "TRASLADO"));
            }
        }

        XElement? retenciones = Hijo(impuestosNodo, "Retenciones");
        if (retenciones is not null)
        {
            foreach (XElement retencion in Hijos(retenciones, "Retencion"))
            {
                impuestos.Add(MapearImpuesto(retencion, "RETENCION"));
            }
        }

        return impuestos;
    }

    private static List<ImpuestoDto> LeerImpuestosGlobales(
        XElement? impuestosNodo)
    {
        var impuestos = new List<ImpuestoDto>();

        if (impuestosNodo is null)
        {
            return impuestos;
        }

        XElement? traslados = Hijo(impuestosNodo, "Traslados");
        if (traslados is not null)
        {
            foreach (XElement traslado in Hijos(traslados, "Traslado"))
            {
                impuestos.Add(MapearImpuesto(traslado, "TRASLADO"));
            }
        }

        XElement? retenciones = Hijo(impuestosNodo, "Retenciones");
        if (retenciones is not null)
        {
            foreach (XElement retencion in Hijos(retenciones, "Retencion"))
            {
                impuestos.Add(MapearImpuesto(retencion, "RETENCION"));
            }
        }

        return impuestos;
    }

    private static ImpuestoDto MapearImpuesto(
        XElement nodo,
        string tipo)
    {
        return new ImpuestoDto
        {
            Tipo = tipo,
            Impuesto = Atributo(nodo, "Impuesto"),
            Base = DecimalOpcional(Atributo(nodo, "Base")) ?? 0m,
            TipoFactor = Atributo(nodo, "TipoFactor"),
            Tasa = DecimalOpcional(
                Atributo(nodo, "TasaOCuota")) ?? 0m,
            Importe = DecimalOpcional(Atributo(nodo, "Importe")) ?? 0m
        };
    }

    private static List<CfdiRelacionadoDto> LeerRelacionados(
        XElement comprobante)
    {
        var resultado = new List<CfdiRelacionadoDto>();

        foreach (XElement grupo in Hijos(comprobante, "CfdiRelacionados"))
        {
            string tipoRelacion = Atributo(grupo, "TipoRelacion");

            foreach (XElement relacionado in Hijos(grupo, "CfdiRelacionado"))
            {
                resultado.Add(new CfdiRelacionadoDto
                {
                    TipoRelacion = tipoRelacion,
                    UUID = Atributo(relacionado, "UUID")
                });
            }
        }

        return resultado;
    }

    private static decimal? LeerMontoTotalPagos(XElement comprobante)
    {
        XElement? totales = Descendiente(comprobante, "Totales");

        if (totales is not null)
        {
            decimal? monto = DecimalOpcional(
                Atributo(totales, "MontoTotalPagos"));

            if (monto.HasValue)
            {
                return monto;
            }
        }

        IEnumerable<XElement> pagos = comprobante
            .Descendants()
            .Where(elemento => EsElemento(elemento, "Pago"));

        decimal suma = 0m;
        bool encontro = false;

        foreach (XElement pago in pagos)
        {
            decimal? monto = DecimalOpcional(Atributo(pago, "Monto"));
            if (monto.HasValue)
            {
                suma += monto.Value;
                encontro = true;
            }
        }

        return encontro ? suma : null;
    }

    private static string LeerCuentaPredial(XElement concepto)
    {
        XElement? cuenta = Hijo(concepto, "CuentaPredial");
        return Atributo(cuenta, "Numero");
    }

    private static XElement? Hijo(XElement padre, string nombreLocal)
    {
        return padre.Elements()
            .FirstOrDefault(elemento => EsElemento(elemento, nombreLocal));
    }

    private static IEnumerable<XElement> Hijos(
        XElement padre,
        string nombreLocal)
    {
        return padre.Elements()
            .Where(elemento => EsElemento(elemento, nombreLocal));
    }

    // Impuestos del nodo Totales del complemento de pagos 2.0 (en MXN). Pagos 1.0 no los trae: regresa ceros.
    public static (decimal Iva, decimal IvaRetenido, decimal IsrRetenido) LeerImpuestosPagos(string rutaXml)
    {
        XElement comprobante = XDocument.Load(rutaXml).Root
            ?? throw new InvalidOperationException("El XML no tiene nodo raíz.");

        XElement? totales = Descendiente(comprobante, "Totales");

        decimal Valor(string atributo) => DecimalOpcional(Atributo(totales, atributo)) ?? 0m;

        return (Valor("TotalTrasladosImpuestoIVA16") + Valor("TotalTrasladosImpuestoIVA8"),
                Valor("TotalRetencionesIVA"),
                Valor("TotalRetencionesISR"));
    }

    private static XElement? Descendiente(
        XElement padre,
        string nombreLocal)
    {
        return padre.Descendants()
            .FirstOrDefault(elemento => EsElemento(elemento, nombreLocal));
    }

    private static bool EsElemento(XElement elemento, string nombreLocal)
    {
        return string.Equals(
            elemento.Name.LocalName,
            nombreLocal,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Atributo(XElement? elemento, string nombre)
    {
        if (elemento is null)
        {
            return "";
        }

        XAttribute? atributo = elemento.Attributes()
            .FirstOrDefault(item => string.Equals(
                item.Name.LocalName,
                nombre,
                StringComparison.OrdinalIgnoreCase));

        return atributo?.Value?.Trim() ?? "";
    }

    private static decimal DecimalObligatorio(
        string valor,
        string campo)
    {
        decimal? resultado = DecimalOpcional(valor);

        return resultado
            ?? throw new InvalidOperationException(
                $"El atributo {campo} no contiene un decimal válido.");
    }

    private static decimal? DecimalOpcional(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        return decimal.TryParse(
            valor,
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out decimal resultado)
                ? resultado
                : null;
    }

    private static DateTime FechaObligatoria(
        string valor,
        string campo)
    {
        DateTime? fecha = FechaOpcional(valor);

        return fecha
            ?? throw new InvalidOperationException(
                $"El atributo {campo} no contiene una fecha válida.");
    }

    private static DateTime? FechaOpcional(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        return DateTime.TryParse(
            valor,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out DateTime resultado)
                ? resultado
                : null;
    }
}
