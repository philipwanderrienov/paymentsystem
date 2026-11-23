using PaymentHub.Simple.Library;
namespace PaymentHub.Simple.TransferFile;

// Pengganti FTP untuk demo: remote-inbox -> incoming, cbs-outbox -> remote-outbox.
public sealed class LocalFileTransfer(DataPaths paths)
{
    public void Download() => Transfer("remote-inbox", "incoming");
    public void Upload() => Transfer("cbs-outbox", "remote-outbox");
    private void Transfer(string source, string target)
    {
        var input = paths.Folder(source);
        var output = paths.Folder(target);
        foreach (var file in Directory.EnumerateFiles(input, "*.txt", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(output, Path.GetRelativePath(input, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                // Replay sesudah restart hanya diterima bila isi persis sama.
                if (File.ReadAllText(file) != File.ReadAllText(destination))
                    throw new IOException("Konflik file transfer: " + destination);
                File.Delete(file);
            }
            else File.Move(file, destination);
        }
    }
}
