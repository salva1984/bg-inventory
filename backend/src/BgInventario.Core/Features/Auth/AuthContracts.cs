// Request/response shapes of the login flow and the security ports implemented in Infrastructure.
using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Entities;

namespace BgInventario.Core.Features.Auth;

public sealed record LoginRequest(
    [property: Required, EmailAddress, StringLength(UserLimits.EmailMaxLength)] string Email,
    [property: Required, StringLength(UserLimits.PasswordMaxLength)] string Password);

public sealed record UserSummary(int Id, string Email, string FullName, UserRole Role);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserSummary User);

public sealed record AccessToken(string Token, DateTime ExpiresAt);

/// <summary>Issues access tokens (JWT in Infrastructure). Keeps Core free of token libraries.</summary>
public interface ITokenService
{
    AccessToken CreateToken(User user);
}

/// <summary>Hashes and verifies passwords (PBKDF2 in Infrastructure).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
