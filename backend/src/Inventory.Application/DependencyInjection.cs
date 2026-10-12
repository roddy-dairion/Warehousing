using FluentValidation;
using Inventory.Application.Common;
using Inventory.Application.Customers;
using Inventory.Application.Imports;
using Inventory.Application.Inventory;
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

        services.AddScoped<StockRules>();
        services.AddScoped<StockLedger>();
        services.AddScoped<ReservationService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<AdjustmentService>();
        services.AddScoped<TransferService>();
        services.AddScoped<LotService>();
        services.AddScoped<InventoryQueries>();
        services.AddScoped<ReconciliationService>();
        return services;
    }
}
