namespace QueueLess.Core;

// Pure functions: no database, clock, UI or mutable shared state.
public static class QueueRules
{
    public static bool IsActive(TicketStatus status) => status is TicketStatus.Waiting or TicketStatus.Serving;
    public static int EstimateWait(int waitingAhead, int averageMinutes, bool serving) =>
        Math.Max(0, waitingAhead) * Math.Max(1, averageMinutes) + (serving ? Math.Max(1, averageMinutes) : 0);
    public static bool CanFinish(TicketStatus current, TicketStatus next) =>
        current == TicketStatus.Serving && next is TicketStatus.Completed or TicketStatus.NoShow;
    public static DateTimeOffset CampusTime(long timestamp) => DateTimeOffset.FromUnixTimeSeconds(timestamp).ToOffset(TimeSpan.FromHours(5));
    public static string Day(long timestamp) => CampusTime(timestamp).ToString("yyyy-MM-dd");
    public static bool ValidSlot(long timestamp, long now)
    {
        if (timestamp <= now || timestamp > now + 7 * 86400) return false;
        var local = CampusTime(timestamp);
        return local.Hour >= 9 && local.Hour < 18
            && local.Minute % 30 == 0 && local.Second == 0;
    }
    public static bool CanCheckIn(long slot, long now) => now >= slot - 600 && now <= slot + 900;
}
