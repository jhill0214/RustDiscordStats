using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;

namespace Oxide.Plugins
{
    [Info("DiscordServerStats", "GumbiAI", "1.0.0")]
    [Description("Live Rust server stats sent to Discord via webhook")]

    public class DiscordServerStats : RustPlugin
    {
        private PluginConfig config;

        private class PluginConfig
        {
            public string DiscordWebhookUrl { get; set; } = "";
            public bool EnableServerStats { get; set; } = false;
            public int StatsUpdateInterval { get; set; } = 300; // seconds (5 minutes)
            public string StatsEmbedTitle { get; set; } = "📊 Server Stats";
            public string StatsEmbedColor { get; set; } = "#00ff00";
            public bool ShowPlayerNames { get; set; } = false;
            public bool ShowTeamInfo { get; set; } = false;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating new configuration file");
            Config.WriteObject(new PluginConfig(), true);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            
            try
            {
                config = Config.ReadObject<PluginConfig>();
                
                if (config == null)
                {
                    PrintWarning("Config was null, loading defaults");
                    LoadDefaultConfig();
                    config = new PluginConfig();
                }
            }
            catch (System.Exception ex)
            {
                PrintError($"Error loading config: {ex.Message}");
                LoadDefaultConfig();
                config = new PluginConfig();
            }
            
            SaveConfig();
        }

        private void Init()
        {
            // Force write config to ensure it's populated
            Config.WriteObject(config, true);
            
            Puts("[DiscordServerStats] Loaded successfully");
            Puts($"[DiscordServerStats] Server stats enabled: {config.EnableServerStats}");
            Puts($"[DiscordServerStats] Update interval: {config.StatsUpdateInterval}s");

            if (config.EnableServerStats)
                StartServerStats();
        }

        [ConsoleCommand("discordstats.reload")]
        private void CmdReloadConfig(ConsoleSystem.Arg arg)
        {
            LoadConfig();
            Puts("[DiscordServerStats] Config reloaded successfully");
        }

        [ConsoleCommand("discordstats.toggle")]
        private void CmdToggleStats(ConsoleSystem.Arg arg)
        {
            config.EnableServerStats = !config.EnableServerStats;
            SaveConfig();
            
            if (config.EnableServerStats)
            {
                StartServerStats();
                Puts("[DiscordServerStats] Server stats enabled");
            }
            else
            {
                Puts("[DiscordServerStats] Server stats disabled");
            }
        }

        [ConsoleCommand("discordstats.send")]
        private void CmdSendStats(ConsoleSystem.Arg arg)
        {
            SendServerStatsToDiscord();
            Puts("[DiscordServerStats] Server stats sent manually");
        }

        #region Server Stats System

        private void StartServerStats()
        {
            if (string.IsNullOrEmpty(config.DiscordWebhookUrl))
            {
                Puts("[DiscordServerStats] Discord webhook URL is not configured. Server stats disabled.");
                return;
            }

            timer.Every(config.StatsUpdateInterval, () =>
            {
                SendServerStatsToDiscord();
            });

            // Send initial stats
            timer.Once(1f, () => SendServerStatsToDiscord());

            Puts($"[DiscordServerStats] Server stats system started (Interval: {config.StatsUpdateInterval}s)");
        }

        private void SendServerStatsToDiscord()
        {
            try
            {
                var playerCount = BasePlayer.activePlayerList.Count;
                var maxPlayers = ConVar.Server.maxplayers;
                var fps = UnityEngine.Mathf.Round(1f / UnityEngine.Time.deltaTime);
                var uptime = UnityEngine.Time.time;
                var uptimeHours = System.TimeSpan.FromSeconds(uptime).TotalHours;
                var entityCount = BaseNetworkable.serverEntities.Count;
                var seed = ConVar.Server.seed;
                var hostname = ConVar.Server.hostname;

                var fields = new List<object>
                {
                    new { name = "👥 Players", value = $"{playerCount}/{maxPlayers}", inline = true },
                    new { name = "⚡ FPS", value = fps.ToString("F1"), inline = true },
                    new { name = "⏱️ Uptime", value = $"{uptimeHours:F1}h", inline = true },
                    new { name = "🏗️ Entities", value = entityCount.ToString(), inline = true },
                    new { name = "🌱 Seed", value = seed.ToString(), inline = true },
                    new { name = "📅 Last Update", value = System.DateTime.Now.ToString("HH:mm:ss"), inline = true }
                };

                // Add player names if enabled
                if (config.ShowPlayerNames && playerCount > 0)
                {
                    var playerNames = string.Join(", ", GetPlayerNames());
                    fields.Add(new { name = "👤 Online Players", value = playerNames.Length > 1024 ? playerNames.Substring(0, 1021) + "..." : playerNames, inline = false });
                }

                // Add team info if enabled
                if (config.ShowTeamInfo)
                {
                    var teamCount = RelationshipManager.ServerInstance.playerToTeam.Count;
                    fields.Add(new { name = "👥 Teams", value = teamCount.ToString(), inline = true });
                }

                var embed = new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = config.StatsEmbedTitle,
                            color = ConvertColorToInt(config.StatsEmbedColor),
                            fields = fields.ToArray(),
                            footer = new
                            {
                                text = $"Server: {hostname}"
                            },
                            timestamp = System.DateTime.UtcNow.ToString("o")
                        }
                    }
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(embed);
                webrequest.Enqueue(config.DiscordWebhookUrl, json, (code, response) =>
                {
                    if (code != 200 && code != 204)
                    {
                        Puts($"[DiscordServerStats] Discord webhook failed: {code} - {response}");
                    }
                }, this, new Dictionary<string, string> { { "Content-Type", "application/json" } }, "POST");
            }
            catch (System.Exception ex)
            {
                Puts($"[DiscordServerStats] Error sending server stats: {ex.Message}");
            }
        }

        private string[] GetPlayerNames()
        {
            var names = new List<string>();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && !string.IsNullOrEmpty(player.displayName))
                {
                    names.Add(player.displayName);
                }
            }
            return names.ToArray();
        }

        private int ConvertColorToInt(string hexColor)
        {
            try
            {
                if (hexColor.StartsWith("#"))
                    hexColor = hexColor.Substring(1);

                return System.Convert.ToInt32(hexColor, 16);
            }
            catch
            {
                return 65280; // Default green
            }
        }

        #endregion
    }
}
