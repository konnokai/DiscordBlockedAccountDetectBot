using DiscordBlockedAccountDetectBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DiscordBlockedAccountDetectBot
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // 從環境變數讀取 .env 文件路徑，默認為根目錄的 .env
                    var envFilePath = Environment.GetEnvironmentVariable("ENV_FILE_PATH") ?? ".env";
                    
                    if (File.Exists(envFilePath))
                    {
                        // 讀取 .env 文件並設置環境變數
                        foreach (var line in File.ReadAllLines(envFilePath))
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                                continue;

                            var parts = line.Split('=', 2);
                            if (parts.Length == 2)
                            {
                                var key = parts[0].Trim();
                                var value = parts[1].Trim();
                                Environment.SetEnvironmentVariable(key, value);
                            }
                        }
                    }

                    // 從環境變數讀取配置
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Discord:Token", Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "" },
                        { "XApi:ClientId", Environment.GetEnvironmentVariable("XAPI_CLIENT_ID") ?? "" },
                        { "XApi:ClientSecret", Environment.GetEnvironmentVariable("XAPI_CLIENT_SECRET") ?? "" },
                        { "XApi:RedirectUri", Environment.GetEnvironmentVariable("XAPI_REDIRECT_URI") ?? "http://127.0.0.1:3000/callback" },
                        { "XApi:Scopes", Environment.GetEnvironmentVariable("XAPI_SCOPES") ?? "tweet.read users.read block.read offline.access" },
                        { "Redis:ConnectionString", Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "localhost:6379" }
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var botConfig = new BotConfig();
                    configuration.Bind(botConfig);
                    services.AddSingleton(botConfig);

                    services.AddSingleton<RedisService>();
                    services.AddSingleton<XService>();
                    services.AddHttpClient("XApi")
                        .ConfigureHttpClient(client =>
                        {
                            client.DefaultRequestHeaders.Add("User-Agent", "DiscordBlockedAccountDetectBot/1.0");
                        });
                    services.AddHttpClient("VXApi");
                    services.AddHostedService<DiscordBotService>(); // This starts the bot
                })
                .Build();

            await host.RunAsync();
        }
    }
}
