using System.Net;
using System.Net.Http.Json;
using Api.Services;

namespace Api.Tests;

/// <summary>The signed-in employee's own profile, including their manager's name.</summary>
public class ProfileServiceTests
{
    [Fact]
    public async Task Profile_includes_the_managers_name()
    {
        using var db = TestDb.Create();
        var farah = await new ProfileService(db).GetAsync(6);

        Assert.Equal("Farah Tan", farah!.Name);
        Assert.Equal(1, farah.ManagerId);
        Assert.Equal("Aina Lee", farah.ManagerName);
    }

    [Fact]
    public async Task No_manager_means_no_manager_name()
    {
        using var db = TestDb.Create();
        var aina = await new ProfileService(db).GetAsync(1);

        Assert.Null(aina!.ManagerId);
        Assert.Null(aina.ManagerName);
    }

    [Fact]
    public async Task Manager_id_that_points_to_nobody_gives_no_manager_name()
    {
        using var db = TestDb.Create();
        db.Employees.Find(6)!.ManagerId = 999;
        db.SaveChanges();

        var farah = await new ProfileService(db).GetAsync(6);
        Assert.Equal(999, farah!.ManagerId);
        Assert.Null(farah.ManagerName);
    }

    [Fact]
    public async Task Unknown_employee_is_null()
    {
        using var db = TestDb.Create();
        Assert.Null(await new ProfileService(db).GetAsync(9999));
    }
}

public class MeApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record MeResponse(int Id, string Name, string Email, string Department, int? ManagerId, string? ManagerName, decimal AnnualEntitlement, string[] Roles);

    [Fact]
    public async Task Me_returns_the_signed_in_employees_manager_name()
    {
        var farah = await api.SignedInAs(People.Farah);
        var me = await farah.GetFromJsonAsync<MeResponse>("/me");

        Assert.Equal(6, me!.Id);
        Assert.Equal("Farah Tan", me.Name);
        Assert.Equal(1, me.ManagerId);
        Assert.Equal("Aina Lee", me.ManagerName);
        Assert.Equal(["Employee"], me.Roles);
    }

    [Fact]
    public async Task Me_for_someone_without_a_manager_has_null_manager_name()
    {
        var aina = await api.SignedInAs(People.Aina);
        var me = await aina.GetFromJsonAsync<MeResponse>("/me");

        Assert.Equal(1, me!.Id);
        Assert.Null(me.ManagerName);
    }

    [Fact]
    public async Task Me_ignores_an_employeeId_in_the_query()
    {
        var farah = await api.SignedInAs(People.Farah);
        var me = await farah.GetFromJsonAsync<MeResponse>("/me?employeeId=1");
        Assert.Equal(6, me!.Id);
        Assert.Equal("Aina Lee", me.ManagerName);
    }

    [Fact]
    public async Task Me_without_sign_in_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me")).StatusCode);
    }
}
