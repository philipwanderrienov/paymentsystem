using PaymentHub.Simple.Library;
using PaymentHub.Simple.Library.Rtgs;
using PaymentHub.Simple.Repository;
namespace PaymentHub.Simple.Service;

public sealed class PaymentFlow(IPaymentRepository repository, IVictoriaBank bank, DataPaths paths, DemoOptions options)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public IReadOnlyList<Payment> All() => repository.All();

    public async Task<Payment> ImportAsync(string message, DemoScenario scenario, CancellationToken ct)
    {
        var p = Mt103.Parse(message, scenario);
        await gate.WaitAsync(ct);
        try
        {
            if (repository.Find(p.Reference) is not null)
                throw new InvalidOperationException("Sender reference sudah terdaftar.");
            p.IsVa = p.Account.Length >= options.VaMinLength && p.Account.Length <= options.VaMaxLength;
            p.Record(PaymentStatus.Unposting, "STPG: MT103 diterima.");
            repository.Save(p);
            return p;
        }
        finally { gate.Release(); }
    }

    public async Task ProcessAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            foreach (var p in repository.All())
            {
                ct.ThrowIfCancellationRequested();
                Step(p);
                repository.Save(p);
            }
        }
        finally { gate.Release(); }
    }

    private void Step(Payment p)
    {
        switch (p.Status)
        {
            case PaymentStatus.Unposting:
                if (!p.IsVa)
                {
                    WriteOutput(p, false);
                    p.Record(PaymentStatus.PostingNonVa, "Non-VA: MT103 asli diteruskan ke CBS; selesai dalam demo.");
                }
                else if (p.Amount <= options.Fee || !bank.ValidatePayment(p))
                {
                    if (++p.Attempts >= options.MaxAttempts)
                    {
                        WriteOutput(p, false);
                        p.Record(PaymentStatus.FailedToValidate, "Validasi VA gagal; MT103 asli dikembalikan ke CBS.");
                    }
                    else p.Record(p.Status, "Validasi VA gagal; retry pada polling berikutnya.");
                }
                else
                {
                    p.ParentAccount = options.ParentAccount;
                    p.PaidAmount = p.Amount - options.Fee;
                    p.Attempts = 0;
                    p.Record(PaymentStatus.Posting, "ValidatePayment: rekening induk dan nominal bersih diterima.");
                }
                break;
            case PaymentStatus.Posting:
                WriteOutput(p, true);
                p.Record(PaymentStatus.Posted, "MT103 rekening induk dibuat untuk CBS.");
                break;
            case PaymentStatus.Posted:
                if (!bank.MutasiByRef(p))
                    p.Record(p.Status, "MutasiByRef: belum ditemukan; lanjut VM40 seperti cabang total_data=0.");
                else p.Record(PaymentStatus.SetToPostFee, "MutasiByRef: transaksi ditemukan.");
                ApplyVm40(p);
                break;
            case PaymentStatus.SetToPostFee:
                ApplyVm40(p);
                break;
            case PaymentStatus.SetToSettle:
            case PaymentStatus.RetryToSettle:
                if (bank.PayOffUs(p))
                    p.Record(PaymentStatus.Settle, "PayOffUs berhasil.");
                else if (bank.InquiryStatus(p))
                    p.Record(PaymentStatus.Settle, "PayOffUs tanpa respons; InquiryStatus mengonfirmasi sukses.");
                else RetryOrHold(p, PaymentStatus.RetryToSettle, PaymentStatus.SettleHold, "InquiryStatus belum sukses");
                break;
        }
    }

    private void ApplyVm40(Payment p)
    {
        if (bank.Vm40(p))
        {
            p.Attempts = 0;
            p.Record(PaymentStatus.SetToSettle, "VM40 berhasil; siap PayOffUs.");
        }
        else RetryOrHold(p, PaymentStatus.SetToPostFee, PaymentStatus.HoldPostFee, "VM40 gagal");
    }

    private void RetryOrHold(Payment p, PaymentStatus retry, PaymentStatus hold, string message) =>
        p.Record(++p.Attempts >= options.MaxAttempts ? hold : retry,
            message + $" (percobaan {p.Attempts}/{options.MaxAttempts}).");

    private void WriteOutput(Payment p, bool parent)
    {
        var file = Path.Combine(paths.Folder("cbs-outbox"), p.Reference + ".txt");
        File.WriteAllText(file + ".tmp", parent ? Mt103.ForParent(p) : p.OriginalMessage);
        File.Move(file + ".tmp", file, true);
    }
}
