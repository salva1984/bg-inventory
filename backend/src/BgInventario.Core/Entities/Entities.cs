// Domain entities of the inventory. Each entity has a *Limits class next to it: the same
// constants size the database columns (EF configuration) and validate the requests (DTOs),
// so a rule like "a code has at most 30 characters" lives in a single place.
namespace BgInventario.Core.Entities;

/// <summary>Timestamps filled automatically by an EF SaveChanges interceptor.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Soft delete: deleting only stamps <see cref="DeletedAt"/> and a global EF query filter hides the
/// row from every query, so the history stays in the database without leaking into the API.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}

public enum UserRole
{
    Admin,
    Viewer
}

/// <summary>Application user. Only used for authentication; not related to the inventory.</summary>
public class User : IAuditable
{
    public int Id { get; set; }

    /// <summary>Login identifier. Stored lowercase so the lookup is case-insensitive.</summary>
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class UserLimits
{
    // Longest address allowed by SMTP (RFC 5321 path limit).
    public const int EmailMaxLength = 254;
    public const int FullNameMaxLength = 150;
    public const int PasswordMaxLength = 100;

    // Identity's PBKDF2 hashes are ~84 chars in base64; the margin allows future algorithms.
    public const int PasswordHashMaxLength = 500;
    public const int RoleMaxLength = 20;
}

/// <summary>Catalog item. Its price and stock live in its <see cref="InventoryLot"/>s.</summary>
public class Product : IAuditable, ISoftDeletable
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<InventoryLot> Lots { get; set; } = [];

    /// <summary>Approximate-search index of the product, kept in sync by ProductSearchIndex.</summary>
    public List<ProductTrigram> Trigrams { get; set; } = [];
}

public static class ProductLimits
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;
}

public class Supplier : IAuditable, ISoftDeletable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<InventoryLot> Lots { get; set; } = [];
}

public static class SupplierLimits
{
    public const int NameMaxLength = 150;
    public const int ContactEmailMaxLength = 150;
    public const int PhoneMaxLength = 30;
}

/// <summary>
/// Inventory record: price and stock of a product from a given supplier and lot number.
/// A product has one row per supplier/lot combination, e.g. the same monitor bought from three suppliers.
/// </summary>
public class InventoryLot : IAuditable
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int SupplierId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Product Product { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
}

public static class InventoryLotLimits
{
    public const int LotNumberMaxLength = 50;

    // Money is stored as decimal(18,2): 18 digits in total, 2 of them after the decimal point.
    public const int PricePrecision = 18;
    public const int PriceScale = 2;
    public const double MinPrice = 0.01;
    public const double MaxPrice = 999_999_999;
}

/// <summary>
/// One trigram (three consecutive characters) of a product's normalized name and code. The table
/// works as an approximate-search index: the products that share the most trigrams with an invoice
/// line are its candidates, found by an indexed query instead of loading the catalog into memory.
/// </summary>
public class ProductTrigram
{
    public int ProductId { get; set; }
    public required string Trigram { get; set; }
    public Product Product { get; set; } = null!;
}

public static class ProductTrigramLimits
{
    public const int TrigramLength = 3;
}

/// <summary>
/// Equivalence confirmed by a user: how a supplier writes a product in its invoices.
/// It belongs to a supplier because the same text can mean different products for different suppliers.
/// The next invoice with the same description (or item code) is recognized without guessing.
/// </summary>
public class SupplierProductAlias : IAuditable
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public int ProductId { get; set; }

    /// <summary>The description exactly as the invoice wrote it, for display.</summary>
    public required string Description { get; set; }

    /// <summary>Lookup key: the description after ProductSearchIndex.Normalize.</summary>
    public required string NormalizedDescription { get; set; }

    /// <summary>The supplier's own article code, when the invoice has one.</summary>
    public string? SupplierItemCode { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public static class SupplierProductAliasLimits
{
    public const int DescriptionMaxLength = 300;
    public const int SupplierItemCodeMaxLength = 50;
}

/// <summary>
/// History of a confirmed supplier invoice: who registered it, when, the original file and what each
/// line became. It is written with the stock, so every invoice in the inventory has its record.
///
/// The supplier and product names are copied at confirmation time: the record keeps saying what
/// happened even if they are renamed or deleted later.
/// </summary>
public class InvoiceReceipt : IAuditable
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public required string SupplierName { get; set; }

    /// <summary>Normalized like the lot number of the received stock.</summary>
    public required string InvoiceNumber { get; set; }
    public DateOnly? IssueDate { get; set; }

    /// <summary>Sum of quantity × unit price of the confirmed lines, before taxes.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>The total with taxes printed on the invoice, as the analysis read it; null if not found.</summary>
    public decimal? InvoiceTotal { get; set; }

    public int RegisteredByUserId { get; set; }

    /// <summary>The uploaded file, as the user sent it. The type is detected from its content.</summary>
    public required string FileName { get; set; }
    public required string FileContentType { get; set; }
    public required byte[] FileContent { get; set; }

    /// <summary>When the invoice was confirmed (filled by the audit interceptor).</summary>
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // No navigation to Supplier or Product: they have soft-delete query filters, and joining
    // through them would hide the history of a deleted supplier or product. The copied names are used.
    public User RegisteredBy { get; set; } = null!;
    public List<InvoiceReceiptLine> Lines { get; set; } = [];
}

public static class InvoiceReceiptLimits
{
    // Largest invoice file accepted. An invoice is a few pages; the limit protects the server's
    // memory and keeps the stored file small.
    public const int MaxFileMegabytes = 10;
    public const int MaxFileBytes = MaxFileMegabytes * 1024 * 1024;

    // Longest file name allowed by common file systems.
    public const int FileNameMaxLength = 255;

    // The longest media type the service stores is "application/pdf".
    public const int FileContentTypeMaxLength = 50;

    // Amounts of a whole invoice. A line can reach int.MaxValue units × MaxPrice (about 2.1e18),
    // which needs 19 integer digits: more than the 16 of a price's decimal(18,2).
    public const int AmountPrecision = 21;
    public const string MaxAmount = "9999999999999999999.99";
}

/// <summary>
/// One confirmed line: the description as the invoice wrote it and the product it was mapped to.
/// </summary>
public class InvoiceReceiptLine
{
    public int Id { get; set; }
    public int InvoiceReceiptId { get; set; }

    /// <summary>Position of the line in the confirmed invoice, starting at 1.</summary>
    public int LineNumber { get; set; }

    /// <summary>The description exactly as the invoice wrote it.</summary>
    public required string Description { get; set; }
    public string? SupplierItemCode { get; set; }

    public int ProductId { get; set; }
    public required string ProductCode { get; set; }
    public required string ProductName { get; set; }

    /// <summary>The product did not exist: the user created it while reviewing this invoice.</summary>
    public bool ProductCreated { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public InvoiceReceipt InvoiceReceipt { get; set; } = null!;
}
