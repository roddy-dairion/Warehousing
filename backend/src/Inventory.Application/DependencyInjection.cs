using FluentValidation;
using Inventory.Application.Common;
using Inventory.Application.Customers;
using Inventory.Application.Imports;
using Inventory.Application.Products;
using Inventory.Application.Warehouses;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<BarcodeRegistry>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<LocationService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ProductService>();
        services.AddScoped<ImportService>();
        return services;
    }
}
