using System.Collections.Concurrent;
using SMSGateway.Core.Interfaces;
using SMSGateway.Core.Models;
using SmsGateway.Core.Enums;
using Microsoft.Extensions.Options;

namespace SmsGateway.Core;

public class SlidingWindowRateLimiter : IRateLimitingService {
    private RateLimitConfig _rateLimitConfig { init; get; }
    private Dictionary<string, Queue<DateTime>> _phoneNumberMessages = new();
    private Dictionary<string, Queue<DateTime>> _accountMessages = new();
    private readonly ReaderWriterLockSlim _phoneNumberLock = new();
    private readonly ReaderWriterLockSlim _accountLock = new();

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
        if (now - _phoneNumberMessages[phoneNumber].Peek() < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate))
            return false; //Message is too new, at the limit

        _phoneNumberLock.EnterWriteLock();
        try {
            while (_phoneNumberMessages[phoneNumber].Count > 0 && CanPurgeMessageByTime(_phoneNumberMessages[phoneNumber].Peek(), now)) {
                _phoneNumberMessages[phoneNumber].Dequeue();
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
        // if (!CanPurgeMessageByTime(_accountMessages[accountId].Peek(), now))
        if (now - _accountMessages[accountId].Peek() < TimeSpan.FromSeconds(_rateLimitConfig.RefillRate))
            return false; //Message is too new, at the limit

        _accountLock.EnterWriteLock();
        try {
            while (_accountMessages[accountId].Count > 0 && CanPurgeMessageByTime(_accountMessages[accountId].Peek(), now)) {
                _accountMessages[accountId].Dequeue();
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
            _phoneNumberMessages[phoneNumber].Enqueue(now);
        }
        finally {
            _phoneNumberLock.ExitWriteLock();
        }
    }

    private void AddAccountIdMessage(string accountId, DateTime now) {
        _accountLock.EnterWriteLock();
        try {
            _accountMessages[accountId].Enqueue(now);
        }
        finally {
            _accountLock.ExitWriteLock();
        }
    }


    private bool CanPurgeMessageByTime(DateTime msg, DateTime now) {
        return (now - msg).Nanoseconds >= TimeSpan.FromSeconds(_rateLimitConfig.RefillRate).Nanoseconds;
    }

    public async Task TrackMessageSent(string businessPhoneNumber, string accountId) {
        return;
    }

    public async Task CleanupStaleResources() {
        return;
    }


}
