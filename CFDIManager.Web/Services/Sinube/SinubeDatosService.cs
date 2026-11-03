using System.Text;
using CFDIParser.Models;

namespace CFDIManager.Web.Services.Sinube;

/// <summary>
/// Consultas de solo lectura a la base de SINUBE por su servicio getpost, con el protocolo de
/// HerramientasSinube 13.9 (ClsSiNubeGetPost.ejecutarConsulta + ClsBlobReader, analizado con REA):
/// POST form-urlencoded "&amp;tipo=3&amp;emp=&amp;suc=&amp;usu=&amp;pas=&amp;cns=" donde cns es un SELECT sobre
/// entidades Db* (p. ej. DbFacturaProveedorDeXML) con TAMPAG/CURSOR para paginar.
/// Ver Documentacion\Conciliacion_SINUBE_vs_SAT.md.
/// </summary>
public class SinubeDatosService(SinubeService sinube, IHttpClientFactory http)
{
    public const string ClienteHttp = "SinubeDatos";

    private const string UrlProduccion = "http://getpost.si-nube.appspot.com/getpost";
    private const string UrlPruebas = "http://getpost.facturanube.appspot.com/getpost";

    private const string Nulo = "&NullSiNube;";
    private const char SeparadorFilas = '¬';
    private const int TamanoPagina = 1000;

    // Escapes del lector del portal (rOr): primero el nivel 1, luego los niveles 2→1 y 3→2.
    private static readonly (string Escape, string Valor)[] Escapes =
    [
        ("&PipeSiNube;", "|"), ("&EnterSiNube;", "\n"), ("&SeparadorSiNube;", "¬"),
        ("&DiagonalSiNube;", "/"), ("&MasSiNube;", "+"), ("&IgualSiNube;", "="),
        ("&Pipe2SiNube;", "&PipeSiNube;"), ("&Enter2SiNube;", "&EnterSiNube;"), ("&Separador2SiNube;", "&SeparadorSiNube;"),
        ("&Diagonal2SiNube;", "&DiagonalSiNube;"), ("&Mas2SiNube;", "&MasSiNube;"), ("&Igual2SiNube;", "&IgualSiNube;"),
        ("&Pipe3SiNube;", "&Pipe2SiNube;"), ("&Enter3SiNube;", "&Enter2SiNube;"), ("&Separador3SiNube;", "&Separador2SiNube;"),
        ("&Diagonal3SiNube;", "&Diagonal2SiNube;"), ("&Mas3SiNube;", "&Mas2SiNube;"), ("&Igual3SiNube;", "&Igual2SiNube;")
    ];

    /// <summary>
    /// UUID de las facturas de clientes (DbFactura) que ya están en SINUBE en esos meses (por fecha de factura).
    /// Una consulta por mes: así no se manda uno por uno lo que SINUBE ya tiene.
    /// </summary>
    public Task<HashSet<string>> UuidsFacturasClientesAsync(
        SinubeConfiguracionDto configuracion,
        IEnumerable<DateTime> meses,
        CancellationToken cancelacion = default) =>
        UuidsEnSinubeAsync(configuracion, clientes: true, meses, cancelacion);

    /// <summary>
    /// UUID que SINUBE ya tiene en esos meses: facturas de clientes (DbFactura) o de proveedores
    /// (DbFacturaProveedorDeXML), las mismas entidades que la Conciliación.
    /// </summary>
    public async Task<HashSet<string>> UuidsEnSinubeAsync(
        SinubeConfiguracionDto configuracion,
        bool clientes,
        IEnumerable<DateTime> meses,
        CancellationToken cancelacion = default)
    {
        var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string empresa = configuracion.Empresa.Replace("'", "");
        (string entidad, string campo) = clientes ? ("DbFactura", "uuid") : ("DbFacturaProveedorDeXML", "UUID");

        foreach (int periodo in meses.Select(m => m.Year * 100 + m.Month).Distinct())
        {
            List<FilaSinube> filas = await ConsultarAsync(
                configuracion, $"SELECT {campo} FROM {entidad} WHERE empresa = '{empresa}' AND mes = {periodo}", cancelacion);

            foreach (FilaSinube fila in filas)
            {
                if (fila.Texto(campo) is { Length: > 0 } uuid)
                    uuids.Add(uuid.Trim());
            }
        }

        return uuids;
    }

