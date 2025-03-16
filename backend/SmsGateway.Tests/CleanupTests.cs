using SmsGateway.Core;
using SmsGateway.Core.Enums;
using SMSGateway.Core.Models;
using Microsoft.Extensions.Options;
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
        var rateLimiterOptions = new OptionsWrapper<RateLimitConfig>(_rateLimitConfig);
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

    [Fact]
    public async Task CleanupStaleResources_ShouldPurgeExpiredPhoneNumbers() {
        // Arrange: Add some phone numbers with expired and valid messages
        DateTime now = DateTime.UtcNow;
        _rateLimiter._phoneNumberMessages.Add("12345", new LinkedList<DateTime>());
        _rateLimiter._phoneNumberMessages["12345"].AddLast(now.AddSeconds(-(_rateLimitConfig.ResourceExpirationTime.Seconds + 1))); // expired message
        _rateLimiter._phoneNumberMessages["12345"].AddLast(now.AddSeconds(-_rateLimitConfig.ResourceExpirationTime.Seconds)); // valid message
        _rateLimiter._phoneNumberMessages["12345"].AddLast(now.AddSeconds(-(_rateLimitConfig.ResourceExpirationTime.Seconds -1))); // valid message
        _rateLimiter._phoneNumberMessages.Add("67890", new LinkedList<DateTime>());
        _rateLimiter._phoneNumberMessages["67890"].AddLast(now.AddSeconds(-_rateLimitConfig.ResourceExpirationTime.Seconds)); // expired message

        Assert.True(_rateLimiter._phoneNumberMessages["12345"].Count == 3);
        // Act: Perform the cleanup operation
        await _rateLimiter.CleanupStaleResources();
        
        // Assert: Verify that expired phone numbers are purged
        Assert.True(_rateLimiter._phoneNumberMessages["12345"].Count == 1); // only valid messages should remain
        Assert.False(_rateLimiter._phoneNumberMessages.ContainsKey("67890")); // should remain with valid message
    }

    [Fact]
    public async Task CleanupStaleResources_ShouldNotCauseDeadlocks() {
        // Arrange: Add phone numbers and accounts with valid messages
        DateTime now = DateTime.UtcNow;
        var rateLimitConfig = new RateLimitConfig {
            MaxMessagesPerNumberPerSecond = 5,
            MaxMessagesPerAccountPerSecond = 10000,
            RefillRate = 1, // 1 second
            ResourceExpirationTime = TimeSpan.FromSeconds(5)
        };
        var rateLimiter = new SlidingWindowRateLimiter(new OptionsWrapper<RateLimitConfig>(rateLimitConfig));

        await Task.Run(() => {
            for (int i =0; i < rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
                rateLimiter.CanSendMessage(i.ToString(), "account1");
            }
        });

        await Task.Delay(rateLimitConfig.RefillRate); // Wait for the messages to be added
        // Act: Perform the cleanup operation concurrently
        var cleanupTask = rateLimiter.CleanupStaleResources();
        var addedTask = Task.Run(() => {
            for (int i =0; i < rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
                rateLimiter.CanSendMessage(i.ToString(), "account1");
            }
        });
        // Assert: Verify that the cleanup does not cause deadlocks
        await Task.WhenAny(cleanupTask, addedTask, Task.Delay(5000)); // Ensure that it completes within a reasonable time

        Assert.True(cleanupTask.IsCompleted); // Task should complete without deadlocks
    }

    [Fact]
    public async Task CleanupStaleResources_ShouldPurgeMessagesOlderThanExpirationTime() {
        // Arrange: Add some phone numbers and accounts with mixed valid and expired messages
        DateTime now = DateTime.UtcNow;
        _rateLimiter._phoneNumberMessages.Add("12345", new LinkedList<DateTime>());
        _rateLimiter._phoneNumberMessages["12345"].AddLast(now.AddSeconds(-(_rateLimitConfig.ResourceExpirationTime.Seconds+1))); // expired message
        _rateLimiter._phoneNumberMessages["12345"].AddLast(now.AddSeconds(-(_rateLimitConfig.ResourceExpirationTime.Seconds-1))); // valid message
        _rateLimiter._accountMessages.Add("account1", new LinkedList<DateTime>());
        _rateLimiter._accountMessages["account1"].AddLast(now.AddSeconds(-(_rateLimitConfig.ResourceExpirationTime.Seconds+1))); // expired message
        _rateLimiter._accountMessages.Add("account2", new LinkedList<DateTime>());
        _rateLimiter._accountMessages["account2"].AddLast(now.AddSeconds(-(_rateLimitConfig.ResourceExpirationTime.Seconds-1))); // valid message

        // Act: Perform the cleanup operation
        await _rateLimiter.CleanupStaleResources();

        // Assert: Verify that expired messages are purged, but valid ones remain
        Assert.True(_rateLimiter._phoneNumberMessages.ContainsKey("12345")); // should remain
        Assert.Equal(1, _rateLimiter._phoneNumberMessages["12345"].Count); // only valid message should remain
        Assert.False(_rateLimiter._accountMessages.ContainsKey("account1")); // account1 should be purged
        Assert.True(_rateLimiter._accountMessages.ContainsKey("account2")); // account2 should remain
    }

    [Fact]
    public async Task CleanupStaleResources_ShouldHandleLargeDataSets() {
        // Arrange: Add a large number of phone numbers and accounts
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < 10000; i++) {
            _rateLimiter._phoneNumberMessages.Add($"phone{i}", new LinkedList<DateTime>());
            _rateLimiter._phoneNumberMessages[$"phone{i}"].AddLast(now.AddSeconds(-70)); // expired message
            _rateLimiter._accountMessages.Add($"account{i}", new LinkedList<DateTime>());
            _rateLimiter._accountMessages[$"account{i}"].AddLast(now.AddSeconds(-70)); // expired message
        }

        // Act: Perform the cleanup operation
        await _rateLimiter.CleanupStaleResources();

        // Assert: Verify that all the phone numbers and accounts are purged
        Assert.Empty(_rateLimiter._phoneNumberMessages); // all should be purged
        Assert.Empty(_rateLimiter._accountMessages); // all should be purged
    }
}
