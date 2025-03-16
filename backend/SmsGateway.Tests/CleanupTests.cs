using SmsGateway.Core;
using SmsGateway.Core.Enums;
using SMSGateway.Core.Models;

namespace SmsGateway.Tests;

public class CleanupTests {

    private readonly SlidingWindowRateLimiter _rateLimiter;
    private RateLimitConfig _rateLimitConfig;

    public CleanupTests() {
        _rateLimitConfig = new RateLimitConfig {
            MaxMessagesPerNumberPerSecond = 5,
            MaxMessagesPerAccountPerSecond = 10,
            RefillRate = 1, // 1 second
            ResourceExpirationTime = TimeSpan.FromSeconds(5)
        };
        var rateLimiterOptions = new Microsoft.Extensions.Options.OptionsWrapper<RateLimitConfig>(_rateLimitConfig);
        _rateLimiter = new SlidingWindowRateLimiter(rateLimiterOptions);
    }


    [Fact]
    public async Task CleanupStaleResources_ShouldRemoveAllStale() {
        for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
            await _rateLimiter.CanSendMessage(i.ToString(), "account1");
        }
        

        var phoneNumberCount = _rateLimiter._phoneNumberMessages.Count;
        var accountCount = _rateLimiter._accountMessages.Count;
        Assert.True(phoneNumberCount == _rateLimitConfig.MaxMessagesPerAccountPerSecond, $"Phone number count: {phoneNumberCount}");
        Assert.True(accountCount == 1, $"Account count: {accountCount}");

        await Task.Delay(_rateLimitConfig.ResourceExpirationTime);

        await _rateLimiter.CleanupStaleResources();

        var phoneNumberCountAfterCleanup = _rateLimiter._phoneNumberMessages.Count;
        var accountCountAfterCleanup = _rateLimiter._accountMessages.Count;
        
        Assert.True(phoneNumberCountAfterCleanup == 0, $"Phone number count after cleanup: {phoneNumberCountAfterCleanup}");
        Assert.True(accountCountAfterCleanup == 0, $"Account count after cleanup: {accountCountAfterCleanup}");
    }


    [Fact]
    public async Task CleanupStaleResources_ShouldKeepRecentMessages() {
        var resourceExpirationTime = TimeSpan.FromSeconds(5);
        var rateLimitConfig = new RateLimitConfig {
            MaxMessagesPerNumberPerSecond = 5,
            MaxMessagesPerAccountPerSecond = 10,
            RefillRate = 1, // 1 second
            ResourceExpirationTime = resourceExpirationTime
        };

        for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond/2; i++) {
            await _rateLimiter.CanSendMessage(i.ToString(), "account1");
        }
        

        var phoneNumberCount = _rateLimiter._phoneNumberMessages.Count;
        var accountCount = _rateLimiter._accountMessages.Count;
        Assert.True(phoneNumberCount == _rateLimitConfig.MaxMessagesPerAccountPerSecond/2, $"Phone number count: {phoneNumberCount}");
        Assert.True(accountCount == 1, $"Account count: {accountCount}");

        //Wait some time, but not enough for the resources to expire
        await Task.Delay(TimeSpan.FromSeconds(4));
        for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond/2; i++) {
            await _rateLimiter.CanSendMessage(i.ToString(), "account1");
        }

        //Wait the remaining time to allow first batch to expire.
        await Task.Delay(TimeSpan.FromSeconds(1));

        await _rateLimiter.CleanupStaleResources();

        var phoneNumberCountAfterCleanup = _rateLimiter._phoneNumberMessages.Count;
        var accountCountAfterCleanup = _rateLimiter._accountMessages.Count;
        
        Assert.True(phoneNumberCountAfterCleanup == _rateLimitConfig.MaxMessagesPerAccountPerSecond/2, $"Phone number count after cleanup: {phoneNumberCountAfterCleanup}");
        Assert.True(accountCountAfterCleanup == 1, $"Account count after cleanup: {accountCountAfterCleanup}");

    }
}
