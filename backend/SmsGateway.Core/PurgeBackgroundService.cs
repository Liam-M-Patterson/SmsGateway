using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SMSGateway.Core.Models;

namespace SMSGateway.Core.Interfaces;

public class PurgeBackgroundService : BackgroundService {
    private readonly IRateLimiterCleanup _rateLimiter;
    private readonly TimeSpan _purgeInterval; // Interval for purging stale resources
    private readonly RateLimitConfig _rateLimitConfig;

    //Inject as a IRateLimitingService, since this is what the DI container registers. 
    //But we only need the IRateLimiterCleanup piece in this service
    public PurgeBackgroundService(IRateLimitingService rateLimiter, IOptions<RateLimitConfig> rateLimitConfig) {
        _rateLimiter = rateLimiter;
        _rateLimitConfig = rateLimitConfig.Value;
        _purgeInterval = _rateLimitConfig.ResourceExpirationTime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            await _rateLimiter.CleanupStaleResources();
            await Task.Delay(_purgeInterval, stoppingToken);
        }
    }
}
