namespace TicTacToeBlazor.Models
{
    public class ChatMessage
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}