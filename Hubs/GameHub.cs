using Microsoft.AspNetCore.SignalR;
using TicTacToeBlazor.Models;
using TicTacToeBlazor.Services;

namespace TicTacToeBlazor.Hubs
{
    public class GameHub : Hub
    {
        private readonly GameStateService _gameState;

        public GameHub(GameStateService gameState)
        {
            _gameState = gameState;
        }

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        // MODIFIED: Updated disconnect logic
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            string connectionId = Context.ConnectionId;
            Console.WriteLine($"Client disconnected: {connectionId} | Exception: {exception?.Message ?? "None"}");

            // --- Find potential opponent BEFORE changing state ---
            var game = _gameState.GetGameByConnection(connectionId);
            string? remainingPlayerConnectionId = null;
            if (game != null)
            {
                // Determine the connection ID of the other player, if they exist AND are still connected according to the GameInfo
                if (game.Player1?.ConnectionId == connectionId && game.Player2 != null)
                {
                    remainingPlayerConnectionId = game.Player2.ConnectionId;
                }
                else if (game.Player2?.ConnectionId == connectionId && game.Player1 != null)
                {
                    remainingPlayerConnectionId = game.Player1.ConnectionId;
                }
                Console.WriteLine($"Disconnecting player was in game {game.GameId}. Potential opponent connection: {remainingPlayerConnectionId ?? "None"}");
            }
            else
            {
                Console.WriteLine($"Disconnecting player {connectionId} was not found in an active game map.");
            }
            // --- End opponent finding ---

            // --- Call service to handle disconnect logic ---
            // This updates game state (sets status to Aborted if needed), cleans up player refs, etc.
            var (disconnectedPlayer, gameAborted) = await _gameState.PlayerDisconnected(connectionId);
            // --- End service call ---

            // --- Notify opponent if needed ---
            if (disconnectedPlayer != null && gameAborted && !string.IsNullOrEmpty(remainingPlayerConnectionId))
            {
                // If the service call resulted in the game being aborted,
                // and we identified an opponent earlier, notify them.
                Console.WriteLine($"Notifying remaining player ({remainingPlayerConnectionId}) about disconnect of {disconnectedPlayer.Name}.");
                try
                {
                    await Clients.Client(remainingPlayerConnectionId)
                                 .SendAsync("OpponentDisconnected", $"{disconnectedPlayer.Name} has disconnected. The game is aborted.");
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"Error notifying opponent {remainingPlayerConnectionId}: {notifyEx.Message}");
                    // Opponent might have disconnected simultaneously
                }
            }
            else if (disconnectedPlayer != null && gameAborted)
            {
                Console.WriteLine($"Game was aborted for player {disconnectedPlayer.Name}, but no remaining opponent connection ID was found/valid to notify.");
            }
            // --- End notification ---

