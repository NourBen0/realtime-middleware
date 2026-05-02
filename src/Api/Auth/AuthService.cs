using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace RealtimeMiddleware.Api.Auth;

public interface IAuthService
{
    string? GenerateToken(string username, string password);
    bool ValidateCredentials(string username, string password);
}

public class AuthService : IAuthService
{
    private readonly IConfiguration _config;

    private static readonly Dictionary<string, string> _users = new()
    {
        { "admin", "admin123" },
        { "client", "client123" }
    };

    public AuthService(IConfiguration config)
    {
        _config = config;
    }

    public bool ValidateCredentials(string username, string password)
        => _users.TryGetValue(username, out var stored) && stored == password;

    public string? GenerateToken(string username, string password)
    {
        if (!ValidateCredentials(username, password)) return null;

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "super-secret-dev-key-32-chars!!"));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, username == "admin" ? "Admin" : "Client"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "RealtimeMiddleware",
            audience: _config["Jwt:Audience"] ?? "RealtimeMiddlewareClients",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
