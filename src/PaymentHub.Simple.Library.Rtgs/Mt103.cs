using System.Globalization;
using System.Text.RegularExpressions;

namespace PaymentHub.Simple.Library.Rtgs;

// Subset MT103 untuk demo: :20:, :32A:, :59:. Tidak menggantikan serializer SWIFT produksi.
public static class Mt103
{
    public static Payment Parse(string content, DemoScenario scenario = DemoScenario.Success)
    {
        if (content.Length > 65536) throw new FormatException("File melebihi 64 KB.");
        var text = content.Replace("\r\n", "\n");
        string Field(string tag)
        {
            var matches = Regex.Matches(text, @"(?m)^:" + tag + @":([^\n]+)$");
            if (matches.Count != 1) throw new FormatException($"Tag {tag} harus tepat satu.");
            return matches[0].Groups[1].Value.Trim();
        }
        var reference = Field("20");
        if (!Regex.IsMatch(reference, @"\A[A-Za-z0-9_-]{1,16}\z"))
            throw new FormatException("Reference wajib 1-16 karakter huruf, angka, _ atau -.");
        var value = Regex.Match(Field("32A"), @"\A([0-9]{6})IDR([0-9]+,[0-9]{0,2})\z");
        if (!value.Success) throw new FormatException("32A harus yyMMddIDRnominal,desimal.");
        var dateText = value.Groups[1].Value;
        var date = DateOnly.ParseExact("20" + dateText, "yyyyMMdd", CultureInfo.InvariantCulture);
        var amount = decimal.Parse(value.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        if (amount <= 0) throw new FormatException("Nominal harus positif.");
        var account = Field("59").TrimStart('/');
        if (account.Length is < 1 or > 34 || !account.All(char.IsAsciiDigit))
            throw new FormatException("Rekening tidak valid.");
        var name = Regex.Match(text, @"(?m)^:59:[^\n]+\n([^:\n{}][^\n]*)").Groups[1].Value.Trim();
        return new Payment { Reference = reference, Account = account, Name = name,
            Amount = amount, PaidAmount = amount, ValueDate = date, OriginalMessage = content, Scenario = scenario };
    }

    public static string ForParent(Payment payment)
    {
        var content = Regex.Replace(payment.OriginalMessage, @"(?m)^:59:[^\r\n]+",
            ":59:/" + payment.ParentAccount);
        return Regex.Replace(content, @"(?m)^:32A:[^\r\n]+",
            ":32A:" + payment.ValueDate.ToString("yyMMdd", CultureInfo.InvariantCulture) + "IDR"
            + payment.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ','));
    }
}
