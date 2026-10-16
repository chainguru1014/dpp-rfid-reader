namespace RfidReaderService.Services;

/// <summary>
/// On startup, connects to and starts the reader if the persisted connection settings
/// (GET/PUT /api/reader/settings) have AutoConnectOnStartup enabled.
/// On shutdown, always attempts a clean disconnect.
/// </summary>
public class ReaderAutoConnectHostedService(
    ImpinjReaderManager manager,
    ReaderConnectionSettingsStore settingsStore,
    ILogger<ReaderAutoConnectHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!settingsStore.Get().AutoConnectOnStartup)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                manager.Connect();
                manager.StartReading();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-connect to reader failed on startup");
            }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            manager.Disconnect();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error disconnecting reader during shutdown");
        }

        return Task.CompletedTask;
    }
}
