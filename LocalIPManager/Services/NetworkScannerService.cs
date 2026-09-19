using LocalIPManager.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace LocalIPManager.Services
{
	public class NetworkScannerService
	{
		public async Task<List<NetworkScanResult>> ScanAsync()
		{
			var results = new List<NetworkScanResult>();

			var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

			var activeNetworkInterface = networkInterfaces.FirstOrDefault(n =>
				n.OperationalStatus == OperationalStatus.Up &&
				n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
				n.GetIPProperties().GatewayAddresses.Any(g =>
					g.Address.AddressFamily == AddressFamily.InterNetwork));

			if (activeNetworkInterface == null)
				return results;

			var ipProperties = activeNetworkInterface.GetIPProperties();

			var ipv4Address = ipProperties.UnicastAddresses.FirstOrDefault(a =>
				a.Address.AddressFamily == AddressFamily.InterNetwork);

			if (ipv4Address == null)
				return results;

			var localMacAddress = string.Join(":",
				activeNetworkInterface
					.GetPhysicalAddress()
					.GetAddressBytes()
					.Select(b => b.ToString("X2")));

			var ipBytes = ipv4Address.Address.GetAddressBytes();
			var maskBytes = ipv4Address.IPv4Mask.GetAddressBytes();

			var networkBytes = new byte[4];
			var broadcastBytes = new byte[4];

			for (var i = 0; i < 4; i++)
			{
				networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
				broadcastBytes[i] = (byte)(networkBytes[i] | ~maskBytes[i]);
			}

			var networkAddress = new IPAddress(networkBytes);
			var broadcastAddress = new IPAddress(broadcastBytes);

			var addressesToScan =
				GetHostAddresses(networkAddress, broadcastAddress);

			var scanResults = new NetworkScanResult?[addressesToScan.Count];

			await Parallel.ForEachAsync(
				Enumerable.Range(0, addressesToScan.Count),
				new ParallelOptions { MaxDegreeOfParallelism = 64 },
				async (index, _) =>
				{
					// Each worker writes to its own slot, preserving address order.
					scanResults[index] = await ScanAddressAsync(
						addressesToScan[index],
						ipv4Address.Address,
						localMacAddress);
				});

			results = scanResults.OfType<NetworkScanResult>().ToList();

			return results;
		}

		private List<IPAddress> GetHostAddresses(
			IPAddress networkAddress,
			IPAddress broadcastAddress)
		{
			var addresses = new List<IPAddress>();

			var networkBytes = networkAddress.GetAddressBytes();
			var broadcastBytes = broadcastAddress.GetAddressBytes();

			uint network =
				((uint)networkBytes[0] << 24) |
				((uint)networkBytes[1] << 16) |
				((uint)networkBytes[2] << 8) |
				networkBytes[3];

			uint broadcast =
				((uint)broadcastBytes[0] << 24) |
				((uint)broadcastBytes[1] << 16) |
				((uint)broadcastBytes[2] << 8) |
				broadcastBytes[3];

			for (var address = network + 1; address < broadcast; address++)
			{
				var bytes = new[]
				{
					(byte)(address >> 24),
					(byte)(address >> 16),
					(byte)(address >> 8),
					(byte)address
				};

				addresses.Add(new IPAddress(bytes));
			}

			return addresses;
		}

		private async Task<NetworkScanResult?> ScanAddressAsync(
	IPAddress ipAddress,
	IPAddress localIpAddress,
	string localMacAddress)
		{
			using var ping = new Ping();

			var detected = false;

			for (var attempt = 0; attempt < 3; attempt++)
			{
				try
				{
					var reply = await ping.SendPingAsync(ipAddress, 750);

					if (reply.Status == IPStatus.Success)
					{
						detected = true;
						break;
					}
				}
				catch (PingException)
				{
					// Try again.
				}
			}

			if (!detected)
				return null;

			string? hostName = null;

			try
			{
				var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
				hostName = hostEntry.HostName;
			}
			catch (SocketException)
			{
				// No hostname could be resolved for this address.
			}

			var macAddress = ipAddress.Equals(localIpAddress)
				? localMacAddress
				: GetMacAddress(ipAddress);

			return new NetworkScanResult
			{
				Name = hostName,
				IpAddress = ipAddress.ToString(),
				MacAddress = macAddress
			};
		}

		private string? GetMacAddress(IPAddress ipAddress)
		{
			var startInfo = CreateNeighborLookupStartInfo(ipAddress);
			if (startInfo == null)
				return null;

			Process? process;
			try
			{
				process = Process.Start(startInfo);
			}
			catch (Win32Exception)
			{
				// The platform's neighbor lookup command is not installed.
				return null;
			}

			using (process)
			{
				if (process == null)
					return null;

				var output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();

				var match = Regex.Match(
					output,
					@"\b[0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5}\b");

				if (!match.Success)
					return null;

				return match.Value.Replace('-', ':').ToUpperInvariant();
			}
		}

		private static ProcessStartInfo? CreateNeighborLookupStartInfo(IPAddress ipAddress)
		{
			string fileName;
			string[] arguments;

			if (OperatingSystem.IsWindows())
			{
				fileName = "arp";
				arguments = ["-a", ipAddress.ToString()];
			}
			else if (OperatingSystem.IsLinux())
			{
				fileName = "ip";
				arguments = ["neigh", "show", ipAddress.ToString()];
			}
			else
			{
				return null;
			}

			var startInfo = new ProcessStartInfo
			{
				FileName = fileName,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);

			return startInfo;
		}
	}
}