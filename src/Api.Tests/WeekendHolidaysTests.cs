using System.Net;
using System.Net.Http.Json;
using Api.Services;

namespace Api.Tests;

/// <summary>Public holidays that fall on a Saturday or Sunday, and the POL-13 replacement day for Sundays.</summary>
public class HolidayServiceTests
{
    readonly HolidayService holidays = new();

    [Fact]
    public void Federal_Territory_Day_on_Sunday_2026_is_replaced_on_Monday_in_KL()
    {
        var result = holidays.OnWeekends("KUL", 2026);

        var ftDay = Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 2, 1), ftDay.Date);
        Assert.Equal(DayOfWeek.Sunday, ftDay.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 2, 2), ftDay.ReplacementDate);
    }

    [Fact]
    public void Saturday_holidays_have_no_replacement_day()
    {
        // 2027: Labour Day (1 May) and Christmas (25 Dec) are Saturdays.
        var result = holidays.OnWeekends("SEL", 2027);

        Assert.Contains(result, h => h.Date == new DateOnly(2027, 5, 1) && h.DayOfWeek == DayOfWeek.Saturday && h.ReplacementDate is null);
        Assert.Contains(result, h => h.Date == new DateOnly(2027, 12, 25) && h.ReplacementDate is null);
        Assert.All(result, h => Assert.True(h.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    [Fact]
    public void State_code_is_case_insensitive()
    {
        Assert.Equal(holidays.OnWeekends("KUL", 2026), holidays.OnWeekends("kul", 2026));
    }

    [Fact]
    public void Replacement_skips_a_holiday_on_the_next_working_day()
    {
        var sunday = new DateOnly(2026, 2, 1);
        var monday = sunday.AddDays(1);
        Assert.Equal(sunday.AddDays(2), HolidayService.ReplacementFor(sunday, new HashSet<DateOnly> { sunday, monday }));
    }

    [Theory]
    [InlineData("XYZ", 2026)]
    [InlineData("", 2026)]
    [InlineData("SEL", 1999)]
    [InlineData("SEL", 2101)]
    public void Unknown_state_or_year_out_of_range_throws(string state, int year)
    {
        Assert.Throws<HolidayException>(() => holidays.OnWeekends(state, year));
    }
}

public class HolidaysApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Row(DateOnly Date, string Name, string DayOfWeek, DateOnly? ReplacementDate);

    [Fact]
    public async Task Signed_in_employee_gets_weekend_holidays()
    {
        var farah = await api.SignedInAs(People.Farah);
        var res = await farah.GetAsync("/holidays/weekends?state=KUL&year=2026");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var row = Assert.Single((await res.Content.ReadFromJsonAsync<Row[]>())!);
        Assert.Equal("Sunday", row.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 2, 2), row.ReplacementDate);
    }

    [Fact]
    public async Task Missing_state_is_400()
    {
        var farah = await api.SignedInAs(People.Farah);
        Assert.Equal(HttpStatusCode.BadRequest, (await farah.GetAsync("/holidays/weekends?year=2026")).StatusCode);
    }

    [Fact]
    public async Task Unknown_state_is_400()
    {
        var farah = await api.SignedInAs(People.Farah);
        Assert.Equal(HttpStatusCode.BadRequest, (await farah.GetAsync("/holidays/weekends?state=XYZ&year=2026")).StatusCode);
    }

    [Fact]
    public async Task Year_out_of_range_is_400()
    {
        var farah = await api.SignedInAs(People.Farah);
        Assert.Equal(HttpStatusCode.BadRequest, (await farah.GetAsync("/holidays/weekends?state=SEL&year=1990")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_caller_is_401()
    {
        var anon = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/holidays/weekends?state=SEL&year=2026")).StatusCode);
    }
}
