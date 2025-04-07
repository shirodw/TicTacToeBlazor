using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using TicTacToeBlazor.Data;
using TicTacToeBlazor.Models;

namespace TicTacToeBlazor.Services
{
    // Singleton service to manage game state
    public class GameStateService
    {
        private readonly ConcurrentDictionary<string, PlayerInfo> _waitingPlayers = new(); // Key: ConnectionId
        private readonly ConcurrentDictionary<string, GameInfo> _activeGames = new(); // Key: GameId (GUID)
        private readonly ConcurrentDictionary<string, string> _playerConnections = new(); // Key: ConnectionId, Value: GameId
        private readonly object _matchmakingLock = new object();
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public GameStateService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<PlayerInfo?> PlayerConnected(string connectionId, string name)
        {
            // Ensure player name is unique among waiting players for simplicity
            if (_waitingPlayers.Values.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var player = new PlayerInfo { ConnectionId = connectionId, Name = name };
            _waitingPlayers[connectionId] = player;
            Console.WriteLine($"Player connected: {name} ({connectionId}). Waiting: {_waitingPlayers.Count}");
            return player;
        }

        // Modified: Returns PlayerInfo tuple for easier Hub handling
        public (GameInfo? game, PlayerInfo? player1, PlayerInfo? player2) TryMatchmake(string connectionId)
        {
            lock (_matchmakingLock)
            {
                if (!_waitingPlayers.TryGetValue(connectionId, out var newPlayer))
                    return (null, null, null);

                if (_waitingPlayers.Count < 2)
                {
                    Console.WriteLine($"{newPlayer.Name} is waiting for an opponent.");
                    return (null, newPlayer, null); // Still waiting
                }

                var opponent = _waitingPlayers.FirstOrDefault(kvp => kvp.Key != connectionId).Value;

                if (opponent != null)
                {
                    _waitingPlayers.TryRemove(newPlayer.ConnectionId, out _);
                    _waitingPlayers.TryRemove(opponent.ConnectionId, out _);

                    // Assign symbols (Player 1 = newPlayer = X, Player 2 = opponent = O)
                    newPlayer.Symbol = "X";
                    opponent.Symbol = "O";

                    var game = new GameInfo
                    {
                        Player1 = newPlayer, // Player 1 is 'X'
                        Player2 = opponent, // Player 2 is 'O'
                        Status = GameStatus.SettingBoardSize,
                        StartTime = DateTime.UtcNow
                    };

                    _activeGames[game.GameId] = game;
                    _playerConnections[newPlayer.ConnectionId] = game.GameId;
                    _playerConnections[opponent.ConnectionId] = game.GameId;

                    Console.WriteLine($"Game started: {game.GameId} between {newPlayer.Name} (X) and {opponent.Name} (O)");
                    // Return game, player1 (X), player2 (O)
                    return (game, newPlayer, opponent);
                }
                return (null, newPlayer, null); // Still waiting
            }
        }

        public GameInfo? GetGameByConnection(string connectionId)
        {
            if (_playerConnections.TryGetValue(connectionId, out var gameId))
            {
                _activeGames.TryGetValue(gameId, out var game);
                return game;
            }
            return null;
        }
        public GameInfo? GetGameById(string gameId)
        {
            _activeGames.TryGetValue(gameId, out var game);
            return game;
        }


        public async Task SetBoardSize(string gameId, int size)
        {
            if (_activeGames.TryGetValue(gameId, out var game))
            {
                if (game.Status == GameStatus.SettingBoardSize && size >= 3 && size <= 10) // Example size limits
                {
                    game.BoardSize = size;
                    game.Board = new string?[size, size];
                    game.Status = GameStatus.Player1Turn; // Player X starts

                    using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                    var dbPlayer1 = await GetOrCreateDbPlayer(dbContext, game.Player1.Name);
                    var dbPlayer2 = await GetOrCreateDbPlayer(dbContext, game.Player2!.Name); // Player 2 is guaranteed non-null here
                    game.Player1.DbPlayerId = dbPlayer1.Id;
                    game.Player2.DbPlayerId = dbPlayer2.Id;


                    var dbGame = new Game
                    {
                        Player1Id = dbPlayer1.Id,
                        Player2Id = dbPlayer2.Id,
                        StartDate = game.StartTime,
                        BoardSize = size,
                    };
                    dbContext.Games.Add(dbGame);
                    await dbContext.SaveChangesAsync();
                    game.DbGameId = dbGame.Id;
                    Console.WriteLine($"Game {gameId} (DB ID: {dbGame.Id}) board size set to {size}x{size}. Player 1's turn.");
                }
            }
        }

        private async Task<Player> GetOrCreateDbPlayer(ApplicationDbContext dbContext, string playerName)
        {
            var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Name == playerName);
            if (player == null)
            {
                player = new Player { Name = playerName };
                dbContext.Players.Add(player);
                await dbContext.SaveChangesAsync();
            }
            return player;
        }

