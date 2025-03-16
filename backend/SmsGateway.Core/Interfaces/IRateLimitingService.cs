namespace SMSGateway.Core.Interfaces;
using SmsGateway.Core.Enums;
public interface IRateLimitingService {
    Task<SendMessageResponse> CanSendMessage(string businessPhoneNumber, string accountId);
    Task TrackMessageSent(string businessPhoneNumber, string accountId);
    Task CleanupStaleResources();
}