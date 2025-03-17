using Microsoft.AspNetCore.Mvc;
using SMSGateway.Core.Interfaces;
using SMSGateway.Core.Models;

namespace SMSGateway.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SmsController : ControllerBase {
    private readonly IRateLimitingService _rateLimitingService;
    private readonly ILogger<SmsController> _logger;

    public SmsController(
        IRateLimitingService rateLimitingService,
        ILogger<SmsController> logger) {
        _rateLimitingService = rateLimitingService;
        _logger = logger;
    }

    [HttpPost("check")]
    public async Task<IActionResult> CheckCanSendMessage([FromBody] SendSmsRequest request) {
        try {
            var canSend = await _rateLimitingService.CanSendMessage(
                request.BusinessPhoneNumber,
                request.AccountId);

            return Ok(new { canSend });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error checking message rate limit");
            return StatusCode(500, "An error occurred while checking rate limits");
        }
    }
}