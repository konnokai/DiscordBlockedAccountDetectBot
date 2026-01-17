using System.Text.Json.Serialization;

namespace DiscordBlockedAccountDetectBot.Models
{
    public class XUserResponse
    {
        [JsonPropertyName("data")]
        public XUserData? Data { get; set; }
    }

    public class XUserData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }

    public class XBlockingResponse
    {
        [JsonPropertyName("data")]
        public List<XUserData>? Data { get; set; }

        [JsonPropertyName("meta")]
        public XMeta? Meta { get; set; }
    }

    public class XMeta
    {
        [JsonPropertyName("next_token")]
        public string? NextToken { get; set; }
    }
    
    public class VXTwitterResponse
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
         [JsonPropertyName("tweetID")]
        public string TweetId { get; set; } = string.Empty;
         [JsonPropertyName("tweetURL")]
        public string TweetUrl { get; set; } = string.Empty;
         [JsonPropertyName("user_name")]
        public string UserName { get; set; } = string.Empty;
        [JsonPropertyName("user_screen_name")]
        public string UserScreenName { get; set; } = string.Empty;
    }

    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty; // Bearer
        
        // Calculated
        public DateTime ExpiresAt { get; set; }

        // Cached User Info
        public string? UserId { get; set; }
    }
}
