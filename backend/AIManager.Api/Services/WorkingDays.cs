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

    /// <summary>The most recent working day strictly before "today" in the given timezone.</summary>
    public static DateOnly PreviousWorkingDay(string tz, DateTimeOffset nowUtc)
    {
        var d = TodayIn(tz, nowUtc).AddDays(-1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(-1);
        return d;
    }
}
