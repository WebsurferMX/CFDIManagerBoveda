using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using CFDIParser.Models;

namespace CFDIParser.Services;

public sealed class ConsultaEstadoSatService
{
    private const string UrlServicio =
        "https://consultaqr.facturaelectronica.sat.gob.mx/" +
        "ConsultaCFDIService.svc";

    private const string SoapAction =
        "http://tempuri.org/" +
        "IConsultaCFDIService/Consulta";

    private readonly HttpClient _httpClient;

    public ConsultaEstadoSatService(
        HttpClient httpClient)
    {
        _httpClient =
            httpClient;
    }

    public async Task<EstadoSatDto> ConsultarAsync(
        string uuid,
        string rfcEmisor,
        string rfcReceptor,
        decimal total,
        CancellationToken cancellationToken = default)
    {
        ValidarDatosConsulta(
            uuid,
            rfcEmisor,
            rfcReceptor);

        string expresionImpresa =
            ConstruirExpresionImpresa(
                uuid,
                rfcEmisor,
                rfcReceptor,
                total);

        string solicitudSoap =
            ConstruirSolicitudSoap(
                expresionImpresa);

        using var solicitud =
            new HttpRequestMessage(
                HttpMethod.Post,
                UrlServicio);

        solicitud.Headers.TryAddWithoutValidation(
            "SOAPAction",
            $"\"{SoapAction}\"");

        solicitud.Headers.TryAddWithoutValidation(
            "Accept",
            "text/xml");

        solicitud.Content =
            new StringContent(
                solicitudSoap,
                Encoding.UTF8,
                "text/xml");

        try
        {
            using HttpResponseMessage respuesta =
                await _httpClient.SendAsync(
                    solicitud,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);

            string contenidoRespuesta =
                await respuesta.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                return CrearError(
                    codigo:
                        $"HTTP {(int)respuesta.StatusCode}",

                    mensaje:
                        LimitarTexto(
                            contenidoRespuesta,
                            1500));
            }

            if (string.IsNullOrWhiteSpace(
                    contenidoRespuesta))
            {
                return CrearError(
                    codigo:
                        "RESPUESTA VACÍA",

                    mensaje:
                        "El SAT no devolvió contenido.");
            }

            return ProcesarRespuestaSoap(
                contenidoRespuesta);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return CrearError(
                codigo:
                    "TIEMPO AGOTADO",

                mensaje:
                    "El servicio del SAT tardó demasiado " +
                    "en responder.");
        }
        catch (HttpRequestException ex)
        {
            return CrearError(
                codigo:
                    "ERROR DE CONEXIÓN",

                mensaje:
                    ex.Message);
        }
        catch (Exception ex)
        {
            return CrearError(
                codigo:
                    "ERROR NO CONTROLADO",

                mensaje:
                    ex.Message);
        }
    }

    private static string ConstruirExpresionImpresa(
        string uuid,
        string rfcEmisor,
        string rfcReceptor,
        decimal total)
    {
        string uuidNormalizado =
            uuid
                .Trim()
                .ToUpperInvariant();

        string emisorNormalizado =
            rfcEmisor
                .Trim()
                .ToUpperInvariant();

        string receptorNormalizado =
            rfcReceptor
                .Trim()
                .ToUpperInvariant();

        /*
         * El SAT recibe el total con seis decimales.
         * CultureInfo.InvariantCulture evita usar coma decimal.
         */
        string totalNormalizado =
            total.ToString(
                "0.000000",
                CultureInfo.InvariantCulture);

        return
            $"?re={emisorNormalizado}" +
            $"&rr={receptorNormalizado}" +
            $"&tt={totalNormalizado}" +
            $"&id={uuidNormalizado}";
    }

    private static string ConstruirSolicitudSoap(
        string expresionImpresa)
    {
        string expresionCodificada =
            WebUtility.HtmlEncode(
                expresionImpresa);

        return
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soapenv:Envelope
                xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                xmlns:tem="http://tempuri.org/">
                <soapenv:Header />
                <soapenv:Body>
                    <tem:Consulta>
                        <tem:expresionImpresa>{expresionCodificada}</tem:expresionImpresa>
                    </tem:Consulta>
                </soapenv:Body>
            </soapenv:Envelope>
            """;
    }

    private static EstadoSatDto ProcesarRespuestaSoap(
        string contenidoRespuesta)
    {
        XDocument documento;

        try
        {
            documento =
                XDocument.Parse(
                    contenidoRespuesta,
                    LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            return CrearError(
                codigo:
                    "RESPUESTA INVÁLIDA",

                mensaje:
                    "La respuesta del SAT no contiene XML válido. " +
                    ex.Message);
        }

        XElement? fault =
            documento
                .Descendants()
                .FirstOrDefault(
                    elemento =>
                        elemento.Name.LocalName.Equals(
                            "Fault",
                            StringComparison.OrdinalIgnoreCase));

        if (fault is not null)
        {
            string mensajeFault =
                BuscarValor(
                    fault,
                    "faultstring");

            if (string.IsNullOrWhiteSpace(
                    mensajeFault))
            {
                mensajeFault =
                    fault.Value.Trim();
            }

            return CrearError(
                codigo:
                    "SOAP FAULT",

                mensaje:
                    mensajeFault);
        }

        XElement? consultaResult =
            documento
                .Descendants()
                .FirstOrDefault(
                    elemento =>
                        elemento.Name.LocalName.Equals(
                            "ConsultaResult",
                            StringComparison.OrdinalIgnoreCase));

        if (consultaResult is null)
        {
            return CrearError(
                codigo:
                    "RESPUESTA INCOMPLETA",

                mensaje:
                    "El SAT no devolvió el nodo ConsultaResult.");
        }

        string codigoEstatus = BuscarValor(consultaResult,"CodigoEstatus");
        string estado = BuscarValor(consultaResult,"Estado");
        string esCancelable = BuscarValor(consultaResult,"EsCancelable");
        string estatusCancelacion = BuscarValor(consultaResult,"EstatusCancelacion");
        string validacionEfos = BuscarValor(consultaResult,"ValidacionEfos");

        /*
         * Algunas respuestas pueden devolver el resultado
         * interno como texto XML escapado.
         */
        if (ValoresVacios(
                codigoEstatus,
                estado,
                esCancelable,
                estatusCancelacion,
                validacionEfos))
        {
            XElement? contenidoInterno =
                IntentarLeerXmlInterno(
                    consultaResult.Value);

            if (contenidoInterno is not null)
            {
                codigoEstatus =
                    BuscarValor(
                        contenidoInterno,
                        "CodigoEstatus");

                estado =
                    BuscarValor(
                        contenidoInterno,
                        "Estado");

                esCancelable =
                    BuscarValor(
                        contenidoInterno,
                        "EsCancelable");

                estatusCancelacion =
                    BuscarValor(
                        contenidoInterno,
                        "EstatusCancelacion");

                validacionEfos =
                    BuscarValor(
                        contenidoInterno,
                        "ValidacionEfos");
            }
        }

        string estadoNormalizado =
            NormalizarEstado(
                estado,
                codigoEstatus);

        bool consultaExitosa =
            !estadoNormalizado.Equals(
                "ERROR",
                StringComparison.OrdinalIgnoreCase);

        return new EstadoSatDto
        {
            CodigoEstatus =
                codigoEstatus,

            Estado =
                estadoNormalizado,

            EsCancelable =
                esCancelable,

            EstatusCancelacion =
                estatusCancelacion,

            ValidacionEfos =
                validacionEfos,

            FechaConsulta =
                DateTime.Now,

            ConsultaExitosa =
                consultaExitosa,

            MensajeError =
                consultaExitosa
                    ? null
                    : codigoEstatus
        };
    }

    private static XElement? IntentarLeerXmlInterno(
        string contenido)
    {
        if (string.IsNullOrWhiteSpace(
                contenido))
        {
            return null;
        }

        string texto =
            WebUtility.HtmlDecode(
                contenido.Trim());

        if (!texto.StartsWith(
                "<",
                StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return XElement.Parse(
                texto);
        }
        catch
        {
            return null;
        }
    }

    private static string BuscarValor(
        XElement nodo,
        string nombreElemento)
    {
        XElement? elemento =
            nodo
                .DescendantsAndSelf()
                .FirstOrDefault(
                    actual =>
                        actual.Name.LocalName.Equals(
                            nombreElemento,
                            StringComparison.OrdinalIgnoreCase));

        return elemento?.Value.Trim()
            ?? "";
    }

    private static string NormalizarEstado(
        string estado,
        string codigoEstatus)
    {
        string estadoLimpio =
            (estado ?? "")
                .Trim();

        if (!string.IsNullOrWhiteSpace(
                estadoLimpio))
        {
            if (estadoLimpio.Equals(
                    "Vigente",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Vigente";
            }

            if (estadoLimpio.Equals(
                    "Cancelado",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Cancelado";
            }

            if (estadoLimpio.Contains(
                    "no encontrado",
                    StringComparison.OrdinalIgnoreCase) ||
                estadoLimpio.Contains(
                    "no se encuentra",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "No Encontrado";
            }

            return estadoLimpio;
        }

        string codigo =
            (codigoEstatus ?? "")
                .Trim();

        if (codigo.Contains(
                "no se encuentra",
                StringComparison.OrdinalIgnoreCase) ||
            codigo.Contains(
                "no encontrado",
                StringComparison.OrdinalIgnoreCase))
        {
            return "No Encontrado";
        }

        if (codigo.Contains(
                "satisfactoriamente",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Vigente";
        }

        return "Desconocido";
    }

    private static bool ValoresVacios(
        params string[] valores)
    {
        return valores.All(
            string.IsNullOrWhiteSpace);
    }

    private static EstadoSatDto CrearError(
        string codigo,
        string mensaje)
    {
        return new EstadoSatDto
        {
            CodigoEstatus =
                codigo,

            Estado =
                "ERROR",

            EsCancelable =
                "",

            EstatusCancelacion =
                "",

            ValidacionEfos =
                "",

            FechaConsulta =
                DateTime.Now,

            ConsultaExitosa =
                false,

            MensajeError =
                mensaje
        };
    }

    private static void ValidarDatosConsulta(
        string uuid,
        string rfcEmisor,
        string rfcReceptor)
    {
        if (string.IsNullOrWhiteSpace(
                uuid))
        {
            throw new ArgumentException(
                "El UUID es obligatorio.",
                nameof(uuid));
        }

        if (string.IsNullOrWhiteSpace(
                rfcEmisor))
        {
            throw new ArgumentException(
                "El RFC del emisor es obligatorio.",
                nameof(rfcEmisor));
        }

        if (string.IsNullOrWhiteSpace(
                rfcReceptor))
        {
            throw new ArgumentException(
                "El RFC del receptor es obligatorio.",
                nameof(rfcReceptor));
        }
    }

    private static string LimitarTexto(
        string? texto,
        int longitudMaxima)
    {
        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return "";
        }

        string resultado =
            texto.Trim();

        return resultado.Length <= longitudMaxima
            ? resultado
            : resultado[..longitudMaxima];
    }
}
