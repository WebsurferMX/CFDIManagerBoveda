


using System.Globalization;
using CFDIParser.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CFDIManager.Web.Services;


public class CfdiPdfService
{
    public byte[] Generar(
        CfdiConsultaDto cfdi)
    {
        CultureInfo cultura =
            CultureInfo.GetCultureInfo(
                "es-MX");

        return Document
            .Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);

                    page.Margin(
                        32);

                    page.DefaultTextStyle(
                        style =>
                            style
                                .FontSize(9)
                                .FontFamily("Lato"));

                    page.Header()
                        .Element(
                            container =>
                                CrearEncabezado(
                                    container,
                                    cfdi));

                    page.Content()
                        .PaddingVertical(16)
                        .Column(column =>
                        {
                            column.Spacing(12);

                            column.Item()
                                .Element(
                                    container =>
                                        CrearClasificacion(
                                            container,
                                            cfdi));

                            column.Item()
                                .Element(
                                    container =>
                                        CrearEmisorReceptor(
                                            container,
                                            cfdi));

                            column.Item()
                                .Element(
                                    container =>
                                        CrearImportes(
                                            container,
                                            cfdi,
                                            cultura));

                            column.Item()
                                .Element(
                                    container =>
                                        CrearDatosAdicionales(
                                            container,
                                            cfdi));
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span(
                                "CFDI Manager · Página ");

                            text.CurrentPageNumber();

                            text.Span(" de ");

                            text.TotalPages();
                        });
                });
            })
            .GeneratePdf();
    }

    private static void CrearEncabezado(
        IContainer container,
        CfdiConsultaDto cfdi)
    {
        container
            .BorderBottom(2)
            .BorderColor("#2563EB")
            .PaddingBottom(12)
            .Row(row =>
            {
                row.RelativeItem()
                    .Column(column =>
                    {
                        column.Item()
                            .Text("CFDI Manager")
                            .Bold()
                            .FontSize(18)
                            .FontColor("#1D4ED8");

                        column.Item()
                            .Text(
                                ObtenerNombreTipo(
                                    cfdi.TipoCfdi))
                            .FontSize(12)
                            .FontColor("#475569");
                    });

                row.AutoItem()
                    .AlignRight()
                    .Column(column =>
                    {
                        column.Item()
                            .Text(
                                $"{cfdi.Serie} {cfdi.Folio}")
                            .Bold()
                            .FontSize(12);

                        column.Item()
                            .Text(
                                cfdi.FechaEmision
                                    .ToString(
                                        "dd/MM/yyyy HH:mm"))
                            .FontColor("#64748B");
                    });
            });
    }

    private static void CrearClasificacion(
        IContainer container,
        CfdiConsultaDto cfdi)
    {
        container
            .Background("#F1F5F9")
            .CornerRadius(6)
            .Padding(10)
            .Row(row =>
            {
                row.RelativeItem()
                    .Text(text =>
                    {
                        text.Span("UUID: ")
                            .Bold();

                        text.Span(cfdi.UUID);
                    });

                row.AutoItem()
                    .Text(cfdi.Clasificacion)
                    .Bold()
                    .FontColor(
                        cfdi.Clasificacion == "EMITIDO"
                            ? "#1D4ED8"
                            : "#6D28D9");
            });
    }

    private static void CrearEmisorReceptor(
        IContainer container,
        CfdiConsultaDto cfdi)
    {
        container.Row(row =>
        {
            row.RelativeItem()
                .Border(1)
                .BorderColor("#CBD5E1")
                .CornerRadius(6)
                .Padding(10)
                .Column(column =>
                {
                    column.Item()
                        .Text("EMISOR")
                        .Bold()
                        .FontColor("#1D4ED8");

                    column.Item()
                        .PaddingTop(6)
                        .Text(cfdi.NombreEmisor);

                    column.Item()
                        .Text(cfdi.RfcEmisor)
                        .FontColor("#64748B");
                });

            row.ConstantItem(12);

            row.RelativeItem()
                .Border(1)
                .BorderColor("#CBD5E1")
                .CornerRadius(6)
                .Padding(10)
                .Column(column =>
                {
                    column.Item()
                        .Text("RECEPTOR")
                        .Bold()
                        .FontColor("#6D28D9");

                    column.Item()
                        .PaddingTop(6)
                        .Text(cfdi.NombreReceptor);

                    column.Item()
                        .Text(cfdi.RfcReceptor)
                        .FontColor("#64748B");
                });
        });
    }

    private static void CrearImportes(
        IContainer container,
        CfdiConsultaDto cfdi,
        CultureInfo cultura)
    {
        container
            .AlignRight()
            .Width(250)
            .Table(table =>
            {
                table.ColumnsDefinition(
                    columns =>
                    {
                        columns.RelativeColumn();
                        columns.ConstantColumn(110);
                    });

                AgregarImporte(
                    table,
                    "Subtotal",
                    cfdi.Subtotal.ToString(
                        "C2",
                        cultura));

                AgregarImporte(
                    table,
                    "Total",
                    cfdi.Total.ToString(
                        "C2",
                        cultura),
                    total: true);

                table.Cell()
                    .PaddingTop(5)
                    .Text("Moneda")
                    .FontColor("#64748B");

                table.Cell()
                    .PaddingTop(5)
                    .AlignRight()
                    .Text(cfdi.Moneda)
                    .Bold();
            });
    }
    private static void AgregarImporte(
    TableDescriptor table,
    string etiqueta,
    string importe,
    bool total = false)
{
    var celdaEtiqueta =
        table.Cell()
            .PaddingVertical(5);

    if (total)
    {
        celdaEtiqueta
            .Text(etiqueta)
            .Bold()
            .FontColor("#0F172A");
    }
    else
    {
        celdaEtiqueta
            .Text(etiqueta)
            .FontColor("#64748B");
    }

    var textoImporte =
        table.Cell()
            .PaddingVertical(5)
            .AlignRight()
            .Text(importe)
            .Bold();

    if (total)
    {
        textoImporte.FontSize(13);
    }
    else
    {
        textoImporte.FontSize(9);
    }
}
    
    
    
    
    
    
    
    
    
    
    
    
    
    private static void CrearDatosAdicionales(
        IContainer container,
        CfdiConsultaDto cfdi)
    {
        container
            .BorderTop(1)
            .BorderColor("#E2E8F0")
            .PaddingTop(10)
            .Column(column =>
            {
                column.Item()
                    .Text(text =>
                    {
                        text.Span("Tipo CFDI: ")
                            .Bold();

                        text.Span(
                            $"{cfdi.TipoCfdi} · " +
                            ObtenerNombreTipo(
                                cfdi.TipoCfdi));
                    });

                column.Item()
                    .PaddingTop(4)
                    .Text(text =>
                    {
                        text.Span("Ruta XML: ")
                            .Bold();

                        text.Span(cfdi.RutaXml);
                    });
            });
    }

    private static string ObtenerNombreTipo(
        string tipo)
    {
        return tipo switch
        {
            "I" => "Factura de ingreso",
            "E" => "Nota de crédito",
            "N" => "Recibo de nómina",
            "T" => "Comprobante de traslado",
            "P" => "Complemento de pago",
            _ => "Comprobante fiscal"
        };
    }
}