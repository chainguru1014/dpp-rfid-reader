namespace RfidReaderService.Models;

public record TagReadDto(
    string Epc,
    int AntennaPort,
    double? PeakRssiInDbm,
    ushort? TagSeenCount,
    DateTime? FirstSeenTimeUtc,
    DateTime? LastSeenTimeUtc,
    double? PhaseAngleInRadians);
