namespace LocalIPManager.Services
{
	public class NotificationService
	{
		private string? _message;

		public void Set(string message)
		{
			_message = message;
		}

		public string? Consume()
		{
			var message = _message;
			_message = null;
			return message;
		}
	}
}
