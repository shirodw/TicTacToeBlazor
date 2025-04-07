using Microsoft.EntityFrameworkCore;
using TicTacToeBlazor.Models;

namespace TicTacToeBlazor.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Player> Players { get; set; }
        public DbSet<Game> Games { get; set; }
        public DbSet<Turn> Turns { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships explicitly to avoid ambiguity with multiple FKs to Player
            modelBuilder.Entity<Game>()
                .HasOne(g => g.Player1)
                .WithMany(p => p.GamesAsPlayer1)
                .HasForeignKey(g => g.Player1Id)
                .OnDelete(DeleteBehavior.Restrict); // Or Cascade if appropriate, be careful

            modelBuilder.Entity<Game>()
                .HasOne(g => g.Player2)
                .WithMany(p => p.GamesAsPlayer2)
                .HasForeignKey(g => g.Player2Id)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Game>()
                .HasOne(g => g.Winner)
                .WithMany(p => p.WonGames)
                .HasForeignKey(g => g.WinnerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Turn>()
               .HasOne(t => t.Player)
               .WithMany(p => p.Turns)
               .HasForeignKey(t => t.PlayerId)
               .OnDelete(DeleteBehavior.Restrict); // Or Cascade

             modelBuilder.Entity<Turn>()
               .HasOne(t => t.Game)
               .WithMany(g => g.Turns)
               .HasForeignKey(t => t.GameId)
               .OnDelete(DeleteBehavior.Cascade); // Turns deleted if game is deleted
        }
    }
}