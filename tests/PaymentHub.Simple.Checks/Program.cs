using Microsoft.Extensions.Logging.Abstractions;
using PaymentHub.Simple.Adapter.Rtgs;
using PaymentHub.Simple.Library;
using PaymentHub.Simple.Library.Rtgs;
using PaymentHub.Simple.Repository;
using PaymentHub.Simple.Service;
using PaymentHub.Simple.TransferFile;

var root = Path.Combine(Path.GetTempPath(), "victoria-checks-" + Guid.NewGuid().ToString("N"));
var paths = new DataPaths(root);
var options = new DemoOptions { PollSeconds = 1 };
var repository = new JsonPaymentRepository(paths);
var flow = new PaymentFlow(repository, new VictoriaSimulator(), paths, options);
var transfer = new LocalFileTransfer(paths);
var checks = 0;
void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    checks++;
}
string Message(string reference, string account = "1234567890123456") =>
    "{4:\n:20:" + reference + "\n:32A:261123IDR1000000,25\n:59:/" + account + "\nNASABAH DEMO\n-}\n";

try
{
    foreach (var scenario in Enum.GetValues<DemoScenario>())
        await flow.ImportAsync(Message("T" + (int)scenario), scenario, default);
    var nonVa = Message("NONVA", "1000000002");
    await flow.ImportAsync(nonVa, DemoScenario.Success, default);
    try { await flow.ImportAsync(nonVa, DemoScenario.Success, default); throw new Exception("Duplicate accepted"); }
    catch (InvalidOperationException) { checks++; }
    try { await flow.ImportAsync(Message("nonva"), DemoScenario.Success, default); throw new Exception("Case duplicate accepted"); }
    catch (InvalidOperationException) { checks++; }
    foreach (var invalid in new[] { "broken", Message("../bad"), Message("BADAMOUNT").Replace("1000000,25", "-1,00"),
        Message("BADDATE").Replace("261123", "261332"), Message("BADACC", "1/2"), Message("DUPTAG") + ":20:OTHER\n" })
    {
        try { Mt103.Parse(invalid); throw new Exception("Malformed MT103 accepted"); }
        catch (FormatException) { checks++; }
    }
    for (var i = 0; i < 10; i++) await flow.ProcessAsync(default);
    Check(repository.Find("T0")!.Status == PaymentStatus.Settle, "VA success");
    Check(repository.Find("T1")!.Status == PaymentStatus.FailedToValidate, "VA failure");
    Check(repository.Find("T2")!.Status == PaymentStatus.Settle, "Mutasi missing -> VM40");
    Check(repository.Find("T3")!.Status == PaymentStatus.HoldPostFee, "VM40 hold");
    Check(repository.Find("T4")!.Status == PaymentStatus.Settle, "Inquiry fallback");
    Check(repository.Find("T4")!.Audit.Any(a => a.Message.Contains("InquiryStatus")), "Inquiry audit");
    Check(repository.Find("T5")!.Status == PaymentStatus.SettleHold, "Settlement retry exhaustion");
    Check(repository.Find("NONVA")!.Status == PaymentStatus.PostingNonVa, "Non-VA status");
    Check(File.ReadAllText(Path.Combine(paths.Folder("cbs-outbox"), "NONVA.txt")) == nonVa, "Non-VA unchanged");
    Check(File.ReadAllText(Path.Combine(paths.Folder("cbs-outbox"), "T1.txt")) == Message("T1"), "Failed VA unchanged");
    var output = Mt103.Parse(File.ReadAllText(Path.Combine(paths.Folder("cbs-outbox"), "T0.txt")));
    Check(output.Account == options.ParentAccount, "Parent account rewrite");
    Check(output.Amount == 997500.25m, "Decimal net amount preserved");
    Check(repository.Find("T0")!.Audit.Select(a => a.Status).SequenceEqual(new[] {
        PaymentStatus.Unposting, PaymentStatus.Posting, PaymentStatus.Posted,
        PaymentStatus.SetToPostFee, PaymentStatus.SetToSettle, PaymentStatus.Settle }), "VA status sequence");
    Check(new JsonPaymentRepository(paths).Find("T0")!.Status == PaymentStatus.Settle, "Persistence reload");
    var count = repository.Find("T0")!.Audit.Count;
    await flow.ProcessAsync(default);
    Check(repository.Find("T0")!.Audit.Count == count, "No repeated settled side effect");
    transfer.Upload();
    Check(File.Exists(Path.Combine(paths.Folder("remote-outbox"), "T0.txt")), "Upload simulator");
    var nested = Path.Combine(paths.Folder("remote-inbox"), "103");
    Directory.CreateDirectory(nested);
    File.WriteAllText(Path.Combine(nested, "incoming.txt"), Message("FILE"));
    File.WriteAllText(Path.Combine(paths.Folder("remote-inbox"), "invalid.txt"), "invalid");
    using var adapter = new VictoriaAdapter(flow, transfer, paths, options, NullLogger<VictoriaAdapter>.Instance);
    await adapter.StartAsync(default);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (repository.Find("FILE")?.Status != PaymentStatus.Settle)
        await Task.Delay(100, timeout.Token);
    await adapter.StopAsync(default);
    Check(Directory.GetFiles(paths.Folder("failed")).Length == 1, "Malformed file quarantine");
    Check(Directory.GetFiles(paths.Folder("completed")).Length == 1, "Input archived");
    Check(File.Exists(Path.Combine(paths.Folder("remote-outbox"), "FILE.txt")), "Background flow end to end");
    // Import simultan dengan reference sama hanya boleh menghasilkan satu transaksi.
    var imports = Enumerable.Range(0, 6).Select(async _ => {
        try { await flow.ImportAsync(Message("PARALLEL"), DemoScenario.Success, default); return true; }
        catch (InvalidOperationException) { return false; }
    });
    Check((await Task.WhenAll(imports)).Count(x => x) == 1, "Concurrent duplicate protection");
    Console.WriteLine($"PASS: {checks} checks (VA, non-VA, retry, audit, persistence, transfer, background worker, concurrency).");
}
finally
{
    // Hapus hanya direktori unik yang dibuat test ini di temporary directory.
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        Directory.Delete(root, true);
}
