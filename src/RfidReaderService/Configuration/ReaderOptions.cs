namespace RfidReaderService.Configuration;

public class ReaderOptions
{
    public const string SectionName = "Reader";

    public string? Hostname { get; set; }

    public bool AutoConnect { get; set; }

    public int ConnectTimeoutMs { get; set; } = 5000;

    public int MessageTimeoutMs { get; set; } = 5000;

    // No default-value initializer here: the config binder appends bound array elements
    // onto an existing array value rather than replacing it, so a literal default like
    // "= [1]" combined with an appsettings.json value produces [1, 1]. Default applied
    // at the one place this is read (ReaderConnectionSettingsStore.SeedFromLegacyOptions).
    public int[]? AntennaPorts { get; set; }

    public bool MaxTxPower { get; set; } = true;

    public double? TxPowerInDbm { get; set; }

    public bool MaxRxSensitivity { get; set; } = true;

    public double? RxSensitivityInDbm { get; set; }

    public int RfMode { get; set; } = 1002;

    public string SearchMode { get; set; } = "DualTarget";

    public ushort Session { get; set; } = 2;

    public int MaxRecentTags { get; set; } = 500;

    public int TagSnapshotExpirySeconds { get; set; } = 10;

    /// <summary>
    /// Path (relative to the content root, unless rooted) where the mutable connection
    /// settings managed via GET/PUT /api/reader/settings are persisted. The properties
    /// above are only used to seed this file the first time it doesn't exist yet.
    /// </summary>
    public string ConnectionSettingsFilePath { get; set; } = "reader-connection-settings.json";
}