            await base.OnDisconnectedAsync(exception);
        }


        public async Task FindGame(string playerName)
        {
            Console.WriteLine($"Player {playerName} ({Context.ConnectionId}) looking for game.");
            var playerInfo = await _gameState.PlayerConnected(Context.ConnectionId, playerName);

            if (playerInfo == null)
            {
                await Clients.Caller.SendAsync("NameInUse");
                return;
            }

            await Clients.Caller.SendAsync("UpdateState", "Waiting"); // Tell client they are waiting

            // Use modified tuple return from TryMatchmake
            var (game, player1, player2) = _gameState.TryMatchmake(Context.ConnectionId);

            if (game != null && player1 != null && player2 != null)
            {
                // Game found! Notify both players using their PlayerInfo objects
                Console.WriteLine($"Notifying players for game {game.GameId}: {player1.Name} ({player1.ConnectionId}) vs {player2.Name} ({player2.ConnectionId})");
                // Player 1 (who got X) is notified they are Player 1
                await Clients.Client(player1.ConnectionId).SendAsync("GameFound", game.GameId, player1.Name, player2.Name, player1.Symbol, true);
                // Player 2 (who got O) is notified they are Player 2
                await Clients.Client(player2.ConnectionId).SendAsync("GameFound", game.GameId, player1.Name, player2.Name, player2.Symbol, false);
            }
            else if (player1 != null) // player1 here is the playerInfo returned when still waiting
            {
                Console.WriteLine($"{player1.Name} is still waiting.");
            }
        }

        public async Task SetBoardSize(string gameId, int size)
        {
            Console.WriteLine($"Received request to set board size for game {gameId} to {size}x{size} from {Context.ConnectionId}");
            var game = _gameState.GetGameById(gameId);
            // Ensure player requesting is Player 1 and game is in correct state
            if (game != null && game.Player1?.ConnectionId == Context.ConnectionId && game.Status == GameStatus.SettingBoardSize)
            {
                await _gameState.SetBoardSize(gameId, size);
                // Check if status successfully changed to Player1Turn
                if (game.Status == GameStatus.Player1Turn)
                {
                    Console.WriteLine($"Board size set. Notifying players of game {gameId}.");
                    // Notify both players the game can start with the chosen size
                    // Player 2 should not be null at this stage if Player 1 is setting size
                    if (game.Player2 != null)
                    {
                        await Clients.Clients(game.Player1.ConnectionId, game.Player2.ConnectionId).SendAsync("GameStarted", game.GameId, size, game.Player1.Name); // Player1 starts
                    }
                    else
                    {
                        Console.WriteLine($"Error: Player 2 is null when trying to notify for game start {gameId}");
                        // Maybe send error back to Player 1?
                        await Clients.Caller.SendAsync("Error", "Opponent data missing, cannot start game.");
                    }
                }
                else
                {
                    // Handle invalid size (e.g., if validation failed in service somehow)
                    Console.WriteLine($"Invalid board size or state prevented setting size for game {gameId}.");
                    await Clients.Caller.SendAsync("Error", "Invalid board size requested or game state changed.");
                }
            }
            else
            {
                Console.WriteLine($"Failed to set board size for game {gameId}. Game not found, wrong player ({Context.ConnectionId} vs {game?.Player1?.ConnectionId}), or wrong state ({game?.Status}).");
                // Send error only if the player is actually part of the game but it's not their turn/right state
                if (game != null && (game.Player1?.ConnectionId == Context.ConnectionId || game.Player2?.ConnectionId == Context.ConnectionId))
                {
                    await Clients.Caller.SendAsync("Error", "Cannot set board size now.");
                }
            }
        }

        public async Task MakeMove(string gameId, int row, int col)
        {
            Console.WriteLine($"Received move ({row},{col}) for game {gameId} from {Context.ConnectionId}");
            var (success, message, newStatus) = await _gameState.MakeMove(Context.ConnectionId, row, col);
            var game = _gameState.GetGameById(gameId); // Get game state *after* move attempt

            if (game == null)
            {
                // Should not happen if MakeMove was called, implies game was removed during processing
                Console.WriteLine($"Error: Game {gameId} not found after MakeMove call.");
                await Clients.Caller.SendAsync("Error", "Game not found after move.");
                return;
            }

            // Ensure players are still present before trying to notify them
            string? p1ConnId = game.Player1?.ConnectionId;
            string? p2ConnId = game.Player2?.ConnectionId;
            var clientsToNotify = new List<string>();
            if (!string.IsNullOrEmpty(p1ConnId)) clientsToNotify.Add(p1ConnId);
            if (!string.IsNullOrEmpty(p2ConnId)) clientsToNotify.Add(p2ConnId);


            if (success)
            {
                Console.WriteLine($"Move successful in game {gameId}. New status: {newStatus}. Notifying players.");
                string nextPlayerName = "";
                if (newStatus == GameStatus.Player1Turn) nextPlayerName = game.Player1?.Name ?? "Player 1";
                else if (newStatus == GameStatus.Player2Turn) nextPlayerName = game.Player2?.Name ?? "Player 2";

                // Notify players still connected
                if (clientsToNotify.Any())
                {
                    await Clients.Clients(clientsToNotify).SendAsync("ReceiveMove", row, col, game.Board?[row, col], newStatus, nextPlayerName); // Send updated cell value
                }


                // If the game ended, send a specific message
                if (newStatus == GameStatus.Player1Win || newStatus == GameStatus.Player2Win || newStatus == GameStatus.Draw)
                {
                    string winnerName = newStatus == GameStatus.Player1Win ? game.Player1?.Name ?? "Player 1" :
                                        newStatus == GameStatus.Player2Win ? game.Player2?.Name ?? "Player 2" :
                                        ""; // Empty for draw
                    if (clientsToNotify.Any())
                    {
                        await Clients.Clients(clientsToNotify).SendAsync("GameOver", newStatus, winnerName);
                    }

                }
            }
            else
            {
                Console.WriteLine($"Invalid move in game {gameId}: {message}");
                // Notify only the caller of the invalid move
                await Clients.Caller.SendAsync("Error", message);
            }
        }

        public async Task SendChatMessage(string gameId, string message)
        {
            var game = _gameState.GetGameByConnection(Context.ConnectionId); // Find game via connection map
            if (game != null && game.GameId == gameId) // Ensure message is for the correct game
            {
                var sender = (game.Player1?.ConnectionId == Context.ConnectionId) ? game.Player1 : game.Player2;
                if (sender != null)
                {
                    var sanitizedMessage = message.Length > 200 ? message.Substring(0, 200) : message;
                    _gameState.AddChatMessage(gameId, sender.Name, sanitizedMessage);

                    Console.WriteLine($"Chat in game {gameId} from {sender.Name}: {sanitizedMessage}");

                    // Broadcast the message to potentially both players in the game
                    string? p1ConnId = game.Player1?.ConnectionId;
                    string? p2ConnId = game.Player2?.ConnectionId;
                    var clientsToNotify = new List<string>();
                    if (!string.IsNullOrEmpty(p1ConnId)) clientsToNotify.Add(p1ConnId);
                    if (!string.IsNullOrEmpty(p2ConnId)) clientsToNotify.Add(p2ConnId);

                    if (clientsToNotify.Any())
                    {
                        await Clients.Clients(clientsToNotify).SendAsync("ReceiveChatMessage", sender.Name, sanitizedMessage);
                    }
                }
            }
        }
    }
}