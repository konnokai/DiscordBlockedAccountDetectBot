using DiscordBlockedAccountDetectBot.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiscordBlockedAccountDetectBot.Services
{
    public class XService
    {
        private readonly HttpClient _httpClient;
        private readonly RedisService _redisService;
        private readonly BotConfig _config;
        private readonly ILogger<XService> _logger;
        private OAuthTokenResponse? _currentToken;
        private const string TokenUrl = "https://api.twitter.com/2/oauth2/token";
        private const string AuthUrlBase = "https://twitter.com/i/oauth2/authorize";

        public XService(IHttpClientFactory httpClientFactory, RedisService redisService, BotConfig config, ILogger<XService> logger)
        {
            _httpClient = httpClientFactory.CreateClient("XApi");
            _redisService = redisService;
            _config = config;
            _logger = logger;
            LoadTokens();
        }

        private async void LoadTokens()
        {
            try
            {
                _currentToken = await _redisService.GetTokenAsync();
                if (_currentToken != null)
                {
                    _logger.LogInformation("Loaded existing X tokens from Redis.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tokens from Redis.");
            }
        }

        private async Task SaveTokensAsync()
        {
            if (_currentToken == null) return;
            try
            {
                await _redisService.SaveTokenAsync(_currentToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save tokens to Redis.");
            }
        }

        public async Task InitializeAsync()
        {
            // 1. Check Login / Valid Token
            if (_currentToken == null || string.IsNullOrEmpty(_currentToken.RefreshToken))
            {
                await PerformLoginFlowAsync();
            }
            else
            {
                if (DateTime.UtcNow >= _currentToken.ExpiresAt.AddMinutes(-1)) // Buffer
                {
                    await RefreshTokenAsync();
                }
            }

            // 2. Initial Sync
            await SyncBlockedListAsync();

            // 3. Start Loop
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(16));
                    try
                    {
                         await SyncBlockedListAsync();
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex, "Error in Sync loop");
                    }
                }
            });
        }

        private async Task PerformLoginFlowAsync()
        {
            // Generate State and Code Challenge (PKCE is required for Twitter API v2 even for confidential clients sometimes, or good practice)
            // Twitter requires PKCE for ALL OAuth 2.0 flows (User Context).
            
            var state = GenerateRandomString(16);
            var codeVerifier = GenerateRandomString(64);
            var codeChallenge = ComputeSha256Hash(codeVerifier);

            var query = System.Web.HttpUtility.ParseQueryString("");
            query["response_type"] = "code";
            query["client_id"] = _config.XApi.ClientId;
            query["redirect_uri"] = _config.XApi.RedirectUri;
            query["scope"] = _config.XApi.Scopes;
            query["state"] = state;
            query["code_challenge"] = codeChallenge;
            query["code_challenge_method"] = "S256";

            var authUrl = $"{AuthUrlBase}?{query}";
            
            Console.WriteLine("=================================================");
            Console.WriteLine("Please visit the following URL to log in to X:");
            Console.WriteLine(authUrl);
            Console.WriteLine("=================================================");

            // Start local listener
            using var listener = new HttpListener();
            listener.Prefixes.Add(_config.XApi.RedirectUri.EndsWith("/") ? _config.XApi.RedirectUri : _config.XApi.RedirectUri + "/");
            listener.Start();

            _logger.LogInformation("Listening for callback at {Url}...", _config.XApi.RedirectUri);
            
            var context = await listener.GetContextAsync();
            var req = context.Request;
            var code = req.QueryString["code"];
            var receivedState = req.QueryString["state"];

             // Respond to browser
            var resp = context.Response;
            var responseString = "<html><body>Login successful! You can close this tab.</body></html>";
            var buffer = Encoding.UTF8.GetBytes(responseString);
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            resp.OutputStream.Close();
            listener.Stop();

            if (state != receivedState)
            {
                throw new Exception("State mismatch! CSRF attack?");
            }

            if (string.IsNullOrEmpty(code))
            {
                throw new Exception("No code received.");
            }

            await ExchangeCodeForTokenAsync(code, codeVerifier);
        }

        private async Task ExchangeCodeForTokenAsync(string code, string codeVerifier)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "code", code },
                { "grant_type", "authorization_code" },
                { "client_id", _config.XApi.ClientId },
                { "redirect_uri", _config.XApi.RedirectUri },
                { "code_verifier", codeVerifier }
            });

            // Basic Auth header with Client ID & Secret
            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.XApi.ClientId}:{_config.XApi.ClientSecret}"));
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            var respContent = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            _currentToken = JsonSerializer.Deserialize<OAuthTokenResponse>(respContent);
            if (_currentToken != null)
            {
                _currentToken.ExpiresAt = DateTime.UtcNow.AddSeconds(_currentToken.ExpiresIn);
                await SaveTokensAsync();
                _logger.LogInformation("Successfully exchanged code for token.");
            }
        }

        private async Task<bool> RefreshTokenAsync()
        {
            if (_currentToken == null || string.IsNullOrEmpty(_currentToken.RefreshToken)) return false;

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", _currentToken.RefreshToken },
                { "client_id", _config.XApi.ClientId },
            });

            // Basic Auth header with Client ID & Secret is needed for confidential client refreshing usually
            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.XApi.ClientId}:{_config.XApi.ClientSecret}"));
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            request.Content = content;

            // Handle scenario where refresh token is revoked
            var response = await _httpClient.SendAsync(request);
            var respContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to refresh token: {respContent}");
                // If refresh failed (e.g. invalid grant), we might need to re-login. 
                // For now, simple error.
                return false;
            }

            _currentToken = JsonSerializer.Deserialize<OAuthTokenResponse>(respContent);
            if (_currentToken != null)
            {
                _currentToken.ExpiresAt = DateTime.UtcNow.AddSeconds(_currentToken.ExpiresIn);
                await SaveTokensAsync();
                _logger.LogInformation("Successfully refreshed token.");
                return true;
            }
            
            return false;
        }

        public async Task SyncBlockedListAsync()
        {
             if (_currentToken == null) 
             {
                 _logger.LogWarning("Cannot sync blocked list, no token.");
                 return;
             }

             // Auto Refresh if needed
             if (DateTime.UtcNow >= _currentToken.ExpiresAt.AddMinutes(-5))
             {
                 var refreshSuccess = await RefreshTokenAsync();
                 if (!refreshSuccess)
                 {
                     _logger.LogWarning("Token refresh failed. Aborting sync blocked list.");
                     return;
                 }
             }

             try 
             {
                // 1. Get Me ID (Use cached or fetch new)
                string myId;
                if (!string.IsNullOrEmpty(_currentToken.UserId))
                {
                    myId = _currentToken.UserId;
                }
                else
                {
                    await CheckRateLimitAsync("users_me");
                
                    var requestMe = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");
                    requestMe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken!.AccessToken);
                    var responseMe = await _httpClient.SendAsync(requestMe);
                
                    await ExtractAndSaveRateLimitAsync(responseMe.Headers, "users_me");

                    responseMe.EnsureSuccessStatusCode();
                    var meData = JsonSerializer.Deserialize<XUserResponse>(await responseMe.Content.ReadAsStringAsync());
                    myId = meData?.Data?.Id ?? string.Empty;

                    if (string.IsNullOrEmpty(myId)) throw new Exception("Could not retrieve User ID");

                    // Cache it
                    _currentToken.UserId = myId;
                    await SaveTokensAsync();
                }

                // 2. Get Blocking
                List<string> blockedUsernames = new List<string>();
                string? nextToken = null;
                
                do 
                {
                    await CheckRateLimitAsync("users_blocking");

                    var url = $"https://api.x.com/2/users/{myId}/blocking?max_results=1000";
                    if (!string.IsNullOrEmpty(nextToken))
                    {
                        url += $"&pagination_token={nextToken}";
                    }

                    var requestBlock = new HttpRequestMessage(HttpMethod.Get, url);
                    requestBlock.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken.AccessToken);
                    var responseBlock = await _httpClient.SendAsync(requestBlock);
                    
                    await ExtractAndSaveRateLimitAsync(responseBlock.Headers, "users_blocking");
                    
                    if(responseBlock.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                         _logger.LogWarning("Rate limit hit while fetching blocked users. Saving partial list.");
                         break; // Stop and save what we have or wait? 15 min wait is too long for main thread. Just break.
                    }
                    
                    responseBlock.EnsureSuccessStatusCode();

                    var blockData = JsonSerializer.Deserialize<XBlockingResponse>(await responseBlock.Content.ReadAsStringAsync());
                    
                    if (blockData?.Data != null)
                    {
                        blockedUsernames.AddRange(blockData.Data.Select(u => u.Username));
                    }
                    
                    nextToken = blockData?.Meta?.NextToken;

                } while (!string.IsNullOrEmpty(nextToken));

                // 3. Save to Redis
                await _redisService.SaveBlockedUsersAsync(blockedUsernames);
                _logger.LogInformation($"Synced {blockedUsernames.Count} blocked users to Redis.");

             }
             catch(Exception ex)
             {
                 _logger.LogError(ex, "Failed to sync blocked list.");
             }
        }

        private async Task CheckRateLimitAsync(string endpoint)
        {
            var rateLimit = await _redisService.GetRateLimitAsync(endpoint);
            if (rateLimit != null && rateLimit.Value.Remaining <= 0)
            {
                var resetTime = DateTimeOffset.FromUnixTimeSeconds(rateLimit.Value.Reset);
                var waitTime = resetTime - DateTimeOffset.UtcNow;
                if (waitTime > TimeSpan.Zero)
                {
                    _logger.LogWarning("Rate limit reaced for {Endpoint}. Waiting {WaitTime}...", endpoint, waitTime);
                    // In a real strict environment we might wait or throw. 
                    // This is a background task, so waiting 15 mins might be okay OR we just skip this sync cycle.
                    // Given the loop is 16 mins, maybe we just throw or return?
                    // "將其數值存取存放至 redis 內" - The requirement is mostly about storing it.
                    // But if we don't respect it, we just get 429.
                    
                    // Let's create a custom exception or just throw generic to abort this sync.
                    if (waitTime.TotalMinutes > 15) {
                        throw new Exception($"Rate limit reached for {endpoint}, reset at {resetTime}. Too long to wait.");
                    }
                    else
                    {
                         // Wait it out if it's short (e.g. seconds) or just skip? 
                         // To be safe for a background service, let's just skip this run.
                         throw new Exception($"Rate limit reached for {endpoint}. Skipping sync.");
                    }
                }
            }
        }

        private async Task ExtractAndSaveRateLimitAsync(HttpResponseHeaders headers, string endpoint)
        {
            if (headers.TryGetValues("x-rate-limit-limit", out var limitValues) &&
                headers.TryGetValues("x-rate-limit-remaining", out var remainingValues) &&
                headers.TryGetValues("x-rate-limit-reset", out var resetValues))
            {
                if (int.TryParse(limitValues.FirstOrDefault(), out int limit) &&
                    int.TryParse(remainingValues.FirstOrDefault(), out int remaining) &&
                    long.TryParse(resetValues.FirstOrDefault(), out long reset))
                {
                    await _redisService.SaveRateLimitAsync(endpoint, limit, remaining, reset);
                }
            }
        }

        private static string GenerateRandomString(int length = 32)
        {
             var randomNumber = new byte[length];
             using var rng = RandomNumberGenerator.Create();
             rng.GetBytes(randomNumber);
             return Convert.ToBase64String(randomNumber)
                 .Replace("+", "-")
                 .Replace("/", "_")
                 .Replace("=", "")
                 .Substring(0, length);
        }

        private static string ComputeSha256Hash(string data)
        {
            using var sha256 = SHA256.Create();
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
             return Convert.ToBase64String(challengeBytes)
                 .Replace("+", "-")
                 .Replace("/", "_")
                 .Replace("=", "");
        }
    }
}
