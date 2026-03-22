using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace CerberusPlugin
{
    [ApiVersion(2, 1)]
    public class CerberusBridge : TerrariaPlugin
    {
        public override string Name => "Cerberus Authentication Bridge";
        public override Version Version => new Version(2, 0, 0);
        public override string Author => "Server Admin";
        public override string Description => "Enterprise Discord authentication bridge using SQLite.";

        private string DbPath => Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/world/shared/cerberus.db";
        private string LimboGroup => Environment.GetEnvironmentVariable("LIMBO_GROUP_NAME") ?? "guest_limbo";
        private string KickMessage => Environment.GetEnvironmentVariable("KICK_MESSAGE") ?? "Please join our Discord to verify your account.";

        public CerberusBridge(Main game) : base(game) { }

        public override void Initialize()
        {
            InitializeDatabase();
            EnsureLimboGroupExists();

            ServerApi.Hooks.NetConnect.Register(this, OnNetConnect, 10);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NetConnect.Deregister(this, OnNetConnect);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
            }
            base.Dispose(disposing);
        }

        private SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection($"Data Source={DbPath};");
            conn.Open();
            // Enable Write-Ahead Logging for concurrent read/writes across multi-server docker containers
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
            return conn;
        }

        private void InitializeDatabase()
        {
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS PendingTokens (
                        DiscordId TEXT NOT NULL,
                        TsName TEXT NOT NULL,
                        Token TEXT NOT NULL,
                        Expiry DATETIME NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS VerifiedUsers (
                        DiscordId TEXT PRIMARY KEY,
                        TsAccountName TEXT NOT NULL,
                        Role TEXT NOT NULL
                    );";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Cerberus] Critical DB Init Error: {ex.Message}");
            }
        }

        private void EnsureLimboGroupExists()
        {
            if (!TShock.Groups.GroupExists(LimboGroup))
            {
                TShock.Groups.AddGroup(LimboGroup, null, "Limbo", "150,150,150");
                TShock.Log.ConsoleInfo($"[Cerberus] Auto-generated restricted group: {LimboGroup}");
            }
        }

        private void OnNetConnect(ConnectEventArgs args)
        {
            if (args.Handled) return;

            var playerName = args.ConnectRequest.Name;

            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT 1 FROM PendingTokens WHERE TsName = @name AND Expiry > @now
                    UNION 
                    SELECT 1 FROM VerifiedUsers WHERE TsAccountName = @name;";
                cmd.Parameters.AddWithValue("@name", playerName);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);

                var result = cmd.ExecuteScalar();

                // If not in either table, instant kick.
                if (result == null)
                {
                    args.Handled = true;
                    TShock.Utils.ForceKick(TShock.Players[args.Who], KickMessage);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Cerberus] Fail-Closed activated. DB Error: {ex.Message}");
                args.Handled = true;
                TShock.Utils.ForceKick(TShock.Players[args.Who], "Authentication system is currently offline for maintenance.");
            }
        }

        private void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null) return;

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Token FROM PendingTokens WHERE TsName = @name AND Expiry > @now LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", player.Name);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);

            var token = cmd.ExecuteScalar()?.ToString();

            if (token != null)
            {
                player.Group = TShock.Groups.GetGroupByName(LimboGroup);
                player.SetBuff(23, 36000, true); // Cursed
                player.SendErrorMessage($"You are in Limbo. Please log in with /login, then type /verify {token}");
            }
        }

        private void OnChat(ServerChatEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || args.Handled) return;

            if (args.Text.StartsWith("/verify"))
            {
                args.Handled = true;
                
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("You must be logged into a TShock account. Type /register [pass] or /login [pass] first.");
                    return;
                }

                var parts = args.Text.Split(' ');
                if (parts.Length != 2)
                {
                    player.SendErrorMessage("Usage: /verify [4-digit-code]");
                    return;
                }

                var submittedToken = parts[1];

                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DiscordId FROM PendingTokens WHERE TsName = @name AND Token = @token AND Expiry > @now;";
                cmd.Parameters.AddWithValue("@name", player.Name);
                cmd.Parameters.AddWithValue("@token", submittedToken);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);

                var discordId = cmd.ExecuteScalar()?.ToString();

                if (discordId != null)
                {
                    // Token is valid. Execute atomic transaction to move user to Verified and delete Pending token.
                    using var transaction = conn.BeginTransaction();
                    try
                    {
                        using var insertCmd = conn.CreateCommand();
                        insertCmd.Transaction = transaction;
                        insertCmd.CommandText = "INSERT OR REPLACE INTO VerifiedUsers (DiscordId, TsAccountName, Role) VALUES (@id, @acc, 'default');";
                        insertCmd.Parameters.AddWithValue("@id", discordId);
                        insertCmd.Parameters.AddWithValue("@acc", player.Account.Name);
                        insertCmd.ExecuteNonQuery();

                        using var deleteCmd = conn.CreateCommand();
                        deleteCmd.Transaction = transaction;
                        deleteCmd.CommandText = "DELETE FROM PendingTokens WHERE TsName = @name;";
                        deleteCmd.Parameters.AddWithValue("@name", player.Name);
                        deleteCmd.ExecuteNonQuery();

                        transaction.Commit();

                        player.Group = TShock.Groups.GetGroupByName("default"); // Replace with dynamic logic later if desired
                        player.Heal(); 
                        player.SendSuccessMessage("Verification complete! You are now permanently linked.");
                    }
                    catch
                    {
                        transaction.Rollback();
                        player.SendErrorMessage("A database error occurred during verification.");
                    }
                }
                else
                {
                    player.SendErrorMessage("Invalid or expired token.");
                }
            }
        }
    }
}