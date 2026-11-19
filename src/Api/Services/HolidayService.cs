using Api.Data;

namespace Api.Services;

public class HolidayException(string message) : Exception(message);

/// <summary>Public holidays by state (POL-13). Static and in-memory: the list is synthetic seed data.</summary>
public static class HolidayService
{
    public const int MinYear = 2000, MaxYear = 2100;

    /// <summary>
    /// "ALL" gives national holidays only; SEL, KUL or PNG gives national plus that state's, sorted by date.
    /// Throws <see cref="HolidayException"/> for an unknown state or a missing or out-of-range year.
    /// </summary>
    public static IReadOnlyList<Holiday> For(string? state, int? year)
    {
        var code = state?.Trim().ToUpperInvariant();
        if (code != Holidays.National && !Holidays.States.Contains(code))
            throw new HolidayException($"Unknown state '{state}'. Use {Holidays.National}, {string.Join(", ", Holidays.States)}.");
        if (year is not int y || y < MinYear || y > MaxYear)
            throw new HolidayException($"Year is required and must be between {MinYear} and {MaxYear}.");

        return Holidays.All
            .Where(h => h.Date.Year == y && (h.State == Holidays.National || h.State == code))
            .OrderBy(h => h.Date).ThenBy(h => h.State)
            .ToList();
    }
}
