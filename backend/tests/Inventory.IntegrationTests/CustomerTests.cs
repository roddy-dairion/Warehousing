using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Customers;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class CustomerTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object NewCustomer(string reference) => new
    {
        reference,
        companyName = "Sunshine Retail Ltd",
        email = "orders@sunshine.mu",
        billingCity = "Port Louis",
        addresses = new[]
        {
            new { label = "Head office", line1 = "1 Royal Road", city = "Port Louis", isDefault = false },
            new { label = "Shop Curepipe", line1 = "5 Main St", city = "Curepipe", isDefault = true },
        },
    };

    [Fact]
    public async Task Create_customer_with_addresses_and_one_default()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var customer = await (await admin.PostJsonAsync("/api/v1/customers", NewCustomer(Unique("c")))).ReadAsync<CustomerDto>();

        customer.Addresses.Count.ShouldBe(2);
        customer.Addresses.Single(a => a.IsDefault).Label.ShouldBe("Shop Curepipe");
        customer.Reference.ShouldStartWith("C-");
    }

    [Fact]
    public async Task Changing_the_default_address_and_deactivating_it_keeps_exactly_one_default()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var c = await (await admin.PostJsonAsync("/api/v1/customers", NewCustomer(Unique("d")))).ReadAsync<CustomerDto>();
        var office = c.Addresses.Single(a => a.Label == "Head office");

        c = await (await admin.PutJsonAsync($"/api/v1/customers/{c.Id}/addresses/{office.Id}", new
        {
            address = new { label = "Head office", line1 = "1 Royal Road", city = "Port Louis", isDefault = true },
            version = c.Version,
        })).ReadAsync<CustomerDto>();
        c.Addresses.Single(a => a.IsDefault).Id.ShouldBe(office.Id);

        c = await (await admin.PostAsync($"/api/v1/customers/{c.Id}/addresses/{office.Id}/deactivate?version={c.Version}")).ReadAsync<CustomerDto>();
        c.Addresses.Count(a => a.IsDefault).ShouldBe(1);
        c.Addresses.Single(a => a.IsDefault).Label.ShouldBe("Shop Curepipe");
    }

    [Fact]
    public async Task Reference_must_be_unique()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var reference = Unique("dup");
        (await admin.PostJsonAsync("/api/v1/customers", NewCustomer(reference))).EnsureSuccessStatusCode();
        (await (await admin.PostJsonAsync("/api/v1/customers", NewCustomer(reference.ToLowerInvariant()))).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("customer.reference_taken");
    }

    [Fact]
    public async Task Invalid_input_returns_field_errors()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var problem = await (await admin.PostJsonAsync("/api/v1/customers", new { reference = "bad ref!", companyName = "", email = "nope" })).ProblemAsync(HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("reference", out _).ShouldBeTrue();
        errors.TryGetProperty("companyName", out _).ShouldBeTrue();
        errors.TryGetProperty("email", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Dispatch_staff_can_read_but_not_change_customers()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var c = await (await admin.PostJsonAsync("/api/v1/customers", NewCustomer(Unique("r")))).ReadAsync<CustomerDto>();
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync()));

        (await op.GetAsync($"/api/v1/customers/{c.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await op.PostJsonAsync("/api/v1/customers", NewCustomer(Unique("x")))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await op.PostAsync($"/api/v1/customers/{c.Id}/deactivate")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_is_paged_searchable_and_filterable()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var tag = Unique("srch");
        for (var i = 0; i < 3; i++) (await admin.PostJsonAsync("/api/v1/customers", NewCustomer($"{tag}-{i}"))).EnsureSuccessStatusCode();

        var page = (await admin.GetJsonAsync<PagedResult<CustomerListItemDto>>($"/api/v1/customers?search={tag}&pageSize=2&sortBy=reference&sortDir=desc"))!;
        page.TotalCount.ShouldBe(3);
        page.Items.Count.ShouldBe(2);
        page.Items[0].Reference.ShouldBe($"{tag}-2");
        page.Items[0].AddressCount.ShouldBe(2);
    }
}
