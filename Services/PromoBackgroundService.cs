using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

public class PromoBackgroundService : BackgroundService
{
    private readonly PromoService _promoService;
    private readonly DiscordService _discordService;
    private readonly BotConfiguration _config;
    private readonly ILogger<PromoBackgroundService> _logger;

    public PromoBackgroundService(
        PromoService promoService,
        DiscordService discordService,
        IOptions<BotConfiguration> config,
        ILogger<PromoBackgroundService> logger)
    {
        _promoService = promoService;
        _discordService = discordService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "⏰ PromoBackgroundService started. Post every {Hours}h to channel ID: {ChannelId}",
            _config.PromoIntervalHours,
            _config.PromoChannelId);

        // Đợi 1 phút để Discord Ready trước khi post lần đầu
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (embed, components) = _promoService.BuildNextPromo();
                await _discordService.SendPromoAsync(embed, components);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PromoBackgroundService error");
            }

            await Task.Delay(
                TimeSpan.FromHours(_config.PromoIntervalHours),
                stoppingToken);
        }
    }
}