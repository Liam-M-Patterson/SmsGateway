namespace SmsGateway.Core.Enums;
public enum SendMessageResponse {
    Success = 1,
    PhoneNumberRateLimited = 2,
    AccountRateLimited = 3,
    FailureGeneral = 4
}
