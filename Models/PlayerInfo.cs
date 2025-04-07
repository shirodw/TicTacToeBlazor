namespace TicTacToeBlazor.Models
{
    public class PlayerInfo
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int? DbPlayerId { get; set; } // Link to the persisted Player ID after creation/lookup
        public string Symbol { get; set; } = string.Empty; // 'X' or 'O'
    }
}