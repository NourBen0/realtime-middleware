using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealtimeMiddleware.Api.Auth;
using RealtimeMiddleware.Application.DTOs;
using RealtimeMiddleware.Application.Interfaces;

namespace RealtimeMiddleware.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMessageService messageService, ILogger<MessagesController> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Publish([FromBody] PublishMessageRequest request, CancellationToken ct)
    {
        var result = await _messageService.PublishAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, new ApiResponse<MessageResponse>(true, result));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var messages = await _messageService.GetAllAsync(ct);
        return Ok(new ApiResponse<IEnumerable<MessageResponse>>(true, messages));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var msg = await _messageService.GetByIdAsync(id, ct);
        if (msg == null) return NotFound(new ApiResponse<MessageResponse>(false, null, "Message not found"));
        return Ok(new ApiResponse<MessageResponse>(true, msg));
    }

    [HttpGet("topic/{topic}")]
    public async Task<IActionResult> GetByTopic(string topic, CancellationToken ct)
    {
        var messages = await _messageService.GetByTopicAsync(topic, ct);
        return Ok(new ApiResponse<IEnumerable<MessageResponse>>(true, messages));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await _messageService.GetStatsAsync(ct);
        return Ok(new ApiResponse<StatsResponse>(true, stats));
    }

    [HttpPost("retry")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RetryFailed(CancellationToken ct)
    {
        await _messageService.RetryFailedAsync(ct);
        return Ok(new ApiResponse<string>(true, "Retry triggered"));
    }
}

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    public record LoginRequest(string Username, string Password);
    public record LoginResponse(string Token, string Username);

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var token = _authService.GenerateToken(request.Username, request.Password);
        if (token == null) return Unauthorized(new ApiResponse<string>(false, null, "Invalid credentials"));
        return Ok(new ApiResponse<LoginResponse>(true, new LoginResponse(token, request.Username)));
    }
}

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
}
