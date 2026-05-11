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
    
    public class FxEmbedResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("status")]
        public FxEmbedStatus? Status { get; set; }
    }

    public class FxEmbedStatus
    {
        [JsonPropertyName("author")]
        public FxEmbedAuthor? Author { get; set; }
    }

    public class FxEmbedAuthor
    {
        [JsonPropertyName("screen_name")]
        public string ScreenName { get; set; } = string.Empty;
    }

    public class XApiError
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }

        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("required_enrollment")]
        public string? RequiredEnrollment { get; set; }

        [JsonPropertyName("registration_url")]
        public string? RegistrationUrl { get; set; }

        [JsonPropertyName("errors")]
        public List<XApiErrorDetail>? Errors { get; set; }
    }

    public class XApiErrorDetail
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string[]>? Parameters { get; set; }
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
