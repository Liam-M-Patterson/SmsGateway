using SMSGateway.Core.Interfaces;
using SMSGateway.Core.Models;
using SmsGateway.Core.Enums;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SmsGateway.Tests")]
namespace SmsGateway.Core;



public class SlidingWindowRateLimiter : IRateLimitingService {
    private RateLimitConfig _rateLimitConfig { init; get; }
    internal Dictionary<string, LinkedList<DateTime>> _phoneNumberMessages = new();
    internal Dictionary<string, LinkedList<DateTime>> _accountMessages = new();
    internal readonly ReaderWriterLockSlim _phoneNumberLock = new();
    internal readonly ReaderWriterLockSlim _accountLock = new();

    public SlidingWindowRateLimiter(IOptions<RateLimitConfig> rateLimitConfig) {
        _rateLimitConfig = rateLimitConfig.Value;
    }

    public async Task<SendMessageResponse> CanSendMessage(string businessPhoneNumber, string accountId) {
        DateTime now = DateTime.UtcNow;

        _phoneNumberLock.EnterUpgradeableReadLock();
        try {
            if (!CheckPhoneNumber(businessPhoneNumber, now))
                return SendMessageResponse.PhoneNumberRateLimited;

            _accountLock.EnterUpgradeableReadLock();
            try {
                if (!CheckAccountId(accountId, now))
                    return SendMessageResponse.AccountRateLimited;

                AddPhoneNumberMessage(businessPhoneNumber, now);
                AddAccountIdMessage(accountId, now);
            }
            finally {
                _accountLock.ExitUpgradeableReadLock();
            }
        }
        finally {
            _phoneNumberLock.ExitUpgradeableReadLock();
        }

        return SendMessageResponse.Success;
    }

    private bool CheckPhoneNumber(string phoneNumber, DateTime now) {
        if (!_phoneNumberMessages.ContainsKey(phoneNumber)) {
            _phoneNumberMessages[phoneNumber] = new();
            return true;
        }

        if (_phoneNumberMessages[phoneNumber].Count < _rateLimitConfig.MaxMessagesPerNumberPerSecond)
            return true;

        //Check if we can remove expired messages
        if (now - _phoneNumberMessages[phoneNumber].First() < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate))
            return false; //Message is too new, at the limit

        _phoneNumberLock.EnterWriteLock();
        try {
            while (_phoneNumberMessages[phoneNumber].Count > 0 && CanPurgeMessageByTime(_phoneNumberMessages[phoneNumber].First(), now)) {
                _phoneNumberMessages[phoneNumber].RemoveFirst();
            }
        }
        finally {
            _phoneNumberLock.ExitWriteLock();
        }

        return true;
    }

    private bool CheckAccountId(string accountId, DateTime now) {
        if (!_accountMessages.ContainsKey(accountId)) {
            _accountMessages[accountId] = new();
            return true;
        }

        if (_accountMessages[accountId].Count < _rateLimitConfig.MaxMessagesPerAccountPerSecond)
            return true;


        //Check if we can remove expired messages
        if (now - _accountMessages[accountId].First() < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate))
            return false; //Message is too new, at the limit

        _accountLock.EnterWriteLock();
        try {
            while (_accountMessages[accountId].Count > 0 && CanPurgeMessageByTime(_accountMessages[accountId].First(), now)) {
                _accountMessages[accountId].RemoveFirst();
            }
        }
        finally {
            _accountLock.ExitWriteLock();
        }

        return true;
    }

    private void AddPhoneNumberMessage(string phoneNumber, DateTime now) {
        _phoneNumberLock.EnterWriteLock();
        try {
            _phoneNumberMessages[phoneNumber].AddLast(now);
        }
        finally {
            _phoneNumberLock.ExitWriteLock();
        }
    }

    private void AddAccountIdMessage(string accountId, DateTime now) {
        _accountLock.EnterWriteLock();
        try {
            _accountMessages[accountId].AddLast(now);
        }
        finally {
            _accountLock.ExitWriteLock();
        }
    }


    private bool CanPurgeMessageByTime(DateTime msg, DateTime now) {
        return (now - msg).Nanoseconds >= TimeSpan.FromSeconds(_rateLimitConfig.RefillRate).Nanoseconds;
    }

    private void PurgeExpiredMessages(Queue<DateTime> messages, DateTime now) {
        while (messages.Count > 0 && CanPurgeMessageByTime(messages.Peek(), now)) {
            messages.Dequeue();
        }
    }

    public async Task CleanupStaleResources() {
        DateTime now = DateTime.UtcNow;

        // Process phonenumbers and accounts in parallel, since they are independent
        await Task.WhenAll(
            Task.Run(() => PurgeMessages(_phoneNumberLock, _phoneNumberMessages, now, _rateLimitConfig.ResourceExpirationTime)),
            Task.Run(() => PurgeMessages(_accountLock, _accountMessages, now, _rateLimitConfig.ResourceExpirationTime))
        );
    }

    private void PurgeMessages(ReaderWriterLockSlim lockObj, Dictionary<string, LinkedList<DateTime>> messageQueue, DateTime now, TimeSpan expirationTime) {
        lockObj.EnterWriteLock();

        try {
            var messagesToProcess = messageQueue.Keys.ToList();
            //Since we have a fixed list of messages to process, we can use a parallel for loop
            Parallel.ForEach(messagesToProcess, messageId => {
                // Perform purging logic for each messageId
                if (messageQueue.ContainsKey(messageId)) {
                    var messageList = messageQueue[messageId];

                    // If the newest message is older than the expiration time, we can remove all messages and return faster
                    var newestMessage = messageList.Last();
                    if (now - newestMessage > expirationTime) {
                        messageQueue.Remove(messageId); 
                        return;
                    }

                    // Remove expired messages
                    while (messageList.Count > 0 && now - messageList.First() > expirationTime) {
                        messageList.RemoveFirst();
                    }

                    // If no messages left, remove the messageId from the queue
                    if (messageList.Count == 0) {
                        messageQueue.Remove(messageId);
                    }
                }
            });
        }
        finally {
            if (lockObj.IsWriteLockHeld)
                lockObj.ExitWriteLock();

        }
    }
}
