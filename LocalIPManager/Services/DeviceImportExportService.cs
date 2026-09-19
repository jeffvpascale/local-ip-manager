using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LocalIPManager.Data;
using LocalIPManager.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalIPManager.Services;

public sealed class DeviceImportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IDbContextFactory<DeviceDbContext> _dbContextFactory;

    public DeviceImportExportService(IDbContextFactory<DeviceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<string> ExportAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var records = await db.Devices
            .AsNoTracking()
            .OrderBy(device => device.IpAddress)
            .Select(device => new DeviceTransferRecord
            {
                Name = device.Name,
                Description = device.Description,
                IpAddress = device.IpAddress,
                MacAddress = device.MacAddress,
                IsReserved = device.IsReserved,
                IsStatic = device.IsStatic
            })
            .ToListAsync();

        return JsonSerializer.Serialize(
            new DeviceExportFile { Devices = records },
            JsonOptions);
    }

    public async Task<DeviceImportResult> ImportAsync(Stream stream)
    {
        DeviceExportFile? exportFile;

        try
        {
            exportFile = await JsonSerializer.DeserializeAsync<DeviceExportFile>(stream, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The selected file is not a valid Local IP Manager export.",
                exception);
        }

        if (exportFile is null || exportFile.Version != 1 || exportFile.Devices is null)
            throw new InvalidDataException("The selected file uses an unsupported export format.");

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var existingDevices = await db.Devices
            .AsNoTracking()
            .Select(device => new { device.IpAddress, device.MacAddress })
            .ToListAsync();

        var ipAddresses = existingDevices
            .Select(device => device.IpAddress)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var macAddresses = existingDevices
            .Where(device => !string.IsNullOrWhiteSpace(device.MacAddress))
            .Select(device => device.MacAddress!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var importedCount = 0;
        var duplicateCount = 0;
        var invalidCount = 0;

        foreach (var record in exportFile.Devices)
        {
            var device = new Device
            {
                Name = record.Name?.Trim() ?? string.Empty,
                Description = string.IsNullOrWhiteSpace(record.Description)
                    ? null
                    : record.Description.Trim(),
                IpAddress = record.IpAddress?.Trim() ?? string.Empty,
                MacAddress = string.IsNullOrWhiteSpace(record.MacAddress)
                    ? null
                    : record.MacAddress.Trim().ToUpperInvariant(),
                IsReserved = record.IsReserved,
                IsStatic = record.IsStatic
            };

            if (!Validator.TryValidateObject(
                device,
                new ValidationContext(device),
                [],
                validateAllProperties: true))
            {
                invalidCount++;
                continue;
            }

            var hasDuplicateIp = ipAddresses.Contains(device.IpAddress);
            var hasDuplicateMac =
                device.MacAddress is not null && macAddresses.Contains(device.MacAddress);

            if (hasDuplicateIp || hasDuplicateMac)
            {
                duplicateCount++;
                continue;
            }

            db.Devices.Add(device);
            ipAddresses.Add(device.IpAddress);
            if (device.MacAddress is not null)
                macAddresses.Add(device.MacAddress);

            importedCount++;
        }

        await db.SaveChangesAsync();

        return new DeviceImportResult(importedCount, duplicateCount, invalidCount);
    }
}