    /// <summary>
    /// UUID de los complementos de pago de clientes de esos meses. SINUBE guarda cada XML de pago en
    /// DbRecepcionPagosXML ("Consulta de CFDI Complemento Recepción Pago", mes de emisión) con la serie con la que se
    /// subió; los depósitos (DbDeposito, mes de pago) casi nunca traen el UUID. Se juntan ambos.
    /// </summary>
    public async Task<HashSet<string>> UuidsDepositosAsync(
        SinubeConfiguracionDto configuracion, IEnumerable<DateTime> meses, CancellationToken cancelacion = default)
    {
        string empresa = configuracion.Empresa.Replace("'", "");
        var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (int periodo in meses.Select(m => m.Year * 100 + m.Month).Distinct())
        {
            foreach (string entidad in new[] { "DbRecepcionPagosXML", "DbDeposito" })
            {
                List<FilaSinube> filas = await ConsultarAsync(
                    configuracion, $"SELECT uuid FROM {entidad} WHERE empresa = '{empresa}' AND mes = {periodo}", cancelacion);

                foreach (FilaSinube fila in filas)
                {
                    if (fila.Texto("uuid") is { Length: > 0 } uuid)
                        uuids.Add(uuid.Trim());
                }
            }
        }

        return uuids;
    }

    /// <summary>
    /// Complementos de pago de clientes que SINUBE ya tiene (DbRecepcionPagosXML) con esas series, de cualquier mes.
    /// SINUBE rechaza un pago cuyo serie-folio ya ocupa otro, aunque el UUID sea distinto.
    /// </summary>
    public async Task<List<PagoSinube>> PagosClientesAsync(
        SinubeConfiguracionDto configuracion, IEnumerable<string> series, CancellationToken cancelacion = default)
    {
        string empresa = configuracion.Empresa.Replace("'", "");
        var pagos = new List<PagoSinube>();

        foreach (string serie in series.Select(s => s.Trim().ToUpperInvariant()).Where(s => s != "").Distinct())
        {
            List<FilaSinube> filas = await ConsultarAsync(configuracion,
                $"SELECT uuid, serieRecepcionPago, folioRecepcionPago FROM DbRecepcionPagosXML " +
                $"WHERE empresa = '{empresa}' AND serieRecepcionPago = '{serie.Replace("'", "")}'", cancelacion);

            foreach (FilaSinube fila in filas)
            {
                if (fila.Entero("folioRecepcionPago") is long folio)
                    pagos.Add(new PagoSinube((fila.Texto("uuid") ?? "").Trim().ToUpperInvariant(), serie, folio));
            }
        }

        return pagos;
    }

    /// <summary>
    /// Ejecuta un SELECT y regresa todas las filas (sigue el CURSOR hasta la última página).
    /// La consulta no debe incluir TAMPAG/CURSOR/NOADMINSINUBE: se agregan aquí.
    /// </summary>
    public async Task<List<FilaSinube>> ConsultarAsync(
        SinubeConfiguracionDto configuracion,
        string consulta,
        CancellationToken cancelacion = default)
    {
        if (SinubeService.FaltanDatos(configuracion, requierePassword: true) is string falta)
            throw new InvalidOperationException(falta);

        if (string.IsNullOrWhiteSpace(configuracion.Usuario))
            throw new InvalidOperationException("Captura el usuario de SINUBE (el del portal) en Importación a SINUBE → Acceso.");

        // getpost solo tiene producción y facturanube (ClsSiNubeGetPost: ambiente 2 / 1).
        string url = configuracion.Ambiente == SinubeAmbientes.PruebasFacturanube ? UrlPruebas : UrlProduccion;
        string password = sinube.Password(configuracion);
        HttpClient cliente = http.CreateClient(ClienteHttp);

        var filas = new List<FilaSinube>();
        string? cursor = null;

        do
        {
            string cns = $"{consulta} TAMPAG {TamanoPagina}{(cursor is null ? "" : " CURSOR " + cursor)} NOADMINSINUBE";
            string cuerpo =
                $"&tipo=3&emp={Escapar(configuracion.Empresa)}&suc={Escapar(configuracion.Sucursal)}" +
                $"&usu={Escapar(configuracion.Usuario)}&pas={Escapar(password)}&cns={Escapar(cns)}";

            using HttpResponseMessage respuesta = await cliente.PostAsync(
                url, new StringContent(cuerpo, Encoding.UTF8, "application/x-www-form-urlencoded"), cancelacion);

            string texto = await respuesta.Content.ReadAsStringAsync(cancelacion);

            if (texto.StartsWith("Error:", StringComparison.Ordinal))
                throw new InvalidOperationException("SINUBE: " + texto["Error:".Length..].Trim());

            if (!respuesta.IsSuccessStatusCode)
                throw new InvalidOperationException($"SINUBE respondió HTTP {(int)respuesta.StatusCode}.");

            cursor = LeerPagina(texto, filas);
        }
        while (cursor is not null);

        return filas;
    }

    /// <summary>
    /// Agrega las filas de una página y regresa el cursor de la siguiente (null si es la última).
    /// Formato (ClsBlobReader): filas separadas por '¬'; la primera es
    /// "cantidad|cursor|campo1|tipo1|campo2|tipo2…" y las demás traen los valores separados por '|'.
    /// </summary>
    internal static string? LeerPagina(string texto, List<FilaSinube> filas)
    {
        string[] renglones = texto.Trim().Split(SeparadorFilas);
        string[] encabezado = renglones[0].Split('|');

        if (encabezado.Length < 2)
            throw new InvalidOperationException("SINUBE devolvió una respuesta con formato desconocido.");

        var campos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 2; i + 1 < encabezado.Length; i += 2)
            campos[encabezado[i]] = (i - 2) / 2;

        foreach (string renglon in renglones.Skip(1))
        {
            if (renglon.Length > 0)
                filas.Add(new FilaSinube(campos, renglon.Split('|')));
        }

        // Sin filas en la página no hay más que pedir aunque venga un cursor.
        string cursor = encabezado[1];
        return cursor == Nulo || string.IsNullOrWhiteSpace(cursor) || renglones.Length == 1 ? null : cursor;
    }

