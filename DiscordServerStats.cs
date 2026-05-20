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
            public string DiscordWebhookUrl { get; set; } = "";
            public bool EnableServerStats { get; set; } = false;
            public int StatsUpdateInterval { get; set; } = 300; // seconds (5 minutes)
            public string StatsEmbedTitle { get; set; } = "Server Stats";
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
            Puts($"[DiscordServerStats] Discord URL: {config.DiscordWebhookUrl}");
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

                string message = $"Server Stats\n";
                message += $"Players: {playerCount}/{maxPlayers}\n";
                message += $"FPS: {fps:F1}\n";
                message += $"Uptime: {uptimeHours:F1}h\n";
                message += $"Entities: {entityCount}\n";
                message += $"Seed: {seed}\n";
                message += $"Updated: {System.DateTime.Now:HH:mm:ss}\n";
                message += $"Server: {hostname}";

                var payload = new Dictionary<string, string> { { "content", message } };
                string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

                var headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "User-Agent", "OxidePlugin-DiscordServerStats" }
                };

                Puts($"[DiscordServerStats] Last message ID: {_lastMessageId ?? "null (will send new message)"}");
                Puts($"[DiscordServerStats] Sending message: {jsonBody}");
                if (_webRequests != null)
                {
                    if (string.IsNullOrEmpty(_lastMessageId))
                    {
                        Puts($"[DiscordServerStats] Sending NEW message");
                        // Send new message
                        _webRequests.Enqueue(config.DiscordWebhookUrl, jsonBody, (code, response) =>
                        {
                            if (code != 200 && code != 204 && code != 0)
                            {
                                Puts($"[DiscordServerStats] Discord webhook failed with code: {code}");
                            }
                            else
                            {
                                Puts($"[DiscordServerStats] Server stats sent successfully");
                                Puts($"[DiscordServerStats] Response: {response}");
                                // Extract message ID from response
                                try
                                {
                                    var responseObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(response);
                                    if (responseObj != null && responseObj["id"] != null)
                                    {
                                        _lastMessageId = responseObj["id"].ToString();
                                        Puts($"[DiscordServerStats] Stored message ID: {_lastMessageId}");
                                    }
                                }
                                catch
                                {
                                    Puts($"[DiscordServerStats] Could not parse message ID from response");
                                }
                            }
                        }, this, RequestMethod.POST, headers);
                    }
                    else
                    {
                        Puts($"[DiscordServerStats] EDITING existing message ID: {_lastMessageId}");
                        // Edit existing message
                        string webhookId = config.DiscordWebhookUrl.Split('/')[5];
                        string webhookToken = config.DiscordWebhookUrl.Split('/')[6];
                        string editUrl = $"https://discord.com/api/webhooks/{webhookId}/{webhookToken}/messages/{_lastMessageId}";
                        Puts($"[DiscordServerStats] Edit URL: {editUrl}");

                        _webRequests.Enqueue(editUrl, jsonBody, (code, response) =>
                        {
                            if (code != 200 && code != 204 && code != 0)
                            {
                                Puts($"[DiscordServerStats] Discord webhook edit failed with code: {code}");
                                Puts($"[DiscordServerStats] Edit response: {response}");
                                // If edit fails, clear the message ID and try sending a new message next time
                                if (code == 404 || code == 403)
                                {
                                    _lastMessageId = null;
                                    Puts($"[DiscordServerStats] Message no longer exists, will send new message next time");
                                }
                            }
                            else
                            {
                                Puts($"[DiscordServerStats] Server stats updated successfully");
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
