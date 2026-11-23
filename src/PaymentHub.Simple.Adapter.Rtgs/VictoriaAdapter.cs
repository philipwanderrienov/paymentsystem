using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentHub.Simple.Library;
using PaymentHub.Simple.Library.Rtgs;
using PaymentHub.Simple.Service;
using PaymentHub.Simple.TransferFile;
namespace PaymentHub.Simple.Adapter.Rtgs;

public sealed class VictoriaAdapter(PaymentFlow flow, LocalFileTransfer transfer, DataPaths paths,
    DemoOptions options, ILogger<VictoriaAdapter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                transfer.Download();
                foreach (var file in Directory.EnumerateFiles(paths.Folder("incoming"), "*.txt", SearchOption.AllDirectories))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    var destination = "completed";
                    try { await flow.ImportAsync(await File.ReadAllTextAsync(file, stoppingToken), DemoScenario.Success, stoppingToken); }
                    catch (InvalidOperationException ex) { logger.LogWarning("Duplicate {File}: {Reason}", file, ex.Message); }
                    catch (Exception ex) when (ex is FormatException or OverflowException)
                    {
                        destination = "failed";
                        logger.LogWarning("Invalid MT103 {File}: {Reason}", file, ex.Message);
                    }
                    var archive = Path.Combine(paths.Folder(destination), Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(file));
                    File.Move(file, archive);
                }
                await flow.ProcessAsync(stoppingToken);
                transfer.Upload();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Victoria adapter cycle failed; retry next poll."); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
