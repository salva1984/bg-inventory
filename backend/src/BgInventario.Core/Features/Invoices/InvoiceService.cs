using System.Globalization;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Dashboard;
using BgInventario.Core.Features.Products;
using BgInventario.Core.Features.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Invoices;

/// <summary>
/// Receiving stock from supplier invoices, in two steps:
///
/// - Analyze: reads the invoice (whatever its layout) and proposes a product for each line.
///   Nothing is saved: a wrong association is an inventory integrity problem, so the user reviews first.
/// - Confirm: with the products chosen by the user, registers the stock, remembers how the
///   supplier names each product (so the next invoice is recognized without guessing) and keeps
///   a record of the invoice with its file for the history.
///
/// An invoice already registered is rejected unless the user explicitly asks to register it again
/// (the demo reuses the same files): then its stock is added to the same lots and the history
/// keeps one record per registration.
/// </summary>
public sealed class InvoiceService(
    IAppDbContext db,
    IInvoiceExtractor extractor,
    InvoiceLineMatcher matcher,
    TimeProvider clock,
    DashboardCache dashboardCache)
{
    // Difference allowed between the sum of the lines and the subtotal: rounding of each line.
    internal const decimal SubtotalTolerance = 0.05m;

    // Largest values a confirmed line accepts (see ConfirmInvoiceLineRequest): the stock is an int
    // and the price has the lot's limit. What the model reads is checked against the same limits.
    private const decimal MaxQuantity = int.MaxValue;
    private static readonly decimal MaxUnitPrice = (decimal)InventoryLotLimits.MaxPrice;

    // From this similarity on, two supplier names are the same company: the invoice may write it with
    // or without accents, punctuation or the legal suffix ("CÍA. LTDA." and "Cia Ltda").
    internal const double SameSupplierMinSimilarity = 0.6;

    public async Task<Result<InvoiceAnalysis>> AnalyzeAsync(int supplierId, byte[] content, CancellationToken ct)
    {
        var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct);
        if (!supplierExists)
        {
            return InvoiceErrors.UnknownSupplier;
        }

        var fileType = InvoiceDocument.DetectType(content);
        if (fileType is null)
        {
            return InvoiceErrors.UnsupportedFile;
        }

        var extraction = await extractor.ExtractAsync(new InvoiceDocument(content, fileType.Value), ct);
        if (!extraction.IsSuccess)
        {
            return extraction.Error!;
        }

        var invoice = extraction.Value;
        if (invoice.Lines.Count == 0)
        {
            return InvoiceErrors.NoLines;
        }

        var (issuerCheck, suggestedSupplier) = await CheckIssuerAsync(supplierId, invoice.SupplierName, ct);
        var matches = await matcher.MatchAsync(supplierId, invoice.Lines, ct);
        var lines = invoice.Lines
            .Select((line, index) => new AnalyzedInvoiceLine(index + 1, line, matches[index]))
            .ToList();

        List<string> warnings = [.. invoice.Warnings, .. ReviewAmounts(invoice)];
        if (await IsRegisteredAsync(supplierId, invoice.InvoiceNumber, ct))
        {
            warnings.Add("Esta factura ya fue registrada para el proveedor; si la confirmas de nuevo, se pedirá confirmación y su stock se sumará al ya registrado.");
        }

        return new InvoiceAnalysis(
            supplierId,
            invoice.InvoiceNumber,
            invoice.IssueDate,
            invoice.SupplierName,
            issuerCheck,
            suggestedSupplier,
            invoice.Subtotal,
            invoice.Total,
            lines,
            warnings);
    }

    /// <summary>
    /// Compares the issuer read from the invoice with the chosen supplier. It never changes the choice:
    /// it only reports the mismatch and, if the issuer is another registered supplier, which one.
    ///
    /// The issuer is the registered supplier whose name is most similar, not just any name above the
    /// threshold: "Distribuidora Andina S.A." and "Distribuidora del Pacífico S.A." share most words,
    /// so a fixed threshold alone would take one for the other.
    /// </summary>
    private async Task<(IssuerCheck Check, SupplierLookup? Suggested)> CheckIssuerAsync(
        int supplierId, string? issuerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(issuerName))
        {
            return (IssuerCheck.NotRead, null);
        }

        // Suppliers are a short list (the lookup endpoint already returns all of them), so their
        // names are compared in memory.
        var suppliers = await db.Suppliers.AsNoTracking()
            .Select(s => new SupplierLookup(s.Id, s.Name))
            .ToListAsync(ct);

        // On a tie the chosen supplier wins: the user's choice is only questioned with a reason.
        var mostSimilar = suppliers
            .Select(s => (Supplier: s, Similarity: ProductSearchIndex.Similarity(s.Name, issuerName)))
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenByDescending(candidate => candidate.Supplier.Id == supplierId)
            .First();

        var isRegistered = mostSimilar.Similarity >= SameSupplierMinSimilarity;
        if (!isRegistered)
        {
            return (IssuerCheck.NotRegistered, null);
        }

        var isChosenSupplier = mostSimilar.Supplier.Id == supplierId;
        return isChosenSupplier
            ? (IssuerCheck.Matches, null)
            : (IssuerCheck.OtherSupplier, mostSimilar.Supplier);
    }

    /// <summary>
    /// Registers the received stock in one lot per product, numbered with the invoice number,
    /// saves the confirmed equivalences and records the invoice (file and lines) for the history.
    /// Everything goes in a single SaveChanges, so it is atomic: either the whole invoice is
    /// registered or nothing is.
    /// </summary>
    public async Task<Result<ConfirmedInvoice>> ConfirmAsync(
        ConfirmInvoiceRequest request, int userId, CancellationToken ct)
    {
        // Each line fits in an int, but their sum may not: the lots' stock and the units received
        // are ints. Summed as long so the check itself cannot overflow.
        var totalUnits = request.Lines.Sum(line => (long)line.Quantity);
        var exceedsStockLimit = totalUnits > int.MaxValue;
        if (exceedsStockLimit)
        {
            return InvoiceErrors.QuantityTooLarge;
        }

        // The same checks as the analysis: the file kept in the history must be the invoice itself.
        var file = request.File;
        var exceedsFileLimit = file.Content.Length > InvoiceReceiptLimits.MaxFileBytes;
        if (exceedsFileLimit)
        {
            return InvoiceErrors.FileTooLarge;
        }

        var fileType = InvoiceDocument.DetectType(file.Content);
        if (fileType is null)
        {
            return InvoiceErrors.UnsupportedFile;
        }

        var supplierName = await db.Suppliers
            .Where(s => s.Id == request.SupplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);
        if (supplierName is null)
        {
            return InvoiceErrors.UnknownSupplier;
        }

        // The invoice number is the lot number. Registering it again needs the user's explicit
        // consent; the frontend asks after receiving this error.
        var lotNumber = ProductService.NormalizeLot(request.InvoiceNumber);
        var isRegistered = await IsRegisteredAsync(request.SupplierId, lotNumber, ct);
        var registersAgainWithoutConsent = isRegistered && !request.RegisterAgain;
        if (registersAgainWithoutConsent)
        {
            return InvoiceErrors.AlreadyRegistered;
        }

        var productIds = request.Lines.Select(line => line.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        if (products.Count != productIds.Count)
        {
            return InvoiceErrors.UnknownProduct;
        }

        // A product already in a lot with this number (the invoice registered again, or a lot entered
        // by hand) gets the stock added there: the lot key is product + supplier + lot number.
        var receivedStock = SumByProduct(request.Lines);
        var existingLots = await db.InventoryLots
            .Where(lot => lot.SupplierId == request.SupplierId && lot.LotNumber == lotNumber && productIds.Contains(lot.ProductId))
            .ToDictionaryAsync(lot => lot.ProductId, ct);

        var overflowsLot = receivedStock.Any(received =>
            existingLots.TryGetValue(received.ProductId, out var lot) && (long)lot.Stock + received.Quantity > int.MaxValue);
        if (overflowsLot)
        {
            return InvoiceErrors.QuantityTooLarge;
        }

        AddToLots(request.SupplierId, lotNumber, receivedStock, existingLots);

        // Receiving stock changes the product, even if none of its own columns change.
        var now = clock.GetUtcNow().UtcDateTime;
        products.ForEach(product => product.UpdatedAt = now);

        var aliasesSaved = await SaveAliasesAsync(request.SupplierId, request.Lines, ct);

        var document = new InvoiceDocument(file.Content, fileType.Value);
        var receipt = ToReceipt(request, supplierName, lotNumber, document, products, userId);
        db.InvoiceReceipts.Add(receipt);

        await db.SaveChangesAsync(ct);
        await dashboardCache.InvalidateAsync(ct);

        var unitsReceived = (int)totalUnits;
        return new ConfirmedInvoice(receipt.Id, lotNumber, receivedStock.Count, unitsReceived, aliasesSaved);
    }

    /// <summary>
    /// The history record of the invoice: one line per confirmed line (not merged by product like
    /// the lots), with the description as the invoice wrote it and the product it was mapped to.
    /// </summary>
    private static InvoiceReceipt ToReceipt(
        ConfirmInvoiceRequest request,
        string supplierName,
        string invoiceNumber,
        InvoiceDocument document,
        IReadOnlyList<Product> products,
        int userId)
    {
        var productsById = products.ToDictionary(product => product.Id);

        var lines = request.Lines
            .Select((line, index) =>
            {
                var product = productsById[line.ProductId];
                // As the invoice wrote it; the alias keeps the normalized code for matching.
                var itemCode = string.IsNullOrWhiteSpace(line.SupplierItemCode) ? null : line.SupplierItemCode.Trim();
                return new InvoiceReceiptLine
                {
                    LineNumber = index + 1,
                    Description = line.Description.Trim(),
                    SupplierItemCode = itemCode,
                    ProductId = product.Id,
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    ProductCreated = line.ProductCreated,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                };
            })
            .ToList();

        var amount = request.Lines.Sum(line => line.Quantity * line.UnitPrice);
        var subtotal = Math.Round(amount, InventoryLotLimits.PriceScale, MidpointRounding.AwayFromZero);

        return new InvoiceReceipt
        {
            SupplierId = request.SupplierId,
            SupplierName = supplierName,
            InvoiceNumber = invoiceNumber,
            IssueDate = request.IssueDate,
            Subtotal = subtotal,
            InvoiceTotal = request.InvoiceTotal,
            RegisteredByUserId = userId,
            FileName = request.File.FileName.Trim(),
            FileContentType = document.MediaType,
            FileContent = document.Content,
            Lines = lines
        };
    }

    /// <summary>Units and amount received of one product, summed over the invoice lines.</summary>
    private sealed record ReceivedStock(int ProductId, int Quantity, decimal Amount);

    /// <summary>
    /// One entry per product. An invoice can list the same product in two lines (e.g. two prices
    /// or a promotion); they are summed so the product's lot gets a single average price.
    /// </summary>
    private static List<ReceivedStock> SumByProduct(IReadOnlyList<ConfirmInvoiceLineRequest> lines) =>
        lines
            .GroupBy(line => line.ProductId)
            .Select(group => new ReceivedStock(
                group.Key,
                group.Sum(line => line.Quantity),
                group.Sum(line => line.Quantity * line.UnitPrice)))
            .ToList();

    /// <summary>
    /// Puts the received stock in the product's lot. A new lot takes the average price of what was
    /// received; an existing one adds the units and its price becomes the average weighted by
    /// quantity of what it had and what arrived.
    /// </summary>
    private void AddToLots(
        int supplierId,
        string lotNumber,
        IReadOnlyList<ReceivedStock> receivedStock,
        IReadOnlyDictionary<int, InventoryLot> existingLots)
    {
        foreach (var received in receivedStock)
        {
            if (existingLots.TryGetValue(received.ProductId, out var lot))
            {
                var stock = lot.Stock + received.Quantity;
                var amount = lot.Stock * lot.Price + received.Amount;
                lot.Stock = stock;
                lot.Price = RoundPrice(amount / stock);
                continue;
            }

            db.InventoryLots.Add(new InventoryLot
            {
                ProductId = received.ProductId,
                SupplierId = supplierId,
                LotNumber = lotNumber,
                Price = RoundPrice(received.Amount / received.Quantity),
                Stock = received.Quantity
            });
        }
    }

    private static decimal RoundPrice(decimal price) =>
        Math.Round(price, InventoryLotLimits.PriceScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Creates or updates the supplier's alias for each confirmed description. If the user chose a
    /// different product than the stored alias, the alias is corrected: the last confirmation wins.
    /// Returns how many aliases were saved.
    /// </summary>
    private async Task<int> SaveAliasesAsync(
        int supplierId, IReadOnlyList<ConfirmInvoiceLineRequest> lines, CancellationToken ct)
    {
        // One alias per normalized description; a description made only of symbols cannot be matched again.
        var confirmedByDescription = lines
            .Select(line => (Line: line, Key: ProductSearchIndex.Normalize(line.Description)))
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.Last().Line);

        var keys = confirmedByDescription.Keys.ToList();
        // Without the query filter: an alias of a product deleted later is hidden by it but still
        // holds the unique (supplier, description) key, so it must be reused, not duplicated.
        var existingByKey = await db.SupplierProductAliases
            .IgnoreQueryFilters()
            .Where(a => a.SupplierId == supplierId && keys.Contains(a.NormalizedDescription))
            .ToDictionaryAsync(a => a.NormalizedDescription, ct);

        foreach (var (key, line) in confirmedByDescription)
        {
            var description = line.Description.Trim();
            var itemCode = InvoiceLineMatcher.NormalizeItemCode(line.SupplierItemCode);

            if (existingByKey.TryGetValue(key, out var alias))
            {
                alias.ProductId = line.ProductId;
                alias.Description = description;
                alias.SupplierItemCode = itemCode ?? alias.SupplierItemCode;
                continue;
            }

            db.SupplierProductAliases.Add(new SupplierProductAlias
            {
                SupplierId = supplierId,
                ProductId = line.ProductId,
                Description = description,
                NormalizedDescription = key,
                SupplierItemCode = itemCode
            });
        }

        return confirmedByDescription.Count;
    }

    private async Task<bool> IsRegisteredAsync(int supplierId, string? invoiceNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            return false;
        }

        var lotNumber = ProductService.NormalizeLot(invoiceNumber);
        var isInHistory = await db.InvoiceReceipts
            .AnyAsync(r => r.SupplierId == supplierId && r.InvoiceNumber == lotNumber, ct);
        if (isInHistory)
        {
            return true;
        }

        // Without the query filter: the lots of products deleted later still mean the invoice was
        // registered. The lots also cover invoices confirmed before the history existed and lots
        // entered by hand with that number.
        return await db.InventoryLots
            .IgnoreQueryFilters()
            .AnyAsync(lot => lot.SupplierId == supplierId && lot.LotNumber == lotNumber, ct);
    }

    /// <summary>
    /// Checks that do not need the database: what the user should look at before confirming.
    /// They are warnings, not errors, because the user can fix the values in the review.
    /// </summary>
    internal static List<string> ReviewAmounts(ExtractedInvoice invoice)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            warnings.Add("No se encontró el número de factura; ingréselo antes de confirmar.");
        }

        for (var index = 0; index < invoice.Lines.Count; index++)
        {
            var line = invoice.Lines[index];
            var lineNumber = index + 1;

            if (line.Quantity <= 0)
            {
                warnings.Add($"Línea {lineNumber}: la cantidad debe ser mayor que cero.");
            }
            else if (line.Quantity > MaxQuantity)
            {
                warnings.Add($"Línea {lineNumber}: la cantidad {Format(line.Quantity)} supera el máximo permitido.");
            }
            else if (line.Quantity != decimal.Truncate(line.Quantity))
            {
                warnings.Add($"Línea {lineNumber}: la cantidad {Format(line.Quantity)} no es un número entero de unidades.");
            }

            if (line.UnitPrice <= 0)
            {
                warnings.Add($"Línea {lineNumber}: el precio unitario debe ser mayor que cero.");
            }
            else if (line.UnitPrice > MaxUnitPrice)
            {
                warnings.Add($"Línea {lineNumber}: el precio unitario {Format(line.UnitPrice)} supera el máximo permitido.");
            }
        }

        // The subtotal is printed on the invoice; if the lines do not add up to it, something was misread.
        // The model can answer any number, so the sum is only computed when every line is within the
        // limits: then no product or sum can overflow a decimal. A line out of limits already has its warning.
        var linesAreWithinLimits = invoice.Lines.All(IsWithinLimits);
        if (invoice.Subtotal is { } subtotal && linesAreWithinLimits)
        {
            var linesTotal = invoice.Lines.Sum(line => line.Quantity * line.UnitPrice);
            // A negative subtotal is a misreading; checked first so the difference of two
            // non-negative values is the only one computed (it cannot overflow).
            var exceedsTolerance = subtotal < 0 || Math.Abs(linesTotal - subtotal) > SubtotalTolerance;
            if (exceedsTolerance)
            {
                warnings.Add($"La suma de las líneas ({Format(linesTotal)}) no coincide con el subtotal de la factura ({Format(subtotal)}).");
            }
        }

        return warnings;
    }

    private static bool IsWithinLimits(ExtractedInvoiceLine line) =>
        line.Quantity is > 0 and <= MaxQuantity
        && line.UnitPrice > 0
        && line.UnitPrice <= MaxUnitPrice;

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
