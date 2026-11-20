// EF Core implementation of IAppDbContext: the only class that knows about SQL Server tables.
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BgInventario.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<ProductTrigram> ProductTrigrams => Set<ProductTrigram>();
    public DbSet<SupplierProductAlias> SupplierProductAliases => Set<SupplierProductAlias>();
    public DbSet<InvoiceReceipt> InvoiceReceipts => Set<InvoiceReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    // Every DateTime is stored in UTC; marking it as Utc on read makes the JSON carry the "Z"
    // so clients convert it to their local time correctly.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
