using PaymentHub.Simple.Library.Rtgs;
namespace PaymentHub.Simple.Service;

public interface IVictoriaBank
{
    bool ValidatePayment(Payment payment);
    bool MutasiByRef(Payment payment);
    bool Vm40(Payment payment);
    bool PayOffUs(Payment payment);
    bool InquiryStatus(Payment payment);
}

// Simulator deterministik; tidak mengirim HTTP atau melakukan posting uang.
public sealed class VictoriaSimulator : IVictoriaBank
{
    public bool ValidatePayment(Payment p) => p.Scenario != DemoScenario.ValidationFailure;
    public bool MutasiByRef(Payment p) => p.Scenario != DemoScenario.MutasiMissing;
    public bool Vm40(Payment p) => p.Scenario != DemoScenario.Vm40Failure;
    public bool PayOffUs(Payment p) => p.Scenario is not (DemoScenario.OffUsTimeout or DemoScenario.InquiryPending);
    public bool InquiryStatus(Payment p) => p.Scenario != DemoScenario.InquiryPending;
}
