namespace Api.Services;

public class HolidayException(string message) : Exception(message);

public record WeekendHoliday(DateOnly Date, string Name, DayOfWeek DayOfWeek, DateOnly? ReplacementDate);

/// <summary>
/// Synthetic public holidays per state (fixed-date holidays only; moving ones like Hari Raya are not modelled).
/// POL-13: a holiday on a Sunday is replaced by the next working day that is not itself a holiday. Saturdays are not replaced.
/// </summary>
public class HolidayService
{
    public const int MinYear = 2000, MaxYear = 2100;

    static readonly (int Month, int Day, string Name)[] National =
    [
        (1, 1, "New Year's Day"),
        (5, 1, "Labour Day"),
        (8, 31, "National Day"),
        (9, 16, "Malaysia Day"),
        (12, 25, "Christmas Day"),
    ];

    static readonly Dictionary<string, (int Month, int Day, string Name)[]> ByState = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SEL"] = [(12, 11, "Sultan of Selangor's Birthday")],
        ["KUL"] = [(2, 1, "Federal Territory Day")],
        ["PNG"] = [(7, 7, "George Town World Heritage City Day")],
    };

    public static IReadOnlyCollection<string> States => ByState.Keys;

    public IReadOnlyList<WeekendHoliday> OnWeekends(string state, int year)
    {
        var all = For(state, year);
        var dates = all.Select(h => h.Date).ToHashSet();
        return all
            .Where(h => !WorkingDays.IsWorkingDay(h.Date))
            .Select(h => new WeekendHoliday(h.Date, h.Name, h.Date.DayOfWeek,
                h.Date.DayOfWeek == DayOfWeek.Sunday ? ReplacementFor(h.Date, dates) : null))
            .ToList();
    }

    public static DateOnly ReplacementFor(DateOnly sunday, IReadOnlySet<DateOnly> holidays)
    {
        var d = sunday.AddDays(1);
        while (!WorkingDays.IsWorkingDay(d) || holidays.Contains(d)) d = d.AddDays(1);
        return d;
    }

    static List<(DateOnly Date, string Name)> For(string state, int year)
    {
        if (string.IsNullOrWhiteSpace(state) || !ByState.TryGetValue(state, out var local))
            throw new HolidayException($"Unknown state '{state}'. Use one of: {string.Join(", ", States)}.");
        if (year is < MinYear or > MaxYear)
            throw new HolidayException($"Year must be between {MinYear} and {MaxYear}.");
        return National.Concat(local)
            .Select(h => (new DateOnly(year, h.Month, h.Day), h.Name))
            .OrderBy(h => h.Item1)
            .ToList();
    }
}
