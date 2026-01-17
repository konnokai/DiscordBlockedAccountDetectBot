using StackExchange.Redis;
using System.Text.Json;
using DiscordBlockedAccountDetectBot.Models;

namespace DiscordBlockedAccountDetectBot.Services
{
    public class RedisService
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private const string BlockedUsersKey = "x:blocked_users";
        private const string RateLimitKeyPrefix = "x:ratelimit:";
        private const string TokenKey = "x:oauth_token";

        public RedisService(BotConfig config)
        {
            _redis = ConnectionMultiplexer.Connect(config.Redis.ConnectionString);
            _db = _redis.GetDatabase(0);
        }

        public async Task SaveBlockedUsersAsync(IEnumerable<string> usernames)
        {
            if (usernames == null || !usernames.Any()) return;

            var transaction = _db.CreateTransaction();
            _ = transaction.KeyDeleteAsync(BlockedUsersKey);
            // Store as lowercase for case-insensitive comparison
            var redisValues = usernames.Select(u => (RedisValue)u.ToLowerInvariant()).ToArray();
            _ = transaction.SetAddAsync(BlockedUsersKey, redisValues);
            await transaction.ExecuteAsync();
        }

        public async Task<bool> IsUserBlockedAsync(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            return await _db.SetContainsAsync(BlockedUsersKey, username.ToLowerInvariant());
        }

        public async Task SaveRateLimitAsync(string endpoint, int limit, int remaining, long reset)
        {
            var key = $"{RateLimitKeyPrefix}{endpoint}";
            var entry = new HashEntry[]
            {
                new HashEntry("limit", limit),
                new HashEntry("remaining", remaining),
                new HashEntry("reset", reset)
            };
            await _db.HashSetAsync(key, entry);
            
            // Set expire to reset time + buffer (e.g. 1 min) to cleanup old keys, or just let them sit.
            // X API reset is unix timestamp (seconds).
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(reset);
            var expiry = resetTime - DateTimeOffset.UtcNow;
            if (expiry > TimeSpan.Zero)
            {
                await _db.KeyExpireAsync(key, expiry.Add(TimeSpan.FromMinutes(1))); 
            }
        }

        public async Task<(int Limit, int Remaining, long Reset)?> GetRateLimitAsync(string endpoint)
        {
            var key = $"{RateLimitKeyPrefix}{endpoint}";
            var hash = await _db.HashGetAllAsync(key);
            
            if (hash.Length == 0) return null;

            var dict = hash.ToDictionary(h => h.Name.ToString(), h => h.Value);
            
            int.TryParse(dict.GetValueOrDefault("limit").ToString(), out int limit);
            int.TryParse(dict.GetValueOrDefault("remaining").ToString(), out int remaining);
            long.TryParse(dict.GetValueOrDefault("reset").ToString(), out long reset);

            return (limit, remaining, reset);
        }

        public async Task SaveTokenAsync(OAuthTokenResponse token)
        {
            if (token == null) return;
            var json = JsonSerializer.Serialize(token);
            await _db.StringSetAsync(TokenKey, json);
        }

        public async Task<OAuthTokenResponse?> GetTokenAsync()
        {
            var json = await _db.StringGetAsync(TokenKey);
            if (json.IsNull) return null;
            
            try
            {
                return JsonSerializer.Deserialize<OAuthTokenResponse>(json.ToString());
            }
            catch
            {
                return null;
            }
        }

        public async Task DeleteTokenAsync()
        {
            await _db.KeyDeleteAsync(TokenKey);
        }
    }
}
