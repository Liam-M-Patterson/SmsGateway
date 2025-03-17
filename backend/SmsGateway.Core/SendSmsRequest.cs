namespace SMSGateway.Core.Models;

public class SendSmsRequest {
    public string BusinessPhoneNumber { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
}