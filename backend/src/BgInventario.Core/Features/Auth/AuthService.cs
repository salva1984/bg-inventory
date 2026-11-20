using BgInventario.Core.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BgInventario.Core.Features.Auth;

/// <summary>Login by email and password, and lookup of the signed-in user.</summary>
public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ILogger<AuthService> logger)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Correo o contraseña incorrectos.");

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        // Emails are stored lowercase, so normalizing here makes the lookup case-insensitive.
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

        // mismo error para evitar enumeracion de usuarios
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            logger.LogWarning("Failed login attempt for {Email}", email);
            return InvalidCredentials;
        }

        var token = tokenService.CreateToken(user);
        logger.LogInformation("User {Email} logged in", user.Email);

        return new LoginResponse(token.Token, token.ExpiresAt, ToUserSummary(user));
    }

    public async Task<Result<UserSummary>> GetUserAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(u.Id, u.Email, u.FullName, u.Role))
            .FirstOrDefaultAsync(ct);

        return user is null ? Error.NotFound("auth.user_not_found", "Usuario no encontrado.") : user;
    }

    private static UserSummary ToUserSummary(Entities.User u) => new(u.Id, u.Email, u.FullName, u.Role);
}
