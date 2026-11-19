using System.Net;
using System.Net.Http.Json;
using Api.Services;

namespace Api.Tests;

/// <summary>Public holidays: national for ALL, national + state for SEL/KUL/PNG, 400 for anything else.</summary>
public class HolidayServiceTests
{
    [Fact]
    public void ALL_returns_national_holidays_only()
    {
        var list = HolidayService.For("ALL", 2026);
        Assert.NotEmpty(list);
        Assert.All(list, h => Assert.Equal("ALL", h.State));
        Assert.Contains(list, h => h.Name == "Merdeka Day" && h.Date == new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void State_filter_adds_that_states_holidays_and_no_other_state()
    {
        var sel = HolidayService.For("SEL", 2026);
        Assert.Contains(sel, h => h.State == "SEL");
        Assert.Contains(sel, h => h.State == "ALL");
        Assert.DoesNotContain(sel, h => h.State is "KUL" or "PNG");
        Assert.Equal(HolidayService.For("ALL", 2026).Count + sel.Count(h => h.State == "SEL"), sel.Count);
    }

    [Fact]
    public void Each_state_has_its_own_holidays()
    {
        foreach (var s in new[] { "SEL", "KUL", "PNG" })
            Assert.Contains(HolidayService.For(s, 2026), h => h.State == s);
    }

    [Fact]
    public void State_is_case_insensitive_and_results_are_sorted_by_date()
    {
        var list = HolidayService.For("kul", 2026);
        Assert.Contains(list, h => h.State == "KUL");
        Assert.Equal(list.OrderBy(h => h.Date).ToList(), list);
    }

    [Fact]
    public void Year_filter_returns_only_that_year()
    {
        Assert.All(HolidayService.For("SEL", 2026), h => Assert.Equal(2026, h.Date.Year));
        Assert.Empty(HolidayService.For("SEL", 2031));
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_state_throws(string? state) =>
        Assert.Throws<HolidayException>(() => HolidayService.For(state, 2026));

    [Theory]
    [InlineData(null)]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Missing_or_silly_year_throws(int? year) =>
        Assert.Throws<HolidayException>(() => HolidayService.For("SEL", year));
}

public class HolidayApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Row(DateOnly Date, string Name, string State);

    [Fact]
    public async Task Get_holidays_for_a_state_returns_date_name_state()
    {
        var http = await api.SignedInAs(People.Farah);
        var res = await http.GetAsync("/holidays?state=SEL&year=2026");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<Row>>();
        Assert.Contains(rows!, r => r.State == "SEL");
        Assert.Contains(rows!, r => r.State == "ALL" && r.Name == "Merdeka Day");
    }

    [Fact]
    public async Task Get_holidays_for_ALL_returns_national_only()
    {
        var http = await api.SignedInAs(People.Farah);
        var rows = await http.GetFromJsonAsync<List<Row>>("/holidays?state=ALL&year=2026");
        Assert.NotEmpty(rows!);
        Assert.All(rows!, r => Assert.Equal("ALL", r.State));
    }

    [Theory]
    [InlineData("/holidays?state=XYZ&year=2026")]
    [InlineData("/holidays?year=2026")]
    [InlineData("/holidays?state=SEL")]
    [InlineData("/holidays?state=SEL&year=1800")]
    public async Task Bad_query_is_400_problem_details(string url)
    {
        var http = await api.SignedInAs(People.Farah);
        var res = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Not_signed_in_is_401()
    {
        var res = await api.CreateClient().GetAsync("/holidays?state=SEL&year=2026");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
