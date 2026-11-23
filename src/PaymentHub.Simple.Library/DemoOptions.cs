namespace PaymentHub.Simple.Library;

public sealed class DemoOptions
{
    public string DataDirectory { get; set; } = "data";
    public bool WorkerEnabled { get; set; } = true;
    public int PollSeconds { get; set; } = 2;
    public int MaxAttempts { get; set; } = 3;
    public int VaMinLength { get; set; } = 16;
    public int VaMaxLength { get; set; } = 18;
    public decimal Fee { get; set; } = 2500m;
    public string ParentAccount { get; set; } = "1000000001";

    public void Validate()
    {
        if (PollSeconds < 1 || MaxAttempts < 1 || VaMinLength < 1 || VaMaxLength < VaMinLength
            || Fee < 0 || string.IsNullOrWhiteSpace(DataDirectory)
            || ParentAccount.Length == 0 || !ParentAccount.All(char.IsAsciiDigit))
            throw new ArgumentException("Konfigurasi Demo tidak valid.");
    }
}

public sealed class DataPaths
{
    public string Root { get; }
    public DataPaths(string root) => Root = Path.GetFullPath(root);
    public string Folder(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
