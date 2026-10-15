namespace RfidReaderService.Configuration;

public class ReaderOptions
{
    public const string SectionName = "Reader";

    public string? Hostname { get; set; }

    public bool AutoConnect { get; set; }

    public int ConnectTimeoutMs { get; set; } = 5000;

    public int MessageTimeoutMs { get; set; } = 5000;

    public int[] AntennaPorts { get; set; } = [1];

    public bool MaxTxPower { get; set; } = true;

    public double? TxPowerInDbm { get; set; }

    public bool MaxRxSensitivity { get; set; } = true;

    public double? RxSensitivityInDbm { get; set; }

    public int RfMode { get; set; } = 1002;

    public string SearchMode { get; set; } = "DualTarget";

    public ushort Session { get; set; } = 2;

    public int MaxRecentTags { get; set; } = 500;

    public int TagSnapshotExpirySeconds { get; set; } = 10;
}
