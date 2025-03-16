using SmsGateway.Core;
using SmsGateway.Core.Enums;
using SMSGateway.Core.Models;

namespace SmsGateway.Tests;


public class PerformanceTests {

    private readonly SlidingWindowRateLimiter _rateLimiter;
    private RateLimitConfig _rateLimitConfig;

    public PerformanceTests() {
        _rateLimitConfig = new RateLimitConfig {
            MaxMessagesPerNumberPerSecond = 5,
            MaxMessagesPerAccountPerSecond = 10,
            RefillRate = 1// 1 second
        };
        var rateLimiterOptions = new Microsoft.Extensions.Options.OptionsWrapper<RateLimitConfig>(_rateLimitConfig);
        _rateLimiter = new SlidingWindowRateLimiter(rateLimiterOptions);
    }

    public async Task<TimeSpan> MakeRequestUntilSuccessful() {
        for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++) {
            await _rateLimiter.CanSendMessage("1234567890", "account1");
        }
        DateTime limitReached = DateTime.UtcNow;

        SendMessageResponse canSend = SendMessageResponse.FailureGeneral;
        while (canSend != SendMessageResponse.Success) {
            canSend = await _rateLimiter.CanSendMessage("1234567890", "account1");
        }
        DateTime resetTime = DateTime.UtcNow;
        return resetTime - limitReached; ;
    }

    [Fact]
    public async Task CanSendMessage_RefillHappeningOnTime() {
        var timeToReset = await MakeRequestUntilSuccessful();

        var tolerance = TimeSpan.FromMilliseconds(1); //Allow for 5 microseconds of error
        Assert.True(timeToReset >= (TimeSpan.FromSeconds(_rateLimitConfig.RefillRate) - tolerance), $"Time to reset: {timeToReset}");
    }

    [Fact]
    public async Task ParallelCanSendMessageTest() {
        var totalTime = 0.0;
        var iterations = 3;
        var numEarly = 0;

        for (int j = 0; j < iterations; j++) {
            var timeToReset = await MakeRequestUntilSuccessful();

            if (timeToReset < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate)) {
                totalTime += timeToReset.Nanoseconds - TimeSpan.FromSeconds(_rateLimitConfig.RefillRate).Nanoseconds;
                numEarly++;
                Console.WriteLine($"Total early return time: {totalTime}");
            }
            await Task.Delay(TimeSpan.FromSeconds(_rateLimitConfig.RefillRate));
        }

        var averageTime = totalTime / numEarly;
        Assert.True(averageTime < 500, $"Average time to was {averageTime} nanoseconds");
    }
}
