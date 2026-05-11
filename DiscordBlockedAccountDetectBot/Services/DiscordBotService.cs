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

        // Regex to extract the host, username and tweet ID from URLs
        private static readonly Regex UrlRegex = new Regex(@"https?:\/\/([a-zA-Z0-9\-\.]+)\/[a-zA-Z0-9_]+\/status\/([0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                    var tweetId = match.Groups[2].Value;
                    await ProcessLinkAsync(message, match.Value, tweetId);
                }
            }
        }

        private async Task ProcessLinkAsync(SocketMessage message, string url, string tweetId)
        {
            try
            {
                _logger.LogInformation("Start fetch: {url}", url);

                var apiUrl = $"https://api.fxtwitter.com/2/status/{tweetId}";

                var httpClient = _httpClientFactory.CreateClient("FxApi");
                var response = await httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    await message.AddReactionAsync(new Emoji("🛠️"));
                    _logger.LogWarning("Failed to fetch api.fxtwitter.com: {ReasonPhrase}", response.ReasonPhrase);
                    return;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tweetData = JsonSerializer.Deserialize<FxEmbedResponse>(responseContent);
                if (tweetData?.Status?.Author != null && !string.IsNullOrEmpty(tweetData.Status.Author.ScreenName))
                {
                    // Check if blocked
                    var isBlocked = await _redisService.IsUserBlockedAsync(tweetData.Status.Author.ScreenName);
                    if (isBlocked)
                    {
                        await message.AddReactionAsync(new Emoji("⛔"));
                    }
                }
                else
                {
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
