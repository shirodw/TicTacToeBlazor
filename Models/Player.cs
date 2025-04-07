using System.ComponentModel.DataAnnotations;

namespace TicTacToeBlazor.Models
{
    public class Player
    {
        public int Id { get; set; } // Unique Player ID across all games

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        // Navigation properties (optional but good practice)
        public virtual ICollection<Game> GamesAsPlayer1 { get; set; } = new List<Game>();
        public virtual ICollection<Game> GamesAsPlayer2 { get; set; } = new List<Game>();
        public virtual ICollection<Game> WonGames { get; set; } = new List<Game>();
        public virtual ICollection<Turn> Turns { get; set; } = new List<Turn>();
    }
}