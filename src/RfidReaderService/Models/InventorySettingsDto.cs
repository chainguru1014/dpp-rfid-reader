namespace RfidReaderService.Models;

public record InventorySettingsDto(
    uint? RfMode,
    string? SearchMode,
    ushort? Session,
    double? PowerDbm);
