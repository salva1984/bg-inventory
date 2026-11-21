using BgInventario.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Invoices;

public sealed class InvoiceReceiptService(IAppDbContext db)
{
    /// <summary>Newest first. The projection never reads the file content, only its name.</summary>
    public Task<PagedResult<InvoiceReceiptItem>> GetPagedAsync(InvoiceReceiptQuery query, CancellationToken ct)
    {
        var receipts = db.InvoiceReceipts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            receipts = receipts.Where(r => r.InvoiceNumber.Contains(term) || r.SupplierName.Contains(term));
        }

        return receipts
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new InvoiceReceiptItem(
                r.Id,
                r.InvoiceNumber,
                r.SupplierName,
                r.IssueDate,
                r.CreatedAt,
                r.RegisteredBy.FullName,
                r.Lines.Count,
                r.Subtotal,
                r.InvoiceTotal,
                r.FileName))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<Result<InvoiceReceiptDetail>> GetByIdAsync(int id, CancellationToken ct)
    {
        var receipt = await db.InvoiceReceipts.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new InvoiceReceiptDetail(
                r.Id,
                r.InvoiceNumber,
                r.SupplierName,
                r.IssueDate,
                r.CreatedAt,
                r.RegisteredBy.FullName,
                r.Subtotal,
                r.InvoiceTotal,
                r.FileName,
                r.Lines
                    .OrderBy(line => line.LineNumber)
                    .Select(line => new InvoiceReceiptLineItem(
                        line.LineNumber,
                        line.Description,
                        line.SupplierItemCode,
                        line.ProductId,
                        line.ProductCode,
                        line.ProductName,
                        line.ProductCreated,
                        line.Quantity,
                        line.UnitPrice))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return receipt is null ? InvoiceErrors.ReceiptNotFound : receipt;
    }

    public async Task<Result<InvoiceReceiptFile>> GetFileAsync(int id, CancellationToken ct)
    {
        var file = await db.InvoiceReceipts.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new InvoiceReceiptFile(r.FileName, r.FileContentType, r.FileContent))
            .FirstOrDefaultAsync(ct);

        return file is null ? InvoiceErrors.ReceiptNotFound : file;
    }
}