        public async Task<(bool success, string message, GameStatus newStatus)> MakeMove(string connectionId, int row, int col)
        {
            // Find game via connection map first
            if (!_playerConnections.TryGetValue(connectionId, out var gameId) || !_activeGames.TryGetValue(gameId, out var game))
                return (false, "Game not found.", GameStatus.Aborted);

            if (game.Status != GameStatus.Player1Turn && game.Status != GameStatus.Player2Turn)
                return (false, "Game is not currently active.", game.Status);

            // Determine player making the move based on game state and connection ID
            PlayerInfo? player = null;
            if (game.Status == GameStatus.Player1Turn && game.Player1?.ConnectionId == connectionId) player = game.Player1;
            else if (game.Status == GameStatus.Player2Turn && game.Player2?.ConnectionId == connectionId) player = game.Player2;

            if (player == null) // This covers wrong turn or player not found in game object
                return (false, "It's not your turn or player invalid.", game.Status);


            if (row < 0 || row >= game.BoardSize || col < 0 || col >= game.BoardSize || game.Board == null || game.Board[row, col] != null)
                return (false, "Invalid move.", game.Status);

            // Make the move
            game.Board[row, col] = player.Symbol;

            // Persist the turn
            await RecordTurn(game.DbGameId!.Value, player.DbPlayerId!.Value, row, col);

            // Check for win/draw
            var winnerSymbol = CheckWin(game.Board, game.BoardSize);
            if (winnerSymbol != null)
            {
                game.Status = (winnerSymbol == game.Player1?.Symbol) ? GameStatus.Player1Win : GameStatus.Player2Win; // Check against Player1 as Player2 could technically be null briefly on disconnect race conditions, though unlikely here
                Console.WriteLine($"Game {gameId} over. Winner: {winnerSymbol}");
                await EndGame(game.DbGameId!.Value, game.Status == GameStatus.Player1Win ? game.Player1?.DbPlayerId : game.Player2?.DbPlayerId);
                return (true, $"{player.Name} wins!", game.Status);
            }
            else if (IsBoardFull(game.Board, game.BoardSize))
            {
                game.Status = GameStatus.Draw;
                Console.WriteLine($"Game {gameId} over. Draw.");
                await EndGame(game.DbGameId!.Value, null); // No winner ID for draw
                return (true, "It's a draw!", game.Status);
            }
            else
            {
                // Switch turns
                game.Status = (game.Status == GameStatus.Player1Turn) ? GameStatus.Player2Turn : GameStatus.Player1Turn;
                Console.WriteLine($"Move made by {player.Name} at ({row},{col}). Next turn: {game.Status}");
                return (true, "Move successful.", game.Status);
            }
        }

        private async Task RecordTurn(int dbGameId, int dbPlayerId, int row, int col)
        {
            using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var turn = new Turn
            {
                GameId = dbGameId,
                PlayerId = dbPlayerId,
                CoordX = row,
                CoordY = col,
                Timestamp = DateTime.UtcNow
            };
            dbContext.Turns.Add(turn);
            await dbContext.SaveChangesAsync();
        }

        private async Task EndGame(int dbGameId, int? winnerDbId)
        {
            using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var dbGame = await dbContext.Games.FindAsync(dbGameId);
            if (dbGame != null && !dbGame.EndDate.HasValue) // Only update if not already ended
            {
                dbGame.WinnerId = winnerDbId;
                dbGame.EndDate = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"Game {dbGameId} marked as ended in DB. WinnerID: {winnerDbId?.ToString() ?? "None"}.");
            }
            else if (dbGame == null)
            {
                Console.WriteLine($"Attempted to end game {dbGameId} in DB, but not found.");
            }
            else
            {
                Console.WriteLine($"Attempted to end game {dbGameId} in DB, but it already has an EndDate.");
            }
        }


        public void AddChatMessage(string gameId, string playerName, string message)
        {
            if (_activeGames.TryGetValue(gameId, out var game))
            {
                game.ChatMessages.Add(new ChatMessage { PlayerName = playerName, Message = message });
            }
        }

