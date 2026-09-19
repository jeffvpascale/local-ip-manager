using LocalIPManager.Models;
using System.Net;

public class DeviceListItem
{
	public int? DeviceId { get; set; }
	public string? Name { get; set; }
	public string? Description { get; set; }
	public required string IpAddress { get; set; }
	public string? MacAddress { get; set; }
	public bool IsReserved { get; set; }
	public bool IsStatic { get; set; }
	public bool IsInDatabase => DeviceId.HasValue;
	public uint IpAddressSortKey
	{
		get
		{
			if (!IPAddress.TryParse(IpAddress, out var address))
				return uint.MaxValue;

			var bytes = address.GetAddressBytes();
			if (bytes.Length != 4)
				return uint.MaxValue;

			return ((uint)bytes[0] << 24) |
				((uint)bytes[1] << 16) |
				((uint)bytes[2] << 8) |
				bytes[3];
		}
	}

	public Device ToDevice()
	{
		return new Device
		{
			Id = DeviceId ?? 0,
			Name = Name ?? "",
			Description = Description,
			IpAddress = IpAddress,
			MacAddress = MacAddress,
			IsReserved = IsReserved,
			IsStatic = IsStatic
		};
	}
	public static DeviceListItem FromDevice(Device device)
	{
		return new DeviceListItem
		{
			DeviceId = device.Id,
			Name = device.Name,
			Description = device.Description,
			IpAddress = device.IpAddress,
			MacAddress = device.MacAddress,
			IsReserved = device.IsReserved,
			IsStatic = device.IsStatic,
		};
	}

	public static DeviceListItem FromScanResult(NetworkScanResult scanResult)
	{
		return new DeviceListItem
		{
			Name = scanResult.Name,
			IpAddress = scanResult.IpAddress,
			MacAddress = scanResult.MacAddress,
		};
	}
}