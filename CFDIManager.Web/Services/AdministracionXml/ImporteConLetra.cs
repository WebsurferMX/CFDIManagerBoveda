namespace CFDIManager.Web.Services.AdministracionXml;

/// <summary>
/// Importe con letra como en las representaciones impresas: "UN MIL TRESCIENTOS SESENTA PESOS 80/100 M.N.".
/// En otra moneda: "… DÓLARES 50/100 USD".
/// </summary>
public static class ImporteConLetra
{
    private static readonly string[] Unidades =
    [
        "", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE",
        "CATORCE", "QUINCE", "DIECISEIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIUN", "VEINTIDOS",
        "VEINTITRES", "VEINTICUATRO", "VEINTICINCO", "VEINTISEIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"
    ];

    private static readonly string[] Decenas = ["", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];

    private static readonly string[] Centenas =
    [
        "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS",
        "OCHOCIENTOS", "NOVECIENTOS"
    ];

    public static string Convertir(decimal importe, string? moneda)
    {
        importe = Math.Round(Math.Abs(importe), 2);
        long enteros = (long)Math.Floor(importe);
        int centavos = (int)Math.Round((importe - enteros) * 100);
        string m = string.IsNullOrWhiteSpace(moneda) ? "MXN" : moneda.Trim().ToUpperInvariant();

        string palabras = enteros == 0 ? "CERO" : Numero(enteros);
        (string nombre, string sufijo) = m switch
        {
            "MXN" => (enteros == 1 ? "PESO" : "PESOS", "M.N."),
            "USD" => (enteros == 1 ? "DÓLAR" : "DÓLARES", "USD"),
            "EUR" => ("EUROS", "EUR"),
            _ => (m, m)
        };

        // "UN MILLÓN DE PESOS": con millones exactos se agrega "DE".
        string de = enteros >= 1_000_000 && enteros % 1_000_000 == 0 ? " DE" : "";
        return $"{palabras}{de} {nombre} {centavos:00}/100 {sufijo}";
    }

    private static string Numero(long n)
    {
        if (n >= 1_000_000_000_000)
            return $"{Numero(n / 1_000_000_000_000)} BILLONES {Numero(n % 1_000_000_000_000)}".Trim();
        if (n >= 1_000_000)
        {
            long millones = n / 1_000_000;
            string texto = millones == 1 ? "UN MILLON" : $"{Numero(millones)} MILLONES";
            return $"{texto} {Numero(n % 1_000_000)}".Trim();
        }
        if (n >= 1000)
        {
            // Como las representaciones impresas del SAT: "UN MIL", "DOS MIL".
            return $"{Numero(n / 1000)} MIL {Numero(n % 1000)}".Trim();
        }
        if (n >= 100)
            return n == 100 ? "CIEN" : $"{Centenas[n / 100]} {Numero(n % 100)}".Trim();
        if (n >= 30)
            return n % 10 == 0 ? Decenas[n / 10] : $"{Decenas[n / 10]} Y {Unidades[n % 10]}";
        return Unidades[n];
    }
}
