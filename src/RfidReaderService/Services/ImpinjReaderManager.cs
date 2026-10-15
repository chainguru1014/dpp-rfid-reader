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
    private readonly object _sdkLock = new();

    private readonly ConcurrentQueue<TagReadDto> _recentTags = new();
    private readonly ConcurrentDictionary<string, TagReadDto> _tagSnapshot = new();

    private ImpinjReader? _reader;
    private bool _isStarted;
    private string? _lastError;
    private DateTime? _lastErrorAtUtc;

    public ImpinjReaderManager(ILogger<ImpinjReaderManager> logger, IOptions<ReaderOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public void Connect(string? hostnameOverride = null)
    {
        var hostname = hostnameOverride ?? _options.Hostname;
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new InvalidOperationException(
                "No reader hostname configured. Set Reader:Hostname in appsettings.json or pass one in the request.");
        }

        lock (_sdkLock)
        {
            if (_reader is { IsConnected: true })
            {
                throw new InvalidOperationException("Reader is already connected. Disconnect first.");
            }

            _reader ??= CreateReader();
            _reader.ConnectTimeout = _options.ConnectTimeoutMs;
            _reader.MessageTimeout = _options.MessageTimeoutMs;

            _logger.LogInformation("Connecting to reader at {Hostname}", hostname);
            _reader.Connect(hostname);

            ApplyConfiguredSettings(_reader);
            _logger.LogInformation("Connected to reader at {Hostname}", hostname);
        }
    }

    public void Disconnect()
    {
        lock (_sdkLock)
        {
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

    private void ApplyConfiguredSettings(ImpinjReader reader)
    {
        var settings = reader.QueryDefaultSettings();

        settings.Report.IncludeAntennaPortNumber = true;
        settings.Report.IncludePeakRssi = true;
        settings.Report.IncludeSeenCount = true;
        settings.Report.IncludeFirstSeenTime = true;
        settings.Report.IncludeLastSeenTime = true;
        settings.Report.IncludePhaseAngle = true;

        settings.RfMode = (uint)_options.RfMode;
        settings.SearchMode = Enum.Parse<SearchMode>(_options.SearchMode, ignoreCase: true);
        settings.Session = _options.Session;

        settings.Antennas.DisableAll();
        foreach (var port in _options.AntennaPorts)
        {
            var antenna = settings.Antennas.GetAntenna((ushort)port);
            antenna.IsEnabled = true;
            antenna.MaxTxPower = _options.MaxTxPower;
            if (!_options.MaxTxPower && _options.TxPowerInDbm is { } txPower)
            {
                antenna.TxPowerInDbm = txPower;
            }

            antenna.MaxRxSensitivity = _options.MaxRxSensitivity;
            if (!_options.MaxRxSensitivity && _options.RxSensitivityInDbm is { } rxSensitivity)
            {
                antenna.RxSensitivityInDbm = rxSensitivity;
            }
        }

        reader.ApplySettings(settings);
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
        _isStarted = false;
        _lastError = "Connection lost";
        _lastErrorAtUtc = DateTime.UtcNow;
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
