using System.Collections.Concurrent;
using Impinj.OctaneSdk;
using Microsoft.Extensions.Options;
using RfidReaderService.Configuration;
using RfidReaderService.Models;

namespace RfidReaderService.Services;

/// <summary>
/// Owns the single <see cref="ImpinjReader"/> connection for this process and exposes
/// thread-safe operations plus an in-memory buffer of recent tag reads for the API layer.
/// The Octane SDK reader instance is not safe for concurrent calls, so every SDK call
/// is serialized through <see cref="_sdkLock"/>.
/// </summary>
public class ImpinjReaderManager : IDisposable
{
    private readonly ILogger<ImpinjReaderManager> _logger;
    private readonly ReaderOptions _options;
    private readonly ReaderConnectionSettingsStore _settingsStore;
    private readonly object _sdkLock = new();

    private readonly ConcurrentQueue<TagReadDto> _recentTags = new();
    private readonly ConcurrentDictionary<string, TagReadDto> _tagSnapshot = new();

    private ImpinjReader? _reader;
    private bool _isStarted;
    private string? _lastError;
    private DateTime? _lastErrorAtUtc;

    // Bumped on every manual Connect()/Disconnect() so a background reconnect loop started
    // after a dropped connection can notice a manual action happened and abandon itself
    // instead of fighting it.
    private int _connectionGeneration;

    public ImpinjReaderManager(
        ILogger<ImpinjReaderManager> logger,
        IOptions<ReaderOptions> options,
        ReaderConnectionSettingsStore settingsStore)
    {
        _logger = logger;
        _options = options.Value;
        _settingsStore = settingsStore;
    }

    public void Connect(string? hostnameOverride = null)
    {
        var connectionSettings = _settingsStore.Get();
        var hostname = hostnameOverride ?? connectionSettings.IpAddress;
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new InvalidOperationException(
                "No reader IP address configured. Set it via PUT /api/reader/settings or pass one in the request.");
        }

