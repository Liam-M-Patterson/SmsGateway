namespace SMSGateway.Core.Interfaces;
using SmsGateway.Core.Enums;
public interface IRateLimitingService : IRateLimiterCleanup{
    Task<SendMessageResponse> CanSendMessage(string businessPhoneNumber, string accountId);
    Task TrackMessageSent(string businessPhoneNumber, string accountId);
}

public interface IRateLimiterCleanup {
    Task CleanupStaleResources();
}