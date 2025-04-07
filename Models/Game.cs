using System.ComponentModel.DataAnnotations.Schema;

namespace TicTacToeBlazor.Models
{
    public class Game
    {
        public int Id { get; set; } // Unique Game ID

        public int Player1Id { get; set; }
        [ForeignKey("Player1Id")]
        public virtual Player Player1 { get; set; } = null!;

        public int Player2Id { get; set; }
        [ForeignKey("Player2Id")]
        public virtual Player Player2 { get; set; } = null!;

        public int? WinnerId { get; set; } // Nullable if draw or ongoing
        [ForeignKey("WinnerId")]
        public virtual Player? Winner { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; } // Nullable if ongoing

        public int BoardSize { get; set; } // Store the board size used for this game

        public virtual ICollection<Turn> Turns { get; set; } = new List<Turn>();
    }
}