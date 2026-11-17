using QueueLess.Core;
namespace QueueLess.Tests;
public class BookingIntegrationTests : IAsyncLifetime
{
    private readonly QueueTestStore store = new();
    public Task InitializeAsync() => store.InitializeAsync();
    public Task DisposeAsync() => store.DisposeAsync();
    private long Slot => store.Clock.UtcNow.AddHours(1).ToUnixTimeSeconds();

    [Fact]
    public async Task CompetingBookingsHaveExactlyOneWinner()
    {
        var results = await Task.WhenAll(store.Service.BookAsync(store.User(2), 1, Slot), store.Service.BookAsync(store.User(3), 1, Slot));
        Assert.Single(results, r => r.Success);
        Assert.DoesNotContain(Slot, await store.Service.GetAvailableSlotsAsync(1, new(2026, 11, 17)));
    }
    [Fact]
    public async Task CancellationFreesTheSlotAndRequiresOwnership()
    {
        await store.Service.BookAsync(store.User(2), 1, Slot);
        var appointment = Assert.Single(await store.Service.GetAppointmentsAsync(store.User(2)));
        Assert.False((await store.Service.CancelBookingAsync(store.User(3), appointment.Id)).Success);
        Assert.True((await store.Service.CancelBookingAsync(store.User(2), appointment.Id)).Success);
        Assert.Contains(Slot, await store.Service.GetAvailableSlotsAsync(1, new(2026, 11, 17)));
        Assert.True((await store.Service.BookAsync(store.User(3), 1, Slot)).Success);
    }
    [Fact]
    public async Task AVisitorCannotDoubleBookDifferentServicesAtSameTime()
    {
        Assert.True((await store.Service.BookAsync(store.User(2), 1, Slot)).Success);
        Assert.False((await store.Service.BookAsync(store.User(2), 2, Slot)).Success);
    }
    [Fact]
    public async Task ThreeUpcomingReservationsIsTheLimit()
    {
        foreach (var i in Enumerable.Range(0, 3)) Assert.True((await store.Service.BookAsync(store.User(2), 1, Slot + i * 1800)).Success);
        Assert.False((await store.Service.BookAsync(store.User(2), 1, Slot + 5400)).Success);
    }
    [Fact]
    public async Task CheckInCreatesOneTicketAndCannotRepeat()
    {
        await store.Service.BookAsync(store.User(2), 1, Slot);
        var appointment = Assert.Single(await store.Service.GetAppointmentsAsync(store.User(2)));
        Assert.False((await store.Service.CheckInAsync(store.User(2), appointment.Id)).Success);
        store.Clock.UtcNow = store.Clock.UtcNow.AddMinutes(50);
        Assert.False((await store.Service.CheckInAsync(store.User(3), appointment.Id)).Success);
        Assert.True((await store.Service.CheckInAsync(store.User(2), appointment.Id)).Success);
        Assert.False((await store.Service.CheckInAsync(store.User(2), appointment.Id)).Success);
        Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2)));
        Assert.Equal(BookingStatus.CheckedIn, Assert.Single(await store.Service.GetAppointmentsAsync(store.User(2))).Status);
    }
    [Fact]
    public async Task ActiveTicketBlocksCheckInWithoutConsumingReservation()
    {
        await store.Service.BookAsync(store.User(2), 1, Slot);
        await store.Service.JoinAsync(store.User(2), 2);
        var appointment = Assert.Single(await store.Service.GetAppointmentsAsync(store.User(2)));
        store.Clock.UtcNow = store.Clock.UtcNow.AddHours(1);
        Assert.False((await store.Service.CheckInAsync(store.User(2), appointment.Id)).Success);
        Assert.Equal(BookingStatus.Reserved, Assert.Single(await store.Service.GetAppointmentsAsync(store.User(2))).Status);
    }
    [Fact]
    public async Task MissedAppointmentIsMarkedByMaintenance()
    {
        await store.Service.BookAsync(store.User(2), 1, Slot);
        store.Clock.UtcNow = store.Clock.UtcNow.AddMinutes(76);
        await store.Service.MaintainAsync();
        Assert.Equal(BookingStatus.Missed, Assert.Single(await store.Service.GetAppointmentsAsync(store.User(2))).Status);
    }
    [Fact]
    public async Task PausedAndUnknownServicesCannotBeBooked()
    {
        await store.Service.SetServiceOpenAsync(store.User(1), 1, false);
        Assert.False((await store.Service.BookAsync(store.User(2), 1, Slot)).Success);
        Assert.False((await store.Service.BookAsync(store.User(2), 999, Slot)).Success);
        Assert.Empty(await store.Service.GetAvailableSlotsAsync(1, new(2026, 11, 17)));
    }
    [Fact]
    public async Task PastAndMalformedSlotsAreRejected()
    {
        Assert.False((await store.Service.BookAsync(store.User(2), 1, Slot - 7200)).Success);
        Assert.False((await store.Service.BookAsync(store.User(2), 1, Slot + 60)).Success);
        Assert.False((await store.Service.BookAsync(store.User(2), 1, long.MaxValue)).Success);
    }
}
