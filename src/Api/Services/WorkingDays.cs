namespace Api.Services;

public static class WorkingDays
{
    /// <summary>Counts Monday–Friday days between start and end, inclusive.</summary>
    public static int Count(DateOnly start, DateOnly end)
    {
        var count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        return count;
    }
}