        // MODIFIED: Changed return type
        public async Task<(PlayerInfo? disconnectedPlayer, bool gameAborted)> PlayerDisconnected(string connectionId)
        {
            bool gameWasAborted = false; // Track if we aborted it here

            // Remove from waiting list if they were waiting
            if (_waitingPlayers.TryRemove(connectionId, out var waitingPlayer))
            {
                Console.WriteLine($"Waiting player disconnected: {waitingPlayer.Name} ({connectionId})");
                return (waitingPlayer, false); // Return player info, game was not aborted
            }

            // Handle disconnection during a game
            // Use TryGetValue first to avoid modifying _playerConnections if game doesn't exist in _activeGames
            if (_playerConnections.TryGetValue(connectionId, out var gameId) && _activeGames.TryGetValue(gameId, out var game))
            {
                // Now remove the connection since we know they were in this valid game
                _playerConnections.TryRemove(connectionId, out _);

                Console.WriteLine($"Player disconnected from game {gameId}: Connection {connectionId}");
                // Determine who disconnected (we know their connectionId)
                PlayerInfo? disconnectedPlayer = (game.Player1?.ConnectionId == connectionId) ? game.Player1 : game.Player2;

                // Check if the game is in a state where a disconnect should abort it
                if (game.Status != GameStatus.Player1Win &&
                    game.Status != GameStatus.Player2Win &&
                    game.Status != GameStatus.Draw &&
                    game.Status != GameStatus.Aborted) // Check if not already finished/aborted
                {
                    game.Status = GameStatus.Aborted;
                    gameWasAborted = true; // Mark that *this call* caused the abortion
                    Console.WriteLine($"Game {gameId} aborted due to player disconnect.");
                    if (game.DbGameId.HasValue)
                    {
                        // Persist the aborted state (no winner, set end date)
                        await EndGame(game.DbGameId.Value, null);
                    }
                }

                // Clean up the disconnected player reference in the game object
                if (game.Player1?.ConnectionId == connectionId) game.Player1 = null;
                if (game.Player2?.ConnectionId == connectionId) game.Player2 = null;

                // If *both* player references are now null (meaning the second player disconnected
                // after the first one already did), remove the game from memory.
                if (game.Player1 == null && game.Player2 == null)
                {
                    _activeGames.TryRemove(gameId, out _);
                    Console.WriteLine($"Game {gameId} removed as both players disconnected.");
                }

                // Return the player who disconnected and whether this disconnect triggered the abort
                return (disconnectedPlayer, gameWasAborted);
            }

            // Player wasn't waiting and wasn't found in an active game connection
            Console.WriteLine($"Disconnecting client {connectionId} not found in waiting or active game.");
            return (null, false);
        }


        // --- Game Logic Helpers ---

        private string? CheckWin(string?[,] board, int boardSize)
        {
            // Check rows, columns, and diagonals
            string? winner;

            // Rows
            for (int i = 0; i < boardSize; i++)
            {
                winner = CheckLine(board, boardSize, i, 0, 0, 1); // Check row i
                if (winner != null) return winner;
            }

            // Columns
            for (int j = 0; j < boardSize; j++)
            {
                winner = CheckLine(board, boardSize, 0, j, 1, 0); // Check column j
                if (winner != null) return winner;
            }

            // Diagonals
            winner = CheckLine(board, boardSize, 0, 0, 1, 1); // Top-left to bottom-right
            if (winner != null) return winner;
            winner = CheckLine(board, boardSize, 0, boardSize - 1, 1, -1); // Top-right to bottom-left
            if (winner != null) return winner;

            return null; // No winner
        }

        private string? CheckLine(string?[,] board, int boardSize, int startRow, int startCol, int dRow, int dCol)
        {
            string? first = board[startRow, startCol];
            if (first == null) return null;

            for (int i = 1; i < boardSize; i++)
            {
                int checkRow = startRow + i * dRow;
                int checkCol = startCol + i * dCol;
                // Bounds check needed for diagonal logic especially
                if (checkRow < 0 || checkRow >= boardSize || checkCol < 0 || checkCol >= boardSize) return null;

                if (board[checkRow, checkCol] != first)
                {
                    return null; // Line is not uniform
                }
            }
            return first; // Return the symbol ('X' or 'O') if the line is a win
        }


        private bool IsBoardFull(string?[,] board, int boardSize)
        {
            for (int i = 0; i < boardSize; i++)
            {
                for (int j = 0; j < boardSize; j++)
                {
                    if (board[i, j] == null)
                    {
                        return false; // Found an empty cell
                    }
                }
            }
            return true; // Board is full
        }
    }
}