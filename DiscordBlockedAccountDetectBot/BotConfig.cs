namespace DiscordBlockedAccountDetectBot
{
    public class BotConfig
    {
        public DiscordConfig Discord { get; set; } = new();
        public XApiConfig XApi { get; set; } = new();
        public RedisConfig Redis { get; set; } = new();
    }

    public class DiscordConfig
    {
        public string Token { get; set; } = string.Empty;
    }

    public class XApiConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = "http://127.0.0.1:3000/callback";
        public string Scopes { get; set; } = "tweet.read users.read block.read offline.access";
    }

    public class RedisConfig
    {
        public string ConnectionString { get; set; } = "localhost:6379";
    }
}
