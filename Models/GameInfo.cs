namespace TicTacToeBlazor.Models
{
    public enum GameStatus { WaitingForPlayers, SettingBoardSize, Player1Turn, Player2Turn, Player1Win, Player2Win, Draw, Aborted }

    public class GameInfo
    {
        public string GameId { get; set; } = Guid.NewGuid().ToString(); // Unique ID for this active session
        public PlayerInfo Player1 { get; set; } = null!;
        public PlayerInfo? Player2 { get; set; } // Null while waiting
        public int? DbGameId { get; set; } // Link to the persisted Game ID after creation
        public int BoardSize { get; set; } = 0; // 0 until set
        public string?[,] Board { get; set; } = null!; // Represents the game board [row, col] -> "X" or "O" or null
        public GameStatus Status { get; set; } = GameStatus.WaitingForPlayers;
        public List<ChatMessage> ChatMessages { get; } = new List<ChatMessage>();
        public DateTime StartTime { get; set; }
    }
}