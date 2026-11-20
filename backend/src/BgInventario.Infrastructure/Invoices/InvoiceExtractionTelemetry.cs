namespace BgInventario.Infrastructure.Invoices;

/// <summary>Name of the OpenTelemetry source of the LLM calls; the API subscribes to it for traces.</summary>
public static class InvoiceExtractionTelemetry
{
    public const string SourceName = "BgInventario.InvoiceExtraction";
}
