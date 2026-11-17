namespace QueueLess.Core;

public enum TicketStatus { Waiting, Serving, Completed, Cancelled, NoShow }
public enum BookingStatus { Reserved, CheckedIn, Cancelled, Missed }

public sealed class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Visitor";
}

public sealed class ServiceDesk
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Category { get; set; } = "";
    public int AverageMinutes { get; set; } = 8;
    public bool IsOpen { get; set; } = true;
    public int Counter { get; set; }
}

public sealed class QueueTicket
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public ServiceDesk Service { get; set; } = null!;
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string Code { get; set; } = "";
    public string Day { get; set; } = "";
    public int Sequence { get; set; }
    public TicketStatus Status { get; set; }
    public long JoinedAt { get; set; }
    public long? CalledAt { get; set; }
    public long? FinishedAt { get; set; }
}

public sealed class Appointment
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public ServiceDesk Service { get; set; } = null!;
    public int AccountId { get; set; }
    public long StartsAt { get; set; }
    public BookingStatus Status { get; set; }
}

public sealed class AuditEntry
{
    public int Id { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public long At { get; set; }
}

public sealed record ServiceSummary(ServiceDesk Service, int Waiting, int EstimatedMinutes, string? ServingCode);
public sealed record TicketView(int Id, string Code, string Service, TicketStatus Status, int Ahead, int WaitMinutes, int Counter, long JoinedAt);
public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}
