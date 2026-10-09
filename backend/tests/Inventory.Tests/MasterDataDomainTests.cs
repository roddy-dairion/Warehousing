using Inventory.Application.Products;
using Inventory.Domain.Common;
using Inventory.Domain.Customers;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;

namespace Inventory.Tests;

public sealed class CustomerDomainTests
{
    [Fact]
    public void First_address_becomes_default_and_only_one_default_exists()
    {
        var c = Customer.Create("cust-1", "Acme");
        var a = c.AddAddress("Office", "1 Road", null, null, null, null, null, isDefault: false);
        a.IsDefault.ShouldBeTrue();
        var b = c.AddAddress("Shop", "2 Road", null, null, null, null, null, isDefault: true);
        c.Addresses.Count(x => x.IsDefault).ShouldBe(1);
        b.IsDefault.ShouldBeTrue();
        c.Reference.ShouldBe("CUST-1");
    }

    [Fact]
    public void Deactivating_the_default_moves_default_to_another_active_address()
    {
        var c = Customer.Create("C2", "Acme");
        var a = c.AddAddress("Office", "1 Road", null, null, null, null, null, false);
        var b = c.AddAddress("Shop", "2 Road", null, null, null, null, null, false);
        c.DeactivateAddress(a.Id);
        b.IsDefault.ShouldBeTrue();
        a.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void Address_labels_are_unique_per_customer_ignoring_case()
    {
        var c = Customer.Create("C3", "Acme");
        c.AddAddress("Office", "1 Road", null, null, null, null, null, false);
        Should.Throw<DomainRuleException>(() => c.AddAddress("office", "x", null, null, null, null, null, false)).Code.ShouldBe("address.label_duplicate");
    }
}

public sealed class ProductDomainTests
{
    private static readonly Guid Ea = Guid.CreateVersion7();
    private static readonly Guid Ctn = Guid.CreateVersion7();

    [Fact]
    public void Pack_quantities_convert_to_base_units()
    {
        var p = Product.Create("sku-1", "Soap", Ea);
        var carton = p.AddUnit(Ctn, 48);
        p.ToBaseQuantity(2, carton.Id).ShouldBe(96);
        p.ToBaseQuantity(5, null).ShouldBe(5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1.0000001)]
    public void Invalid_pack_factors_are_rejected(decimal factor)
    {
        var p = Product.Create("SKU", "Soap", Ea);
        Should.Throw<DomainRuleException>(() => p.AddUnit(Ctn, factor)).Code.ShouldBe("product.unit_factor_invalid");
    }

    [Fact]
    public void The_base_unit_cannot_be_a_pack()
    {
        var p = Product.Create("SKU", "Soap", Ea);
        Should.Throw<DomainRuleException>(() => p.AddUnit(Ea, 12)).Code.ShouldBe("product.unit_is_base");
        p.AddUnit(Ctn, 12);
        Should.Throw<DomainRuleException>(() => p.ChangeBaseUnit(Ctn)).Code.ShouldBe("product.base_unit_is_pack");
    }

    [Fact]
    public void One_primary_barcode_per_unit_and_primary_moves_on_removal()
    {
        var p = Product.Create("SKU", "Soap", Ea);
        var b1 = p.AddBarcode("111", null);
        var b2 = p.AddBarcode("222", null);
        b1.IsPrimary.ShouldBeTrue();
        b2.IsPrimary.ShouldBeFalse();
        p.SetPrimaryBarcode(b2.Id);
        b1.IsPrimary.ShouldBeFalse();
        p.RemoveBarcode(b2.Id);
        b1.IsPrimary.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("tab\there")]
    public void Invalid_barcodes_are_rejected(string value)
    {
        var p = Product.Create("SKU", "Soap", Ea);
        Should.Throw<DomainRuleException>(() => p.AddBarcode(value, null)).Code.ShouldBe("barcode.invalid");
    }

    [Fact]
    public void Expiry_requires_lot_tracking_and_shelf_life_requires_expiry()
    {
        var p = Product.Create("SKU", "Milk", Ea);
        Should.Throw<DomainRuleException>(() => p.SetStockRules(null, false, true, null)).Code.ShouldBe("product.expiry_needs_lot");
        Should.Throw<DomainRuleException>(() => p.SetStockRules(null, true, false, 30)).Code.ShouldBe("product.shelf_life_invalid");
        p.SetStockRules(10, true, true, 30);
        p.ShelfLifeDays.ShouldBe(30);
    }
}

public sealed class LocationDomainTests
{
    [Fact]
    public void Only_storage_bins_can_be_pickable_and_default_pickability_follows_type()
    {
        var wh = Guid.CreateVersion7();
        WarehouseLocation.Create(wh, "a-1", LocationType.Storage).IsPickable.ShouldBeTrue();
        WarehouseLocation.Create(wh, "dock-2", LocationType.Dispatch).IsPickable.ShouldBeFalse();
        Should.Throw<DomainRuleException>(() => WarehouseLocation.Create(wh, "Q", LocationType.Quarantine, isPickable: true)).Code.ShouldBe("location.pickable_type");
    }

    [Fact]
    public void Built_in_areas_cannot_be_deactivated_or_retyped()
    {
        var bin = WarehouseLocation.CreateSystem(Guid.CreateVersion7(), "RECEIVING", LocationType.Receiving);
        Should.Throw<DomainRuleException>(bin.Deactivate).Code.ShouldBe("location.system_deactivate");
        Should.Throw<DomainRuleException>(() => bin.Update(null, LocationType.Storage, null, false)).Code.ShouldBe("location.system_type");
    }
}

public sealed class ImageSnifferTests
{
    [Fact]
    public void Detects_real_image_types_from_content()
    {
        ImageSniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        ImageSniffer.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).ShouldBe("image/png");
        ImageSniffer.Detect("RIFF\0\0\0\0WEBP"u8).ShouldBe("image/webp");
        ImageSniffer.Detect("<svg onload=alert(1)>"u8).ShouldBeNull();
        ImageSniffer.Detect("GIF89a"u8).ShouldBeNull();
    }
}
