// Registers the use cases of Core in the DI container.
using BgInventario.Core.Features.Auth;
using BgInventario.Core.Features.ClientLogs;
using BgInventario.Core.Features.Dashboard;
using BgInventario.Core.Features.Products;
using BgInventario.Core.Features.Suppliers;
using Microsoft.Extensions.DependencyInjection;

namespace BgInventario.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<ClientLogService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ProductService>();
        services.AddScoped<SupplierService>();
        return services;
    }
}
