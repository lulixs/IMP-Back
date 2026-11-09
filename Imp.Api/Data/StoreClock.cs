namespace Imp.Api.Data;

// "Today" for the store: the date in Eastern time, not UTC, so a box saved at 9 PM counts as that day
// (docs/api/boxes.md). Use this instead of MySQL's CURDATE(), which gives the server's UTC date.
public static class StoreClock {
    private static readonly TimeZoneInfo storeTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static DateTime Today() {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, storeTimeZone).Date;
    }
}
