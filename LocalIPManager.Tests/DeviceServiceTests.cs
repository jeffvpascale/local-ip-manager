using LocalIPManager.Data;
using LocalIPManager.Models;
using LocalIPManager.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalIPManager.Tests;

public class DeviceServiceTests
{
	[Fact]
	public async Task AddDeviceAsync_WithUniqueIpAddress_AddsDevice()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		// Act
		var result = await service.AddDeviceAsync(device);

		// Assert
		Assert.True(result.Success);

		await using var verificationDb = new DeviceDbContext(options);
		var savedDevice = await verificationDb.Devices.SingleAsync();

		Assert.Equal("Test Router", savedDevice.Name);
		Assert.Equal("192.168.1.1", savedDevice.IpAddress);
	}

	[Fact]
	public async Task AddDeviceAsync_WithDuplicateIpAddress_ReturnsFailure()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		var duplicateDevice = new Device
		{
			Name = "Test Router 2",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:02"
		};

		// Act
		var firstResult = await service.AddDeviceAsync(device);
		var duplicateResult = await service.AddDeviceAsync(duplicateDevice);

		// Assert
		Assert.True(firstResult.Success);
		Assert.False(duplicateResult.Success);

		await using var verificationDb = new DeviceDbContext(options);

		Assert.Equal(1, await verificationDb.Devices.CountAsync());
	}


	[Fact]
	public async Task AddDeviceAsync_WithDuplicateMacAddress_ReturnsFailure()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		var duplicateDevice = new Device
		{
			Name = "Test Router 2",
			IpAddress = "192.168.1.2",
			MacAddress = "02:00:00:00:00:01"
		};

		// Act
		var firstResult = await service.AddDeviceAsync(device);
		var duplicateResult = await service.AddDeviceAsync(duplicateDevice);

		// Assert
		Assert.True(firstResult.Success);
		Assert.False(duplicateResult.Success);

		await using var verificationDb = new DeviceDbContext(options);

		Assert.Equal(1, await verificationDb.Devices.CountAsync());
	}

	[Fact]
	public async Task UpdateDeviceAsync_WithUnchangedAddresses_ReturnsSuccess()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		var addResult = await service.AddDeviceAsync(device);

		device.Name = "Test Router 2";

		var updateResult = await service.UpdateDeviceAsync(device);

		// Assert
		Assert.True(addResult.Success);
		Assert.True(updateResult.Success);

		await using var verificationDb = new DeviceDbContext(options);
		var savedDevice = await verificationDb.Devices.SingleAsync();

		Assert.Equal("Test Router 2", savedDevice.Name);
		Assert.Equal("192.168.1.1", savedDevice.IpAddress);
		Assert.Equal("02:00:00:00:00:01", savedDevice.MacAddress);
	}

	[Fact]
	public async Task UpdateDeviceAsync_WithAnotherDevicesIpAddress_ReturnsFailure()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		var addResult = await service.AddDeviceAsync(device);

		var device2 = new Device
		{
			Name = "Test Router 2",
			IpAddress = "192.168.1.2",
			MacAddress = "02:00:00:00:00:02"
		};

		var addResult2 = await service.AddDeviceAsync(device2);

		device2.IpAddress = device.IpAddress;

		var updateResult = await service.UpdateDeviceAsync(device2);

		// Assert
		Assert.True(addResult.Success);
		Assert.True(addResult2.Success);
		Assert.False(updateResult.Success);

		await using var verificationDb = new DeviceDbContext(options);

		var savedDevice2 = await verificationDb.Devices
			.SingleAsync(d => d.Id == device2.Id);

		Assert.Equal("192.168.1.2", savedDevice2.IpAddress);
		Assert.Equal(2, await verificationDb.Devices.CountAsync());
	}

	[Fact]
	public async Task UpdateDeviceAsync_WithAnotherDevicesMacAddress_ReturnsFailure()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		var addResult = await service.AddDeviceAsync(device);

		var device2 = new Device
		{
			Name = "Test Router 2",
			IpAddress = "192.168.1.2",
			MacAddress = "02:00:00:00:00:02"
		};

		var addResult2 = await service.AddDeviceAsync(device2);

		device2.MacAddress = device.MacAddress;

		var updateResult = await service.UpdateDeviceAsync(device2);

		// Assert
		Assert.True(addResult.Success);
		Assert.True(addResult2.Success);
		Assert.False(updateResult.Success);

		await using var verificationDb = new DeviceDbContext(options);

		var savedDevice2 = await verificationDb.Devices
			.SingleAsync(d => d.Id == device2.Id);

		Assert.Equal("02:00:00:00:00:02", savedDevice2.MacAddress);
		Assert.Equal(2, await verificationDb.Devices.CountAsync());
	}

	[Fact]
	public async Task DeleteDeviceAsync_WithExistingDevice_RemovesDevice()
	{
		// Arrange
		await using var connection =
			new SqliteConnection("Data Source=:memory:");

		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DeviceDbContext>()
			.UseSqlite(connection)
			.Options;

		await using (var db = new DeviceDbContext(options))
		{
			await db.Database.EnsureCreatedAsync();
		}

		var factory = new TestDbContextFactory(options);
		var service = new DeviceService(factory);

		var device = new Device
		{
			Name = "Test Router",
			IpAddress = "192.168.1.1",
			MacAddress = "02:00:00:00:00:01"
		};

		var addResult = await service.AddDeviceAsync(device);

		// Act
		var deleteResult = await service.DeleteDeviceAsync(device.Id);

		// Assert
		Assert.True(addResult.Success);
		Assert.True(deleteResult.Success);

		await using var verificationDb = new DeviceDbContext(options);

		Assert.Equal(0, await verificationDb.Devices.CountAsync());
	}

	private sealed class TestDbContextFactory(
		DbContextOptions<DeviceDbContext> options)
		: IDbContextFactory<DeviceDbContext>
	{
		public DeviceDbContext CreateDbContext()
		{
			return new DeviceDbContext(options);
		}
	}
}