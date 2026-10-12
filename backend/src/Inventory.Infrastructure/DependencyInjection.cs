using Inventory.Application.Auth;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Application.Roles;
using Inventory.Application.Users;
using Inventory.Infrastructure.Auth;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<AuditableInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditableInterceptor>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.Section));

        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        services
            .AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireDigit = true;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = jwt.MaxFailedAttempts;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(jwt.LockoutMinutes);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddSingleton<TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IWarehouseAccess, WarehouseAccess>();
        services.AddScoped<IRoleQueries, RoleQueries>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IStockStore, StockStore>();
        services.AddOptions<InventoryOptions>().Bind(configuration.GetSection(InventoryOptions.Section));
        if (configuration.GetSection(InventoryOptions.Section).Get<InventoryOptions>()?.MaintenanceEnabled ?? true)
        {
            services.AddHostedService<MaintenanceService>();
        }
        return services;
    }
}
