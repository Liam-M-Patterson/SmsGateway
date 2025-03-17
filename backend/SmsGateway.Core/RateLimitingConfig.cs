namespace SMSGateway.Core.Models;

public class RateLimitConfig {
    public int RefillRate { get; set; }
    public int MaxMessagesPerNumberPerSecond { get; set; }
    public int MaxMessagesPerAccountPerSecond { get; set; }
    public TimeSpan ResourceExpirationTime { get; set; } = TimeSpan.FromHours(1);
}
