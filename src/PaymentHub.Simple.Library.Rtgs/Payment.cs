namespace PaymentHub.Simple.Library.Rtgs;

public enum PaymentStatus
{
    Unposting, Posting, Posted, PostingNonVa, FailedToValidate,
    SetToPostFee, SetToSettle, RetryToSettle, Settle, SettleHold, HoldPostFee
}

public enum DemoScenario { Success, ValidationFailure, MutasiMissing, Vm40Failure, OffUsTimeout, InquiryPending }

public sealed record AuditEntry(DateTimeOffset At, PaymentStatus Status, string Message);

public sealed record Payment
{
    public required string Reference { get; init; }
    public required string Account { get; init; }
    public required string Name { get; init; }
    public required string OriginalMessage { get; init; }
    public DateOnly ValueDate { get; init; }
    public decimal Amount { get; init; }
    public decimal PaidAmount { get; set; }
    public string? ParentAccount { get; set; }
    public bool IsVa { get; set; }
    public DemoScenario Scenario { get; init; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Unposting;
    public int Attempts { get; set; }
    public List<AuditEntry> Audit { get; init; } = [];
    public void Record(PaymentStatus status, string message)
    {
        Status = status;
        Audit.Add(new(DateTimeOffset.UtcNow, status, message));
    }
}
