namespace SMSGateway.Core.Interfaces;

public interface IRateLimitingService
{
    Task<bool> CanSendMessage(string businessPhoneNumber, string accountId);
    Task TrackMessageSent(string businessPhoneNumber, string accountId);
    Task CleanupStaleResources();
} 