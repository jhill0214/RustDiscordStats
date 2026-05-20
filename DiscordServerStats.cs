using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("DiscordServerStats", "GumbiAI", "1.0.0")]
    [Description("Live Rust server stats sent to Discord via webhook")]

    public class DiscordServerStats : RustPlugin
    {
        private PluginConfig config;
        private WebRequests _webRequests;
        private string _lastMessageId;

        private class PluginConfig
        {
            public string DiscordBotToken { get; set; } = "";
            public string DiscordChannelId { get; set; } = "";
            public bool EnableServerStats { get; set; } = false;
            public int StatsUpdateInterval { get; set; } = 300; // seconds (5 minutes)
            public string ServerIp { get; set; } = "";
            public int ServerPort { get; set; } = 28015;
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
            _webRequests = Interface.Oxide.GetLibrary<WebRequests>();

            // Force write config to ensure it's populated
            Config.WriteObject(config, true);

            Puts("[DiscordServerStats] Loaded successfully");

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
            if (string.IsNullOrEmpty(config.DiscordBotToken) || string.IsNullOrEmpty(config.DiscordChannelId))
            {
                Puts("[DiscordServerStats] Discord bot token or channel ID is not configured. Server stats disabled.");
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
                var sleeperCount = BasePlayer.sleepingPlayerList.Count;
                var mapSize = ConVar.Server.worldsize;
                var hostname = ConVar.Server.hostname;

                // Calculate average ping
                float avgPing = 0f;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player != null && player.net != null)
                    {
                        try
                        {
                            avgPing += player.net.connection.ping;
                        }
                        catch
                        {
                            // Skip if ping unavailable
                        }
                    }
                }
                if (playerCount > 0)
                    avgPing /= playerCount;

                // Create Discord embed
                var embed = new Newtonsoft.Json.Linq.JObject();
                embed["title"] = config.StatsEmbedTitle;
                embed["color"] = ConvertColorToInt(config.StatsEmbedColor);
                embed["timestamp"] = System.DateTime.Now.ToString("o");

                var fields = new Newtonsoft.Json.Linq.JArray();

                // Players field
                var playerField = new Newtonsoft.Json.Linq.JObject();
                playerField["name"] = "👥 Players";
                playerField["value"] = $"{playerCount}/{maxPlayers}";
                playerField["inline"] = true;
                fields.Add(playerField);

                // Sleepers field
                var sleeperField = new Newtonsoft.Json.Linq.JObject();
                sleeperField["name"] = "💤 Sleepers";
                sleeperField["value"] = sleeperCount.ToString();
                sleeperField["inline"] = true;
                fields.Add(sleeperField);

                // FPS field
                var fpsField = new Newtonsoft.Json.Linq.JObject();
                fpsField["name"] = "⚡ FPS";
                fpsField["value"] = $"{fps:F1}";
                fpsField["inline"] = true;
                fields.Add(fpsField);

                // Ping field
                var pingField = new Newtonsoft.Json.Linq.JObject();
                pingField["name"] = "📶 Avg Ping";
                pingField["value"] = $"{avgPing:F0}ms";
                pingField["inline"] = true;
                fields.Add(pingField);

                // Uptime field
                var uptimeField = new Newtonsoft.Json.Linq.JObject();
                uptimeField["name"] = "⏱️ Uptime";
                uptimeField["value"] = $"{uptimeHours:F1}h";
                uptimeField["inline"] = true;
                fields.Add(uptimeField);

                // Map size field
                var mapField = new Newtonsoft.Json.Linq.JObject();
                mapField["name"] = "🗺️ Map Size";
                mapField["value"] = $"{mapSize}";
                mapField["inline"] = true;
                fields.Add(mapField);

                // Entities field
                var entityField = new Newtonsoft.Json.Linq.JObject();
                entityField["name"] = "🏗️ Entities";
                entityField["value"] = entityCount.ToString();
                entityField["inline"] = true;
                fields.Add(entityField);

                // Connection info field
                var connectField = new Newtonsoft.Json.Linq.JObject();
                connectField["name"] = "🔗 Connect";
                if (!string.IsNullOrEmpty(config.ServerIp))
                {
                    connectField["value"] = $"{config.ServerIp}:{config.ServerPort}";
                }
                else
                {
                    connectField["value"] = hostname;
                }
                connectField["inline"] = false;
                fields.Add(connectField);

                embed["fields"] = fields;

                var payload = new Newtonsoft.Json.Linq.JObject();
                payload["embeds"] = new Newtonsoft.Json.Linq.JArray { embed };

                string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

                var headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "Authorization", $"Bot {config.DiscordBotToken}" },
                    { "User-Agent", "OxidePlugin-DiscordServerStats" }
                };

                string apiUrl = $"https://discord.com/api/v10/channels/{config.DiscordChannelId}/messages";

                if (_webRequests != null)
                {
                    if (string.IsNullOrEmpty(_lastMessageId))
                    {
                        // Send new message
                        _webRequests.Enqueue(apiUrl, jsonBody, (code, response) =>
                        {
                            if (code != 200 && code != 204 && code != 0)
                            {
                                Puts($"[DiscordServerStats] Discord API failed: {code}");
                            }
                            else
                            {
                                // Extract message ID from response
                                try
                                {
                                    var responseObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(response);
                                    if (responseObj != null && responseObj["id"] != null)
                                    {
                                        _lastMessageId = responseObj["id"].ToString();
                                    }
                                }
                                catch { }
                            }
                        }, this, RequestMethod.POST, headers);
                    }
                    else
                    {
                        // Edit existing message
                        string editUrl = $"{apiUrl}/{_lastMessageId}";

                        _webRequests.Enqueue(editUrl, jsonBody, (code, response) =>
                        {
                            if (code != 200 && code != 204 && code != 0)
                            {
                                Puts($"[DiscordServerStats] Discord API edit failed: {code}");
                                // If edit fails, clear the message ID and try sending a new message next time
                                if (code == 404 || code == 403)
                                {
                                    _lastMessageId = null;
                                }
                            }
                        }, this, RequestMethod.PATCH, headers);
                    }
                }
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
