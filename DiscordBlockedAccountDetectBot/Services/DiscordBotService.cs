using Discord;
using Discord.WebSocket;
using DiscordBlockedAccountDetectBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiscordBlockedAccountDetectBot.Services
{
    public class DiscordBotService : BackgroundService
    {
        private readonly DiscordSocketClient _client;
        private readonly BotConfig _config;
        private readonly RedisService _redisService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DiscordBotService> _logger;
        private readonly XService _xService;
        private readonly IHostApplicationLifetime _appLifetime;

        // List of allowed hosts for Twitter/X links
        private static readonly List<string> AllowedHosts = new List<string> {
            "www.twitter.com", "twitter.com",
            "www.x.com", "x.com",
            "www.fixvx.com" ,"fixvx.com",
            "www.vxtwitter.com" ,"vxtwitter.com" ,
            "www.fxtwitter.com" ,"fxtwitter.com" };

        // Regex to extract the host and path from URLs
        private static readonly Regex UrlRegex = new Regex(@"https?:\/\/([a-zA-Z0-9\-\.]+)\/[a-zA-Z0-9_]+\/status\/[0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public DiscordBotService(
            BotConfig config,
            RedisService redisService,
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordBotService> logger,
            XService xService,
            IHostApplicationLifetime appLifetime)
        {
            _config = config;
            _redisService = redisService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _xService = xService;
            _appLifetime = appLifetime;

            var discordConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(discordConfig);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1. Validate Configuration
            if (!ValidateConfig())
            {
                _logger.LogCritical("Configuration missing or invalid. Please check appsettings.json.");
                _appLifetime.StopApplication();
                return;
            }

            try
            {
                // 2. Initialize X Service (OAuth etc)
                // This will perform login flow if needed, check existing token, and sync blocked list.
                _logger.LogInformation("Initializing X Service...");
                await _xService.InitializeAsync();
                _logger.LogInformation("X Service Initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to initialize X Service (Token or Sync issue). Discord Bot will not start.");
                _appLifetime.StopApplication();
                return;
            }

            // 3. Start Discord Bot
            _client.Log += LogAsync;
            _client.MessageReceived += OnMessageReceivedAsync;

            try
            {
                _logger.LogInformation("Logging into Discord...");
                await _client.LoginAsync(TokenType.Bot, _config.Discord.Token);
                await _client.StartAsync();
                _logger.LogInformation("Discord Bot Started.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to connect to Discord.");
                _appLifetime.StopApplication();
                return;
            }

            try
            {
                // Block this task until the app stops
                await Task.Delay(-1, stoppingToken);
            }
            catch (TaskCanceledException) { }
        }

        private bool ValidateConfig()
        {
            bool isValid = true;

            if (string.IsNullOrWhiteSpace(_config.Discord.Token))
            {
                _logger.LogError("Discord:Token is missing.");
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(_config.XApi.ClientId))
            {
                _logger.LogError("XApi:ClientId is missing.");
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(_config.XApi.ClientSecret))
            {
                _logger.LogError("XApi:ClientSecret is missing.");
                isValid = false;
            }

            return isValid;
        }

        private Task LogAsync(LogMessage log)
        {
            _logger.LogInformation(log.ToString());
            return Task.CompletedTask;
        }

        private async Task OnMessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return;
            if (string.IsNullOrEmpty(message.Content)) return;

            var matches = UrlRegex.Matches(message.Content);
            if (matches.Count == 0) return;

            foreach (Match match in matches)
            {
                var host = match.Groups[1].Value.ToLower();
                if (AllowedHosts.Contains(host))
                {
                    await ProcessLinkAsync(message, match.Value);
                }
            }
        }

        private async Task ProcessLinkAsync(SocketMessage message, string url)
        {
            try
            {
                _logger.LogInformation("Start fetch: {url}", url);

                // Replace domain with api.vxtwitter.com
                var uri = new Uri(url);
                var builder = new UriBuilder(uri)
                {
                    Host = "api.vxtwitter.com"
                };

                var apiUrl = builder.ToString();

                var httpClient = _httpClientFactory.CreateClient("VXApi");
                var response = await httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    // If fetch fails, maybe warn? Or ignore? Prompt says: "若其中一個流程出錯則反應一個 ⚠️"
                    // "Fetching data" is a process.
                    await message.AddReactionAsync(new Emoji("🛠️"));
                    _logger.LogWarning("Failed to fetch api.vxtwitter.com: {response.ReasonPhrase}", response.ReasonPhrase);
                    return;
                }

                var responseContext = await response.Content.ReadAsStringAsync();
                if (responseContext.Contains("Failed to scan your link"))
                {
                    _logger.LogWarning("api.vxtwitter.com returned failure for URL: {url}", url);
                    await message.AddReactionAsync(new Emoji("🛠️"));
                }

                var tweetData = JsonSerializer.Deserialize<VXTwitterResponse>(responseContext);
                if (tweetData != null && !string.IsNullOrEmpty(tweetData.UserScreenName))
                {
                    // Check if blocked
                    var isBlocked = await _redisService.IsUserBlockedAsync(tweetData.UserScreenName);
                    if (isBlocked)
                    {
                        await message.AddReactionAsync(new Emoji("⛔"));
                    }
                }
                else
                {
                    // Parsing failed or no user name? 
                    // Is this an error flow? "若其中一個流程出錯"
                    // If we got 200 OK but bad JSON, it's an error.
                    await message.AddReactionAsync(new Emoji("🛠️"));
                }
            }
            catch (JsonException)
            {
                _logger.LogError("JSON parsing error for URL: {url}", url);
                await message.AddReactionAsync(new Emoji("🛠️"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing link");
                await message.AddReactionAsync(new Emoji("🛠️"));
            }
        }
    }
}
