using System.Text.RegularExpressions;
using Inventory.Domain.Security;

namespace Inventory.Tests;

public sealed partial class PermissionCatalogTests
{
    [Fact]
    public void Every_default_role_only_grants_known_permissions()
    {
        foreach (var role in Roles.Defaults)
        {
            role.Permissions.ShouldAllBe(p => Permissions.All.ContainsKey(p), $"role {role.Code}");
            role.Permissions.Distinct().Count().ShouldBe(role.Permissions.Count, $"role {role.Code} has duplicates");
        }
    }

    [Fact]
    public void Administrator_has_every_permission()
    {
        Roles.Defaults.Single(r => r.Code == Roles.Administrator).Permissions.ShouldBe(Permissions.All.Keys, ignoreOrder: true);
    }

    [Fact]
    public void Operator_cannot_approve_ship_cancel_or_manage_users()
    {
        var op = Roles.Defaults.Single(r => r.Code == Roles.WarehouseOperator).Permissions;
        op.ShouldNotContain(Permissions.DispatchesApprove);
        op.ShouldNotContain(Permissions.DispatchesShip);
        op.ShouldNotContain(Permissions.DispatchesCancel);
        op.ShouldNotContain(Permissions.InventoryAdjustApprove);
        op.ShouldNotContain(Permissions.UsersManage);
    }

    /// <summary>The frontend permission list must match the backend (the UI hides features by these codes).</summary>
    [Fact]
    public void Frontend_permission_codes_match_backend()
    {
        var path = FindRepoFile("frontend/projects/shared/src/lib/auth/permissions.ts");
        Assert.SkipWhen(path is null, "Frontend sources not present next to the backend.");

        var frontend = CodePattern().Matches(File.ReadAllText(path!)).Select(m => m.Groups[1].Value).ToHashSet();
        frontend.ShouldBe(Permissions.All.Keys.ToHashSet(), ignoreOrder: true);
    }

    private static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [GeneratedRegex(@":\s*'([a-z]+\.[a-z.]+)'")]
    private static partial Regex CodePattern();
}