        lock (_sdkLock)
        {
            if (_reader is { IsConnected: true })
            {
                throw new InvalidOperationException("Reader is already connected. Disconnect first.");
            }

            _connectionGeneration++;

            _reader ??= CreateReader();
            _reader.ConnectTimeout = connectionSettings.ConnectionTimeoutMs;
            _reader.MessageTimeout = _options.MessageTimeoutMs;

            _logger.LogInformation("Connecting to reader at {Hostname}:{Port}", hostname, connectionSettings.Port);
            _reader.Connect(hostname, connectionSettings.Port);

            ApplyConfiguredSettings(_reader, connectionSettings);
            TryUpdateAvailableAntennaPorts(_reader);

            _logger.LogInformation("Connected to reader at {Hostname}:{Port}", hostname, connectionSettings.Port);
        }
    }

    public void Disconnect()
    {
        lock (_sdkLock)
        {
            _connectionGeneration++;

            if (_reader is null)
            {
                return;
            }

            if (_isStarted)
            {
                TryStopInternal();
            }

            _logger.LogInformation("Disconnecting reader at {Address}", _reader.Address);
            _reader.Disconnect();
        }
    }

    public void StartReading()
    {
        lock (_sdkLock)
        {
            RequireConnected();
            _reader!.Start();
            _isStarted = true;
        }
    }

    public void StopReading()
    {
        lock (_sdkLock)
        {
            RequireConnected();
            TryStopInternal();
        }
    }

    public ReaderStatusDto GetStatus()
    {
        lock (_sdkLock)
        {
            var isConnected = _reader is { IsConnected: true };
            short? temperature = null;

            if (isConnected)
            {
                try
                {
                    temperature = _reader!.QueryStatus().TemperatureInCelsius;
                }
                catch (OctaneSdkException ex)
                {
                    _logger.LogWarning(ex, "Failed to query reader status");
                }
            }

            string? firmwareVersion = null;
            string? modelName = null;
            if (isConnected)
            {
                try
                {
                    var featureSet = _reader!.QueryFeatureSet();
                    firmwareVersion = featureSet.FirmwareVersion;
                    modelName = featureSet.ModelName;
                }
                catch (OctaneSdkException ex)
                {
                    _logger.LogWarning(ex, "Failed to query reader feature set");
                }
            }

            return new ReaderStatusDto(
                isConnected,
                _isStarted,
                _reader?.Address,
                firmwareVersion,
                modelName,
                temperature,
                _lastError,
                _lastErrorAtUtc);
        }
    }

    public IReadOnlyCollection<TagReadDto> GetRecentTags(int count)
    {
        return _recentTags.ToArray().TakeLast(Math.Max(0, count)).ToList();
    }

    public IReadOnlyCollection<TagReadDto> GetTagSnapshot()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_options.TagSnapshotExpirySeconds);
        return _tagSnapshot.Values
            .Where(t => t.LastSeenTimeUtc is null || t.LastSeenTimeUtc >= cutoff)
            .ToList();
    }

    public void ClearTagBuffers()
    {
        _recentTags.Clear();
        _tagSnapshot.Clear();
    }

    private ImpinjReader CreateReader()
    {
        var reader = new ImpinjReader();
        reader.TagsReported += OnTagsReported;
        reader.ConnectionLost += OnConnectionLost;
        reader.ErrorNotification += OnErrorNotification;
        return reader;
    }

    private void ApplyConfiguredSettings(ImpinjReader reader, ReaderConnectionSettingsDto connectionSettings)
    {
        var settings = reader.QueryDefaultSettings();

        settings.Report.IncludeAntennaPortNumber = true;
        settings.Report.IncludePeakRssi = true;
        settings.Report.IncludeSeenCount = true;
        settings.Report.IncludeFirstSeenTime = true;
        settings.Report.IncludeLastSeenTime = true;
        settings.Report.IncludePhaseAngle = true;

        var inventory = connectionSettings.InventorySettings;

        if (inventory.RfMode is { } rfMode)
        {
            settings.RfMode = rfMode;
        }

        if (inventory.SearchMode is { } searchModeName &&
            Enum.TryParse<SearchMode>(searchModeName, ignoreCase: true, out var searchMode))
        {
            settings.SearchMode = searchMode;
        }

        if (inventory.Session is { } session)
        {
            settings.Session = session;
        }

        settings.Antennas.DisableAll();
        foreach (var port in connectionSettings.EnabledAntennaPorts)
        {
            var antenna = settings.Antennas.GetAntenna((ushort)port);
            antenna.IsEnabled = true;

            if (inventory.PowerDbm is { } powerDbm)
            {
                antenna.MaxTxPower = false;
                antenna.TxPowerInDbm = powerDbm;
            }
            else
            {
                antenna.MaxTxPower = true;
            }

            antenna.MaxRxSensitivity = _options.MaxRxSensitivity;
            if (!_options.MaxRxSensitivity && _options.RxSensitivityInDbm is { } rxSensitivity)
            {
                antenna.RxSensitivityInDbm = rxSensitivity;
            }
        }

        reader.ApplySettings(settings);
    }

    private void TryUpdateAvailableAntennaPorts(ImpinjReader reader)
    {
        try
        {
            var featureSet = reader.QueryFeatureSet();
            var ports = Enumerable.Range(1, (int)featureSet.AntennaCount).ToArray();
            _settingsStore.UpdateAvailableAntennaPorts(ports);
        }
        catch (OctaneSdkException ex)
        {
            _logger.LogWarning(ex, "Failed to query reader feature set for available antenna ports");
        }
    }

    private void TryStopInternal()
    {
        try
        {
            _reader!.Stop();
        }
        finally
        {
            _isStarted = false;
        }
    }

    private void RequireConnected()
    {
        if (_reader is not { IsConnected: true })
        {
            throw new InvalidOperationException("Reader is not connected.");
        }
    }

    private void OnTagsReported(ImpinjReader sender, TagReport report)
    {
        foreach (Tag tag in report)
        {
            var dto = new TagReadDto(
                tag.Epc.ToHexString(),
                tag.AntennaPortNumber,
                tag.IsPeakRssiInDbmPresent ? tag.PeakRssiInDbm : null,
                tag.IsSeenCountPresent ? tag.TagSeenCount : null,
                tag.IsFirstSeenTimePresent ? tag.FirstSeenTime.LocalDateTime.ToUniversalTime() : null,
                tag.IsLastSeenTimePresent ? tag.LastSeenTime.LocalDateTime.ToUniversalTime() : null,
                tag.IsRfPhaseAnglePresent ? tag.PhaseAngleInRadians : null);

            _tagSnapshot[dto.Epc] = dto;

            _recentTags.Enqueue(dto);
            while (_recentTags.Count > _options.MaxRecentTags && _recentTags.TryDequeue(out _))
            {
            }
        }
    }

    private void OnConnectionLost(ImpinjReader reader)
    {
        _logger.LogWarning("Connection lost to reader at {Address}", reader.Address);
        var resumeReading = _isStarted;
        _isStarted = false;
        _lastError = "Connection lost";
        _lastErrorAtUtc = DateTime.UtcNow;

        var connectionSettings = _settingsStore.Get();
        if (connectionSettings.AutoReconnect)
        {
            var generation = _connectionGeneration;
            _ = Task.Run(() => ReconnectLoopAsync(generation, resumeReading));
        }
    }

    private async Task ReconnectLoopAsync(int generation, bool resumeReading)
    {
        var attempt = 0;

        while (true)
        {
            var connectionSettings = _settingsStore.Get();
            if (!connectionSettings.UnlimitedReconnectAttempts && attempt >= connectionSettings.MaxReconnectAttempts)
            {
                _logger.LogError("Giving up reconnecting to reader after {Attempts} attempt(s)", attempt);
                return;
            }

            if (generation != _connectionGeneration)
            {
                _logger.LogInformation("Abandoning reconnect loop; reader state changed manually");
                return;
            }

            attempt++;
            await Task.Delay(connectionSettings.ReconnectIntervalMs);

            if (generation != _connectionGeneration)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Reconnect attempt {Attempt} to reader", attempt);
                Connect();
                if (resumeReading)
                {
                    StartReading();
                }

                _logger.LogInformation("Reconnected to reader after {Attempt} attempt(s)", attempt);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect attempt {Attempt} failed", attempt);
            }
        }
    }

    private void OnErrorNotification(ImpinjReader reader, Exception error)
    {
        _logger.LogError(error, "Unhandled Octane SDK error from reader at {Address}", reader.Address);
        _lastError = error.Message;
        _lastErrorAtUtc = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            if (_reader.IsConnected)
            {
                Disconnect();
            }
        }
        catch (OctaneSdkException ex)
        {
            _logger.LogWarning(ex, "Error while disconnecting reader during shutdown");
        }
    }
}
