namespace AIManager.Api.Services;

/// <summary>Weekend-aware "previous working day" — ports the n8n "Identify target day" logic.</summary>
public static class WorkingDays
{
    public static TimeZoneInfo ResolveZone(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>Today's date in the given timezone.</summary>
    public static DateOnly TodayIn(string tz, DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, ResolveZone(tz));
        return DateOnly.FromDateTime(local.Date);
    }

    /// <summary>Parse a CSV of DayOfWeek ints (Sun=0…Sat=6). Falls back to Mon–Fri when empty/invalid.</summary>
    public static ISet<DayOfWeek> ParseWorkingDays(string? csv)
    {
        var set = new HashSet<DayOfWeek>();
        if (!string.IsNullOrWhiteSpace(csv))
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var n) && n is >= 0 and <= 6)
                    set.Add((DayOfWeek)n);

        if (set.Count == 0)
            set = new HashSet<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
            };
        return set;
    }

    /// <summary>The most recent working day strictly before "today", honoring the team's working days.</summary>
    public static DateOnly PreviousWorkingDay(string tz, string? workingDaysCsv, DateTimeOffset nowUtc)
    {
        var working = ParseWorkingDays(workingDaysCsv);
        var d = TodayIn(tz, nowUtc).AddDays(-1);
        var guard = 0;
        while (!working.Contains(d.DayOfWeek) && guard++ < 14)
            d = d.AddDays(-1);
        return d;
    }
}
