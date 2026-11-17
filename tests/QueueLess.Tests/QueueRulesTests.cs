using QueueLess.Core;
namespace QueueLess.Tests;

public class QueueRulesTests
{
    [Theory]
    [InlineData(0, 8, false, 0)]
    [InlineData(2, 8, false, 16)]
    [InlineData(2, 8, true, 24)]
    [InlineData(-2, 0, true, 1)]
    public void WaitEstimateAccountsForCurrentVisitor(int ahead, int minutes, bool serving, int expected) => Assert.Equal(expected, QueueRules.EstimateWait(ahead, minutes, serving));

    [Theory]
    [InlineData(TicketStatus.Waiting, true)]
    [InlineData(TicketStatus.Serving, true)]
    [InlineData(TicketStatus.Completed, false)]
    [InlineData(TicketStatus.Cancelled, false)]
    [InlineData(TicketStatus.NoShow, false)]
    public void TerminalTicketsAreNotActive(TicketStatus status, bool expected) => Assert.Equal(expected, QueueRules.IsActive(status));

    [Theory]
    [InlineData(TicketStatus.Serving, TicketStatus.Completed, true)]
    [InlineData(TicketStatus.Serving, TicketStatus.NoShow, true)]
    [InlineData(TicketStatus.Waiting, TicketStatus.Completed, false)]
    [InlineData(TicketStatus.Completed, TicketStatus.Completed, false)]
    [InlineData(TicketStatus.Serving, TicketStatus.Cancelled, false)]
    public void TransitionRulesRejectInvalidOutcomes(TicketStatus current, TicketStatus next, bool expected) => Assert.Equal(expected, QueueRules.CanFinish(current, next));

    [Theory]
    [InlineData(-601, false)]
    [InlineData(-600, true)]
    [InlineData(0, true)]
    [InlineData(900, true)]
    [InlineData(901, false)]
    public void CheckInWindowHasInclusiveBoundaries(int offset, bool expected) => Assert.Equal(expected, QueueRules.CanCheckIn(10000, 10000 + offset));

    [Theory]
    [InlineData("2026-11-18T09:00:00+05:00", true)]
    [InlineData("2026-11-18T17:30:00+05:00", true)]
    [InlineData("2026-11-18T18:00:00+05:00", false)]
    [InlineData("2026-11-18T08:30:00+05:00", false)]
    [InlineData("2026-11-18T10:15:00+05:00", false)]
    [InlineData("2026-11-18T10:30:01+05:00", false)]
    [InlineData("2026-11-25T10:00:00+05:00", false)]
    [InlineData("2026-11-17T09:00:00+05:00", false)]
    public void OnlyFutureCampusBusinessSlotsAreValid(string input, bool expected) => Assert.Equal(expected,
        QueueRules.ValidSlot(DateTimeOffset.Parse(input).ToUnixTimeSeconds(), DateTimeOffset.Parse("2026-11-17T10:00:00+05:00").ToUnixTimeSeconds()));

    [Fact] public void ExtremeTimestampIsRejectedWithoutThrowing() => Assert.False(QueueRules.ValidSlot(long.MaxValue, 1800000000));
    [Fact] public void DayUsesCampusTimezone() => Assert.Equal("2026-11-18", QueueRules.Day(DateTimeOffset.Parse("2026-11-17T20:00:00Z").ToUnixTimeSeconds()));
}
