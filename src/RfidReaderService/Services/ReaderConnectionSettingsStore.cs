using System.Text.Json;
using Impinj.OctaneSdk;
using Microsoft.Extensions.Options;
using RfidReaderService.Configuration;
using RfidReaderService.Models;

namespace RfidReaderService.Services;

/// <summary>
/// Owns the mutable reader connection/inventory settings exposed via GET/PUT
/// /api/reader/settings. Persisted to a JSON file (<see cref="ReaderOptions.ConnectionSettingsFilePath"/>)
/// so they survive a service restart, seeded from Reader:* in appsettings.json the first
/// time that file doesn't exist yet.
/// </summary>
public class ReaderConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ILogger<ReaderConnectionSettingsStore> _logger;
    private readonly string _filePath;
    private readonly object _lock = new();
    private ReaderConnectionSettingsDto _current;

    public ReaderConnectionSettingsStore(
        IOptions<ReaderOptions> options,
        IHostEnvironment environment,
        ILogger<ReaderConnectionSettingsStore> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(environment.ContentRootPath, options.Value.ConnectionSettingsFilePath);
        _current = Load(options.Value);
    }

    public ReaderConnectionSettingsDto Get()
    {
        lock (_lock)
        {
            return _current;
        }
    }

    /// <summary>
    /// Applies a full replacement of the settings (PUT semantics). <see cref="ReaderConnectionSettingsDto.AvailableAntennaPorts"/>
    /// is a reader capability discovered on connect, not client-settable, so whatever the
    /// request carries for it is ignored in favor of the store's own value.
    /// </summary>
    public ReaderConnectionSettingsDto Update(ReaderConnectionSettingsDto request)
    {
        Validate(request);

        lock (_lock)
        {
            var toSave = request with { AvailableAntennaPorts = _current.AvailableAntennaPorts };
            Save(toSave);
            _current = toSave;
            return _current;
        }
    }

    public void UpdateAvailableAntennaPorts(int[] ports)
    {
        lock (_lock)
        {
            var updated = _current with { AvailableAntennaPorts = ports };
            Save(updated);
            _current = updated;
        }
    }

    private static void Validate(ReaderConnectionSettingsDto s)
    {
        if (string.IsNullOrWhiteSpace(s.IpAddress))
        {
            throw new ArgumentException("ipAddress is required.");
        }

        if (s.Port is < 1 or > 65535)
        {
            throw new ArgumentException("port must be between 1 and 65535.");
        }

        if (s.ConnectionTimeoutMs <= 0)
        {
            throw new ArgumentException("connectionTimeoutMs must be greater than zero.");
        }

        if (s.AutoReconnect)
        {
            if (s.ReconnectIntervalMs <= 0)
            {
                throw new ArgumentException("reconnectIntervalMs must be greater than zero.");
            }

            if (!s.UnlimitedReconnectAttempts && s.MaxReconnectAttempts <= 0)
            {
                throw new ArgumentException(
                    "maxReconnectAttempts must be greater than zero unless unlimitedReconnectAttempts is set.");
            }
        }

        if (s.EnabledAntennaPorts is null || s.EnabledAntennaPorts.Length == 0)
        {
            throw new ArgumentException("At least one antenna port must be enabled.");
        }

        foreach (var port in s.EnabledAntennaPorts)
        {
            if (port < 1)
            {
                throw new ArgumentException($"Invalid antenna port: {port}.");
            }
        }

        if (s.InventorySettings.SearchMode is { } searchMode &&
            !Enum.TryParse<SearchMode>(searchMode, ignoreCase: true, out _))
        {
            throw new ArgumentException($"Unrecognized searchMode '{searchMode}'.");
        }
    }

    private ReaderConnectionSettingsDto Load(ReaderOptions options)
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<ReaderConnectionSettingsDto>(json, JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read {Path}, falling back to defaults from configuration", _filePath);
            }
        }

        var seeded = SeedFromLegacyOptions(options);
        Save(seeded);
        return seeded;
    }

    private static ReaderConnectionSettingsDto SeedFromLegacyOptions(ReaderOptions options)
    {
        const int defaultLlrpPort = 5084;

        return new ReaderConnectionSettingsDto(
            ReaderName: "Main RFID Reader",
            IpAddress: options.Hostname ?? string.Empty,
            Port: defaultLlrpPort,
            ConnectionTimeoutMs: options.ConnectTimeoutMs,
            AutoReconnect: false,
            ReconnectIntervalMs: 5000,
            MaxReconnectAttempts: 10,
            UnlimitedReconnectAttempts: false,
            AutoConnectOnStartup: options.AutoConnect,
            AvailableAntennaPorts: null,
            EnabledAntennaPorts: options.AntennaPorts is { Length: > 0 } ? options.AntennaPorts : [1],
            InventorySettings: new InventorySettingsDto(
                RfMode: (uint)options.RfMode,
                SearchMode: options.SearchMode,
                Session: options.Session,
                PowerDbm: options.MaxTxPower ? null : options.TxPowerInDbm));
    }

    private void Save(ReaderConnectionSettingsDto settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
