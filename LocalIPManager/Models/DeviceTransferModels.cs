namespace LocalIPManager.Models;

public sealed class DeviceExportFile
{
    public int Version { get; init; } = 1;
    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<DeviceTransferRecord> Devices { get; init; } = [];
}

public sealed class DeviceTransferRecord
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? IpAddress { get; init; }
    public string? MacAddress { get; init; }
    public bool IsReserved { get; init; }
    public bool IsStatic { get; init; }
}

public sealed record DeviceImportResult(
    int ImportedCount,
    int DuplicateCount,
    int InvalidCount);
