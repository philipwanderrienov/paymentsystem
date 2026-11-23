using System.Text.Json;
using System.Text.Json.Serialization;
using PaymentHub.Simple.Library;
using PaymentHub.Simple.Library.Rtgs;

namespace PaymentHub.Simple.Repository;

public interface IPaymentRepository
{
    IReadOnlyList<Payment> All();
    Payment? Find(string reference);
    void Save(Payment payment);
}

public sealed class JsonPaymentRepository(DataPaths paths) : IPaymentRepository
{
    private readonly object gate = new();
    private readonly string folder = paths.Folder("transactions");
    private static readonly JsonSerializerOptions Json = new() {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public IReadOnlyList<Payment> All()
    {
        lock (gate) return Directory.GetFiles(folder, "*.json")
            .Select(f => JsonSerializer.Deserialize<Payment>(File.ReadAllText(f), Json)!).ToArray();
    }

    public Payment? Find(string reference) => All().SingleOrDefault(p => string.Equals(p.Reference, reference, StringComparison.OrdinalIgnoreCase));

    public void Save(Payment payment)
    {
        // File key dari reference yang sudah divalidasi parser, bukan nama file input.
        lock (gate)
        {
            var file = Path.Combine(folder, payment.Reference + ".json");
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(payment, Json));
            File.Move(file + ".tmp", file, true);
        }
    }
}
