using BgInventario.Core.Entities;
using Bogus;

namespace BgInventario.Infrastructure.Persistence;

internal sealed record GeneratedSampleData(List<Supplier> Suppliers, List<Product> Products);

internal static class SampleDataGenerator
{
    // Fixed seed: every run generates the same catalog.
    private const int RandomSeed = 2026;

    private const int MaxLotsPerProduct = 3;
    private const int MaxStock = 150;

    public static GeneratedSampleData Generate(int productCount, IEnumerable<Supplier> existingSuppliers)
    {
        var faker = new Faker("es") { Random = new Randomizer(RandomSeed) };

        var suppliers = SampleCatalog.Suppliers
            .Select(s => Supplier(faker, s))
            .ToList();

        var supplierPool = existingSuppliers.Concat(suppliers).ToList();

        var products = new List<Product>(productCount);
        for (var sequence = 1; sequence <= productCount; sequence++)
            products.Add(Product(faker, sequence, supplierPool));

        return new GeneratedSampleData(suppliers, products);
    }

    private static Supplier Supplier(Faker faker, SampleSupplier sample) => new()
    {
        Name = sample.Name,
        ContactEmail = $"ventas@{sample.EmailDomain}",
        Phone = faker.Phone.PhoneNumber("04#######") // Guayaquil landline format, like the fixed suppliers
    };

    private static Product Product(Faker faker, int sequence, IReadOnlyList<Supplier> supplierPool)
    {
        var type = faker.PickRandom(SampleCatalog.ProductTypes);
        var brand = faker.PickRandom(SampleCatalog.Brands);
        var model = faker.Random.Replace("??-####").ToUpperInvariant(); // '?' is a letter, '#' a digit

        var lotCount = faker.Random.Int(1, MaxLotsPerProduct);
        var lotSuppliers = faker.PickRandom(supplierPool, lotCount);

        return new Product
        {
            Code = $"{type.CodePrefix}-{sequence:D5}",
            Name = $"{type.Name} {brand} {model}",
            Description = type.Description,
            Lots = lotSuppliers.Select(supplier => Lot(faker, supplier, type)).ToList()
        };
    }

    private static InventoryLot Lot(Faker faker, Supplier supplier, SampleProductType type)
    {
        return new InventoryLot
        {
            Supplier = supplier,
            LotNumber = $"L-{faker.Random.Int(1000, 9999)}",
            Price = faker.Finance.Amount(type.MinPrice, type.MaxPrice),
            Stock = faker.Random.Int(0, MaxStock)
        };
    }
}
