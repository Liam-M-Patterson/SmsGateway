using SmsGateway.Core;
using SmsGateway.Core.Enums;
using SMSGateway.Core.Models;
using System;
using System.Threading.Tasks;

namespace SmsGateway.Tests {
    public class SlidingWindowRateLimiterTests {
        private readonly SlidingWindowRateLimiter _rateLimiter;
        private RateLimitConfig _rateLimitConfig;

        public SlidingWindowRateLimiterTests() {
            _rateLimitConfig = new RateLimitConfig {
                MaxMessagesPerNumberPerSecond = 5,
                MaxMessagesPerAccountPerSecond = 10,
                RefillRate = 1// 1 second
            };
            var rateLimiterOptions = new Microsoft.Extensions.Options.OptionsWrapper<RateLimitConfig>(_rateLimitConfig);
            _rateLimiter = new SlidingWindowRateLimiter(rateLimiterOptions);
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_WhenUnderPhoneNumberLimit() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.Success);
            }
        }

        [Fact]
        public async Task CanSendMessage_ShouldDeny_WhenOverPhoneNumberLimit() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1")== SendMessageResponse.Success);
            }

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.PhoneNumberRateLimited);
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_AfterPhoneNumberLimitResets() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.Success, "Failed on iteration " + i);
            }

            await Task.Delay(TimeSpan.FromSeconds(_rateLimitConfig.RefillRate)); // Wait for refill rate to reset

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.Success, "Failed after refill rate reset");
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_WhenUnderAccountLimit() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage(i.ToString(), "account1") == SendMessageResponse.Success, "Failed on iteration " + i);
            }
        }

        [Fact]
        public async Task CanSendMessage_ShouldDeny_WhenOverAccountLimit() {

            var rand = new Random();
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
                var phoneNumber = rand.Next(1000000000).ToString();
                Assert.True(await _rateLimiter.CanSendMessage(phoneNumber, "account1") == SendMessageResponse.Success, "Failed on iteration " + i);
            }

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.AccountRateLimited);
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_AfterAccountLimitResets() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage(i.ToString(), "account1") == SendMessageResponse.Success, "Failed on iteration " + i);
            }

            await Task.Delay(TimeSpan.FromSeconds(_rateLimitConfig.RefillRate)); // Wait for refill rate to reset

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.Success, "Failed after refill rate reset");
        }



        [Fact]
        public async Task CanSendMessage_ShouldAllow_MultiplePhoneNumbersIndependently() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.Success);
                Assert.True(await _rateLimiter.CanSendMessage("0987654321", "account1") == SendMessageResponse.Success);
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.AccountRateLimited);
            Assert.False(await _rateLimiter.CanSendMessage("0987654321", "account1") == SendMessageResponse.AccountRateLimited);
        }

        [Fact]
        public async Task CanSendMessage_ShouldAllow_MultiplePhoneNumbersToReachAccountMax() {
            var rand = new Random();
            int i;
            for (i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond; i++) {
                var phoneNumber = rand.Next(1000000000).ToString();
                var canSend = await _rateLimiter.CanSendMessage(phoneNumber, "account1");
            }

            Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.AccountRateLimited);
        }

        [Fact]
        public async Task CanSendMessage_ShouldHandleMultipleAccountsIndependently() {
            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerNumberPerSecond; i++) {
                Assert.True(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.Success);
                Assert.True(await _rateLimiter.CanSendMessage("9876543210", "account2") == SendMessageResponse.Success);
            }

            Assert.False(await _rateLimiter.CanSendMessage("1234567890", "account1") == SendMessageResponse.AccountRateLimited);
            Assert.False(await _rateLimiter.CanSendMessage("9876543210", "account2") == SendMessageResponse.AccountRateLimited);
        }

        [Fact]
        public async Task CanSend_ShouldHandleConcurrentRequests() {
            var taskList = new List<Task<SendMessageResponse>>();
            ManualResetEventSlim startBatchSend = new ManualResetEventSlim(false);

            var rand = new Random();

            for (int i = 0; i < _rateLimitConfig.MaxMessagesPerAccountPerSecond * 2; i++) {
                int taskId = i;


                taskList.Add(Task.Run(async () => {
                    var phoneNumber = rand.Next(1000000000).ToString();
                    startBatchSend.Wait();
                    var canSend = await _rateLimiter.CanSendMessage(phoneNumber, "account1");

                    Console.WriteLine($"Task {taskId} completed, sent: {canSend}");
                    return canSend;
                }));
            }

            startBatchSend.Set();
            await Task.WhenAll(taskList);

            var numSent = taskList.Count(t => t.Result == SendMessageResponse.Success);
            Console.WriteLine($"Number of messages sent: {numSent}");
            Assert.True(numSent <= _rateLimitConfig.MaxMessagesPerAccountPerSecond);
        }

    }
}