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

    // private readonly Dictionary

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
        // if (!CanPurgeMessageByTime(_phoneNumberMessages[phoneNumber].Peek(), now))
        if (now - _phoneNumberMessages[phoneNumber].First() < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate))
            return false; //Message is too new, at the limit

        _phoneNumberLock.EnterWriteLock();
        try {
            while (_phoneNumberMessages[phoneNumber].Count > 0 && CanPurgeMessageByTime(_phoneNumberMessages[phoneNumber].First(), now)) {
                _phoneNumberMessages[phoneNumber].RemoveFirst();
            }
            // PurgeExpiredMessages(_phoneNumberMessages[phoneNumber], now);
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
        // if (!CanPurgeMessageByTime(_accountMessages[accountId].Peek(), now))
        if (now - _accountMessages[accountId].First() < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate))
            return false; //Message is too new, at the limit

        _accountLock.EnterWriteLock();
        try {
            while (_accountMessages[accountId].Count > 0 && CanPurgeMessageByTime(_accountMessages[accountId].First(), now)) {
                _accountMessages[accountId].RemoveFirst();
            }
            // PurgeExpiredMessages(_accountMessages[accountId], now);
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

    public async Task TrackMessageSent(string businessPhoneNumber, string accountId) {
        return;
    }



    public async Task CleanupStaleResources() {
        DateTime now = DateTime.UtcNow;
        var phoneNumberCleanupTask = CleanupResourceAsync(_phoneNumberMessages, _phoneNumberLock, now, _rateLimitConfig.ResourceExpirationTime);
        var accountCleanupTask = CleanupResourceAsync(_accountMessages, _accountLock, now, _rateLimitConfig.ResourceExpirationTime);
        await Task.WhenAll(phoneNumberCleanupTask, accountCleanupTask);
    }

    private async Task CleanupResourceAsync(Dictionary<string, LinkedList<DateTime>> messages, ReaderWriterLockSlim lockObj, DateTime now, TimeSpan expirationTime) {
        lockObj.EnterUpgradeableReadLock();
        //Initialize a dictionary with each id as a string, and a 0 count
        Dictionary<string, int> messagesToPurge = messages.Keys.ToDictionary(id => id, id => 0);
        try {
            foreach (var messageId in messagesToPurge.Keys) {

                // If the newest message is older than the expiration time, we can remove all messages
                var newestMessage = messages[messageId].Last();
                if (now - newestMessage > expirationTime) {
                    messagesToPurge[messageId] = -1; //-1 indicates remove all
                    continue;
                }

                var node = messages[messageId].First;
                if (node == null) {
                    continue;
                }

                var count = 0;
                var numAccountIds = messages[messageId].Count;
                while (count < numAccountIds && now - node.Value < expirationTime) {
                    node = node.Next;
                    if (node == null) {
                        break;
                    }
                    count++;
                }

                if (count == numAccountIds) {
                    messagesToPurge[messageId] = -1;
                } else {
                    messagesToPurge[messageId] = count;
                }
            }

            //Enter a write lock to remove the messages
            try {
                lockObj.EnterWriteLock();
                foreach (var messageId in messagesToPurge.Keys) {
                    if (messagesToPurge[messageId] == -1) {
                        messages.Remove(messageId);
                    } else {
                        while (messagesToPurge[messageId] > 0) {
                            messages[messageId].RemoveFirst();
                            messagesToPurge[messageId]--;
                        }
                    }
                }
            }
            finally {
                if (lockObj.IsWriteLockHeld) 
                    lockObj.ExitWriteLock();
            }
        }
        finally {
            lockObj.ExitUpgradeableReadLock();
        }
    }


}
