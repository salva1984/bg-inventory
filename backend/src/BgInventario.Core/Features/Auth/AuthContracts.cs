using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Entities;

namespace BgInventario.Core.Features.Auth;

public sealed record LoginRequest(
    [property: Required, EmailAddress, StringLength(UserLimits.EmailMaxLength)] string Email,
    [property: Required, StringLength(UserLimits.PasswordMaxLength)] string Password);

public sealed record UserSummary(int Id, string Email, string FullName, UserRole Role);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserSummary User);

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateToken(User user);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
