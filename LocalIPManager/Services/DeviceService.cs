using LocalIPManager.Data;
using LocalIPManager.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalIPManager.Services
{
	public class DeviceService
	{
		private readonly IDbContextFactory<DeviceDbContext> _deviceDbContextFactory;

		public DeviceService(IDbContextFactory<DeviceDbContext> deviceDbContextFactory)
		{
			_deviceDbContextFactory = deviceDbContextFactory;
		}

		public async Task<List<Device>> GetAllDevicesAsync()
		{
			await using var db = await _deviceDbContextFactory.CreateDbContextAsync();
			var deviceList = await db.Devices.AsNoTracking().ToListAsync();
			return deviceList;
		}

		public async Task<Device?> GetDeviceAsync(int deviceID)
		{
			await using var db = await _deviceDbContextFactory.CreateDbContextAsync();
			var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceID);
			return device;
		}

		public async Task<List<Device>> SearchDevicesByStringAsync(string searchString)
		{
			await using var db = await _deviceDbContextFactory.CreateDbContextAsync();

			var searchResultsList = await db.Devices
				.AsNoTracking()
				.Where(d =>
				d.Name.Contains(searchString) ||
				d.IpAddress.Contains(searchString) ||
				(d.Description != null && d.Description.Contains(searchString)) ||
				(d.MacAddress != null && d.MacAddress.Contains(searchString)))
			.ToListAsync();

			return searchResultsList;
		}

		public async Task<(bool Success, string Message)> AddDeviceAsync(Device device)
		{
			await using var db = await _deviceDbContextFactory.CreateDbContextAsync();
			if (await HasAddressConflictAsync(db, device))
			{
				return (false, "Provide unique IP address and MAC Address");
			}

			db.Devices.Add(device);
			await db.SaveChangesAsync();
			return (true, "Device added successfully.");
		}

		public async Task<(bool Success,string Message)> UpdateDeviceAsync(Device device)
		{
			await using var db = await _deviceDbContextFactory.CreateDbContextAsync();

			if (await HasAddressConflictAsync(db, device))
			{
				return (false, "Provide unique IP address and MAC Address");
			}

			var existingDevice = await db.Devices.FirstOrDefaultAsync(x => x.Id == device.Id);

			if (existingDevice == null) return (false, "Device does not exist");

			existingDevice.Name = device.Name;
			existingDevice.Description = device.Description;
			existingDevice.IpAddress = device.IpAddress;
			existingDevice.MacAddress = device.MacAddress;
			existingDevice.IsReserved = device.IsReserved;
			existingDevice.IsStatic = device.IsStatic;

			await db.SaveChangesAsync();
			return (true, "Device updated successfully.");
		}

		public async Task<(bool Success, string Message)> DeleteDeviceAsync(int deviceId)
		{
			await using var db = await _deviceDbContextFactory.CreateDbContextAsync();
			var existingDevice = await db.Devices.FirstOrDefaultAsync(x => x.Id == deviceId);

			if (existingDevice == null) return (false, "Device does not exist");
			db.Remove(existingDevice);

			await db.SaveChangesAsync();
			return (true, "Device deleted successfully.");
		}

		private static Task<bool> HasAddressConflictAsync(
			DeviceDbContext db,
			Device device)
				{
					return db.Devices.AnyAsync(d =>
						d.Id != device.Id &&
						(
							d.IpAddress == device.IpAddress ||
							(device.MacAddress != null &&
							 d.MacAddress != null &&
							 d.MacAddress.ToLower() == device.MacAddress.ToLower())
						));
				}
	}
}
