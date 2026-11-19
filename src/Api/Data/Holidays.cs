namespace Api.Data;

/// <summary>One public holiday. State is "ALL" for a national holiday, else SEL, KUL or PNG.</summary>
public record Holiday(DateOnly Date, string Name, string State);

/// <summary>A small synthetic holiday list for training. Not an official calendar: check the gazette for real dates.</summary>
public static class Holidays
{
    public const string National = "ALL";
    public static readonly string[] States = ["SEL", "KUL", "PNG"];

    public static readonly Holiday[] All =
    [
        // National (synthetic 2026 dates)
        new(new(2026, 1, 1), "New Year's Day", National),
        new(new(2026, 2, 17), "Chinese New Year", National),
        new(new(2026, 2, 18), "Chinese New Year (second day)", National),
        new(new(2026, 3, 21), "Hari Raya Aidilfitri", National),
        new(new(2026, 3, 22), "Hari Raya Aidilfitri (second day)", National),
        new(new(2026, 5, 1), "Labour Day", National),
        new(new(2026, 5, 31), "Wesak Day", National),
        new(new(2026, 6, 1), "Agong's Birthday", National),
        new(new(2026, 8, 31), "Merdeka Day", National),
        new(new(2026, 9, 16), "Malaysia Day", National),
        new(new(2026, 11, 8), "Deepavali", National),
        new(new(2026, 12, 25), "Christmas Day", National),

        // Selangor
        new(new(2026, 1, 25), "Thaipusam", "SEL"),
        new(new(2026, 12, 11), "Sultan of Selangor's Birthday", "SEL"),

        // Kuala Lumpur
        new(new(2026, 1, 25), "Thaipusam", "KUL"),
        new(new(2026, 2, 1), "Federal Territory Day", "KUL"),

        // Penang
        new(new(2026, 1, 25), "Thaipusam", "PNG"),
        new(new(2026, 7, 7), "George Town World Heritage City Day", "PNG"),
        new(new(2026, 7, 11), "Penang Governor's Birthday", "PNG"),
    ];
}
