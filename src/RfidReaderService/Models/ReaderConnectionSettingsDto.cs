namespace RfidReaderService.Models;

public record ReaderConnectionSettingsDto(
    string ReaderName,
    string IpAddress,
    int Port,
    int ConnectionTimeoutMs,
    bool AutoReconnect,
    int ReconnectIntervalMs,
    int MaxReconnectAttempts,
    bool UnlimitedReconnectAttempts,
    bool AutoConnectOnStartup,
    int[]? AvailableAntennaPorts,
    int[] EnabledAntennaPorts,
    InventorySettingsDto InventorySettings);
