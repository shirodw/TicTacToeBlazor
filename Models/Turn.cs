using System.ComponentModel.DataAnnotations.Schema;

namespace TicTacToeBlazor.Models
{
    public class Turn
    {
        public int Id { get; set; } // Unique Turn ID

        public int GameId { get; set; }
        [ForeignKey("GameId")]
        public virtual Game Game { get; set; } = null!;

        public int PlayerId { get; set; }
        [ForeignKey("PlayerId")]
        public virtual Player Player { get; set; } = null!;

        public int CoordX { get; set; } // Row
        public int CoordY { get; set; } // Column
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}