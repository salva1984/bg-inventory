using BgInventario.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Common;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Product> Products { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<InventoryLot> InventoryLots { get; }
    DbSet<ProductTrigram> ProductTrigrams { get; }
    DbSet<SupplierProductAlias> SupplierProductAliases { get; }
    DbSet<InvoiceReceipt> InvoiceReceipts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
