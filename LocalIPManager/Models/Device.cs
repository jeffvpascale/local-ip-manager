using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LocalIPManager.Models
{
	[Index(nameof(IpAddress), IsUnique =true)]
	[Index(nameof(MacAddress), IsUnique =true)]
	public class Device
	{
		[Key]
		public int Id { get; set; }

		[Required]
		[MaxLength(100)]
		public required string Name { get; set; }

		[MaxLength(500)]
		public string? Description { get; set; }

		[Required]
		[MaxLength(15)]
		[RegularExpression(
			@"^(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)$",
			ErrorMessage = "Enter a valid IPv4 address.")]
		public required string IpAddress { get; set; }

		[MaxLength(17)]
		[RegularExpression(
			@"^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$",
			ErrorMessage = "Enter a valid MAC address (e.g. 00:1A:2B:3C:4D:5E).")]
		public string? MacAddress { get; set; }

		public bool IsReserved { get; set; }

		public bool IsStatic { get; set; }

	}
}
