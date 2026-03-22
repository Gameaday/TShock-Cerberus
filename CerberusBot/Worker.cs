using Discord.WebSocket;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CerberusBot
{
    public class Worker : BackgroundService
    {
        private readonly DiscordSocketClient _client;
        private readonly ILogger<Worker> _logger;
        private readonly string _dbPath;

        public Worker(DiscordSocketClient client, ILogger<Worker> logger)
        {
            _client = client;
            _logger = logger;
            // Uses the exact same path mapped via Docker volumes
            _dbPath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/app/shared/cerberus.db";
        }

        private SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
            return conn;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _client.Log += msg => { _logger.LogInformation(msg.ToString()); return Task.CompletedTask; };
            _client.MessageReceived += MessageReceivedAsync;

            var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            await _client.LoginAsync(Discord.TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1, stoppingToken);
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot || !message.Content.StartsWith("!link ")) return;

            var parts = message.Content.Split(' ');
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: `!link <YourTerrariaName>`");
                return;
            }

            string tsName = parts[1];
            string token = new Random().Next(1000, 9999).ToString();
            DateTime expiry = DateTime.UtcNow.AddMinutes(15);

            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO PendingTokens (DiscordId, TsName, Token, Expiry) VALUES (@id, @name, @token, @expiry);";
                cmd.Parameters.AddWithValue("@id", message.Author.Id.ToString());
                cmd.Parameters.AddWithValue("@name", tsName);
                cmd.Parameters.AddWithValue("@token", token);
                cmd.Parameters.AddWithValue("@expiry", expiry);
                cmd.ExecuteNonQuery();

                await message.Author.SendMessageAsync($"Your Terraria verification code is: **{token}**. \nLog into the server and type `/verify {token}`");
                await message.Channel.SendMessageAsync($"{message.Author.Mention}, I've DM'd you your code!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to generate token: {ex.Message}");
                await message.Channel.SendMessageAsync("An internal database error occurred.");
            }
        }
    }
}