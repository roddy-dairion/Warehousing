using Inventory.Domain.Common;
using Inventory.Domain.Warehouses;

namespace Inventory.Tests;

public sealed class WarehouseTests
{
    [Theory]
    [InlineData("main", "MAIN")]
    [InlineData(" north-2 ", "NORTH-2")]
    public void Code_is_trimmed_and_upper_cased(string input, string expected)
    {
        Warehouse.Create(input, "Main warehouse").Code.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("TOO-LONG-CODE-1234")]
    [InlineData("BAD CODE")]
    [InlineData("-MAIN")]
    public void Invalid_codes_are_rejected(string code)
    {
        Should.Throw<DomainRuleException>(() => Warehouse.Create(code, "Name")).Code.ShouldBe("warehouse.code_invalid");
    }

    [Fact]
    public void Name_is_required()
    {
        Should.Throw<DomainRuleException>(() => Warehouse.Create("MAIN", "   ")).Code.ShouldBe("warehouse.name_invalid");
    }

    [Fact]
    public void Ids_are_time_ordered_uuid_v7()
    {
        Warehouse.Create("MAIN", "Main").Id.Version.ShouldBe(7);
    }

    [Fact]
    public void New_warehouses_are_active_and_can_be_deactivated()
    {
        var w = Warehouse.Create("MAIN", "Main");
        w.IsActive.ShouldBeTrue();
        w.Deactivate();
        w.IsActive.ShouldBeFalse();
    }
}