    internal static string? Decodificar(string valor)
    {
        if (valor == Nulo)
            return null;

        foreach ((string escape, string real) in Escapes)
            valor = valor.Replace(escape, real);

        return valor;
    }

    // Igual que ClsSiNubeGetPost.sustituirEscapes: solo '&' rompe el form-urlencoded de SINUBE.
    private static string Escapar(string? valor) => (valor ?? "").Replace("&", "%26");
}

/// <summary>Complemento de pago registrado en SINUBE (DbRecepcionPagosXML).</summary>
public sealed record PagoSinube(string Uuid, string Serie, long Folio)
{
    public string Llave => SerieFolio(Serie, Folio.ToString());

    /// <summary>"SERIE|FOLIO" en mayúsculas y sin ceros a la izquierda en el folio numérico.</summary>
    public static string SerieFolio(string? serie, string? folio)
    {
        string f = (folio ?? "").Trim();
        return $"{(serie ?? "").Trim().ToUpperInvariant()}|{(long.TryParse(f, out long n) ? n.ToString() : f.ToUpperInvariant())}";
    }
}

/// <summary>Una fila de una consulta a SINUBE; los valores se leen por nombre de campo.</summary>
public sealed class FilaSinube(IReadOnlyDictionary<string, int> campos, string[] valores)
{
    public string? Texto(string campo) =>
        campos.TryGetValue(campo, out int i) && i < valores.Length ? SinubeDatosService.Decodificar(valores[i]) : null;

    public long? Entero(string campo) =>
        long.TryParse(Texto(campo), out long n) ? n : null;

    public decimal? Decimal(string campo) =>
        decimal.TryParse(Texto(campo), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal d) ? d : null;

    public bool Booleano(string campo) =>
        string.Equals(Texto(campo), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Las fechas vienen en milisegundos desde 1970 (UTC); se regresan en hora local.</summary>
    public DateTime? Fecha(string campo) =>
        Entero(campo) is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime : null;
}
