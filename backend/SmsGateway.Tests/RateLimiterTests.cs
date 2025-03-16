using SmsGateway.Core;
using SMSGateway.Core.Models;
using System;
using System.Threading.Tasks;

namespace SmsGateway.Tests
{
    public class SlidingWindowRateLimiterTests
    {
        private readonly SlidingWindowRateLimiter _rateLimiter;
        private RateLimitConfig _rateLimitConfig;
        
        public SlidingWindowRateLimiterTests()
        {
            _rateLimitConfig = new RateLimitConfig
            {
                MaxMessagesPerNumberPerSecond = 5,
                MaxMessagesPerAccountPerSecond = 10,
                RefillRate = 1// 1 second
            };
            _rateLimiter = new SlidingWindowRateLimiter(_rateLimitConfig);
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_WhenUnderPhoneNumberLimit()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1"));
            }
        }

        [Fact]
        public async Task CanSendMessage_ShouldDeny_WhenOverPhoneNumberLimit()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1"));
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1"));
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_AfterPhoneNumberLimitResets()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1"), "Failed on iteration " + i);
            }

            await Task.Delay(TimeSpan.FromSeconds(_rateLimitConfig.RefillRate)); // Wait for refill rate to reset

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1"), "Failed after refill rate reset");
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_WhenUnderAccountLimit()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage(i.ToString(), "account1"), "Failed on iteration " + i);
            }
        }

        [Fact]
        public async Task CanSendMessage_ShouldDeny_WhenOverAccountLimit()
        {

            var rand = new Random();
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++)
            {
                var phoneNumber = rand.Next(1000000000).ToString();
                Assert.True(await _rateLimiter.CanSendMessage(phoneNumber, "account1"));
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1"));
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_AfterAccountLimitResets()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage(i.ToString(), "account1"), "Failed on iteration " + i);
            }

            await Task.Delay(TimeSpan.FromSeconds(_rateLimitConfig.RefillRate)); // Wait for refill rate to reset

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1"), "Failed after refill rate reset");
        }



        [Fact]
        public async Task CanSendMessage_ShouldAllow_MultiplePhoneNumbersIndependently()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "accorunt1"));
                Assert.True(await _rateLimiter.CanSendMessage("0987654321", "account1"));
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1"));
            Assert.False(await _rateLimiter.CanSendMessage("0987654321", "account1"));
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_MultiplePhoneNumbersToReachAccountMax()
        {
            var phoneNumber = "1234567890";
            var rand = new Random();
            int i;
            for (i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++)
            {
                var canSend = await _rateLimiter.CanSendMessage(phoneNumber, "account1");
                if (!canSend) {
                    phoneNumber = rand.Next(1000000000).ToString();
                }
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1"));            
        }

        [Fact]
        public async Task CanSendMessage_ShouldHandleMultipleAccountsIndependently()
        {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++)
            {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1"));
                Assert.True(await _rateLimiter.CanSendMessage("9876543210", "account2"));
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1"));
            Assert.False(await _rateLimiter.CanSendMessage("9876543210", "account2"));
        }

        [Fact]
        public async Task CanSend_ShouldHandleConcurrentRequests() {
            var taskList = new List<Task<bool>>();
            ManualResetEventSlim startBatchSend = new ManualResetEventSlim(false);
            
            var rand = new Random();

            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond*2; i++) {
                int taskId = i;

                
                taskList.Add(Task.Run( async () => {
                    var phoneNumber = rand.Next(1000000000).ToString();
                    startBatchSend.Wait();
                    var canSend = await _rateLimiter.CanSendMessage(phoneNumber, "account1");

                    Console.WriteLine($"Task {taskId} completed, sent: {canSend}");
                    return canSend;
                }));
            }
            
            startBatchSend.Set();
            await Task.WhenAll(taskList);
            
            var numSent = taskList.Count(t => t.Result == true);
            Console.WriteLine($"Number of messages sent: {numSent}");
            Assert.True(numSent <= _rateLimitConfig.MaxMessagesPerAccountPerSecond);
        }

    }
}