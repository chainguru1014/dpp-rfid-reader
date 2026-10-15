namespace RfidReaderService.Models;

public record ReaderStatusDto(
    bool IsConnected,
    bool IsStarted,
    string? Address,
    string? FirmwareVersion,
    string? ModelName,
    short? TemperatureInCelsius,
    string? LastError,
    DateTime? LastErrorAtUtc);
