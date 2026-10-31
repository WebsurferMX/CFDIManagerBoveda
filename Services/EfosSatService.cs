using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CFDIManager.Web.Services.Interfaces;
using CFDIParser.Models;
using CFDIParser.Services;

namespace CFDIManager.Web.Services;

/// <summary>
/// Carga el Listado completo del artículo 69-B del CFF que publica el SAT
/// (presuntos, definitivos, desvirtuados y sentencias favorables, con sus
/// fechas de publicación en el portal del SAT y en el DOF).
/// </summary>
public partial class EfosSatService : IEfosSatService
{
    public const string UrlListadoCompleto =
        "http://omawww.sat.gob.mx/cifras_sat/Documents/Listado_Completo_69-B.csv";

    private static readonly Dictionary<string, string> Situaciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Presunto"] = "PRESUNTO",
        ["Definitivo"] = "DEFINITIVO",
        ["Desvirtuado"] = "DESVIRTUADO",
        ["Sentencia Favorable"] = "FAVORABLE"
    };

    private static readonly string[] Meses =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private readonly PostgreSqlRepository _repository;
    private readonly IHttpClientFactory _http;

    public EfosSatService(
        PostgreSqlRepository repository,
        IHttpClientFactory http)
    {
        _repository = repository;
        _http = http;
    }

    public async Task<ResultadoImportacionEfosDto> DescargarDelSatAsync(
        CancellationToken cancelacion = default)
    {
        HttpClient cliente = _http.CreateClient("SatListados");
        byte[] contenido = await cliente.GetByteArrayAsync(UrlListadoCompleto, cancelacion);
        return Importar(contenido, "Descarga del portal del SAT");
    }

    public async Task<ResultadoImportacionEfosDto> ImportarAsync(
        Stream archivo,
        string nombreArchivo,
        CancellationToken cancelacion = default)
    {
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, cancelacion);
        return Importar(memoria.ToArray(), $"Archivo {nombreArchivo}");
    }

    private ResultadoImportacionEfosDto Importar(
        byte[] contenido,
        string origen)
    {
        (List<EfosRegistroDto> registros, ResultadoImportacionEfosDto resultado) = LeerListado(contenido);

        _repository.ReemplazarListadoEfos(registros, resultado.FechaCorte, origen);

        return resultado;
    }

    /// <summary>Interpreta el CSV del Listado completo 69-B sin tocar la base de datos.</summary>
    public static (List<EfosRegistroDto> Registros, ResultadoImportacionEfosDto Resultado) LeerListado(
        byte[] contenido)
    {
        var resultado = new ResultadoImportacionEfosDto();
        List<string[]> renglones = LeerCsv(Decodificar(contenido));

        int encabezado = renglones.FindIndex(r =>
            r.Length > 3 &&
            r[1].Trim().Equals("RFC", StringComparison.OrdinalIgnoreCase) &&
            r[3].Contains("Situaci", StringComparison.OrdinalIgnoreCase));

        if (encabezado < 0)
        {
            throw new InvalidOperationException(
                "El archivo no tiene el formato del Listado completo del artículo 69-B del SAT " +
                "(no se encontró el encabezado No, RFC, Nombre del Contribuyente, Situación…).");
        }

        resultado.FechaCorte = LeerFechaCorte(renglones.Take(encabezado).SelectMany(r => r));

        // Un RFC puede aparecer en varios renglones: cada uno es un procedimiento distinto
        // (p. ej. una sentencia favorable de 2017 y un definitivo de 2020). Se guardan todos.
        var registros = new List<EfosRegistroDto>();

        foreach (string[] r in renglones.Skip(encabezado + 1))
        {
            string Col(int i) => i < r.Length ? r[i].Trim() : "";

            if (Col(1) == "")
            {
                continue;
            }

            resultado.RegistrosLeidos++;

            // Datos suprimidos por orden judicial (RFC XXXXXXXXXXXX): no hay RFC que cruzar.
            if (!RfcValido().IsMatch(Col(1).ToUpperInvariant()))
            {
                resultado.Suprimidos++;
                continue;
            }

            if (!Situaciones.TryGetValue(Col(3), out string? situacion))
            {
                resultado.Errores++;
                continue;
            }

            registros.Add(new EfosRegistroDto
            {
                Rfc = Col(1).ToUpperInvariant(),
                RazonSocial = Col(2),
                Situacion = situacion,
                OficioPresuncion = Col(4),
                PublicacionPresuntoSat = Fecha(Col(5)),
                PublicacionPresuntoDof = Fecha(Col(7)),
                PublicacionDesvirtuadoDof = Fecha(Col(11)),
                OficioDefinitivo = Col(12),
                PublicacionDefinitivoSat = Fecha(Col(13)),
                PublicacionDefinitivoDof = Fecha(Col(15)),
                PublicacionFavorableDof = Fecha(Col(19))
            });
        }

        if (registros.Count == 0)
        {
            throw new InvalidOperationException("El archivo no contiene contribuyentes válidos.");
        }

        // Un registro por RFC: el procedimiento más grave y, entre iguales, el más reciente.
        List<EfosRegistroDto> porRfc = registros
            .GroupBy(x => x.Rfc)
            .Select(g =>
            {
                EfosRegistroDto e = g
                    .OrderBy(x => Gravedad(x.Situacion))
                    .ThenByDescending(x => x.PublicacionDefinitivoDof ?? x.PublicacionDefinitivoSat ?? x.PublicacionFavorableDof
                                           ?? x.PublicacionDesvirtuadoDof ?? x.PublicacionPresuntoDof ?? x.PublicacionPresuntoSat)
                    .First();
                e.Procedimientos = g.Count();
                return e;
            })
            .ToList();

        resultado.Procedimientos = registros.Count;
        resultado.Insertados = porRfc.Count;
        resultado.PorSituacion = porRfc.GroupBy(x => x.Situacion).ToDictionary(g => g.Key, g => g.Count());

        return (porRfc, resultado);
    }

    /// <summary>Orden de gravedad para elegir la situación efectiva de un RFC con varios procedimientos.</summary>
    public static int Gravedad(string situacion) => situacion switch
    {
        "DEFINITIVO" => 0,
        "PRESUNTO" => 1,
        "DESVIRTUADO" => 2,
        _ => 3
    };

    [GeneratedRegex("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$")]
    private static partial Regex RfcValido();

    /// <summary>El SAT publica el CSV en Latin-1; se acepta también UTF-8.</summary>
    private static string Decodificar(byte[] contenido)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(contenido).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(contenido);
        }
    }

    /// <summary>CSV con comillas; un campo entre comillas puede contener comas y saltos de línea.</summary>
    private static List<string[]> LeerCsv(string texto)
    {
        var renglones = new List<string[]>();
        var campos = new List<string>();
        var campo = new StringBuilder();
        bool comillas = false;

        for (int i = 0; i < texto.Length; i++)
        {
            char c = texto[i];

            if (comillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    campo.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    comillas = false;
                }
                else
                {
                    campo.Append(c);
                }
            }
            else if (c == '"')
            {
                comillas = true;
            }
            else if (c == ',')
            {
                campos.Add(campo.ToString());
                campo.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                {
                    i++;
                }

                campos.Add(campo.ToString());
                campo.Clear();
                renglones.Add(campos.ToArray());
                campos.Clear();
            }
            else
            {
                campo.Append(c);
            }
        }

        if (campo.Length > 0 || campos.Count > 0)
        {
            campos.Add(campo.ToString());
            renglones.Add(campos.ToArray());
        }

        return renglones;
    }

    private static DateTime? Fecha(string texto)
    {
        return DateTime.TryParseExact(texto, ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f)
            ? f
            : null;
    }

    [GeneratedRegex(@"actualizada al (\d{1,2}) de ([a-záéíóú]+) de (\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex FechaCorteRegex();

    /// <summary>"Información actualizada al 31 de diciembre de 2025…" en las primeras líneas.</summary>
    private static DateTime? LeerFechaCorte(IEnumerable<string> textos)
    {
        foreach (string t in textos)
        {
            Match m = FechaCorteRegex().Match(t);
            int mes = m.Success ? Array.IndexOf(Meses, m.Groups[2].Value.ToLowerInvariant()) + 1 : 0;

            if (mes > 0)
            {
                return new DateTime(int.Parse(m.Groups[3].Value), mes, int.Parse(m.Groups[1].Value));
            }
        }

        return null;
    }
}
