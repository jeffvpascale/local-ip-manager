namespace LocalIPManager.Models
{
	public class NetworkScanResult
	{
		public string? Name { get; set; }
		public required string IpAddress { get; set; }
		public string? MacAddress { get; set; }
	}
}
