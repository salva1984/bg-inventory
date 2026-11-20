using BgInventario.Core.Entities;
using BgInventario.Core.Features.Auth;
using BgInventario.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BgInventario.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var hasher = new Pbkdf2PasswordHasher();
        _db.Context.Users.Add(new User
        {
            Email = "admin@inventario.local",
            FullName = "Admin",
            PasswordHash = hasher.Hash("Secret123!"),
            Role = UserRole.Admin
        });
        _db.Context.SaveChanges();

        var jwt = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            Key = new string('k', 32)
        });
        _service = new AuthService(_db.Context, hasher, new JwtTokenService(jwt, TimeProvider.System), NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_token()
    {
        var result = await _service.LoginAsync(new LoginRequest(" Admin@Inventario.local ", "Secret123!"), default);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.AccessToken));
        Assert.Equal(UserRole.Admin, result.Value.User.Role);
    }

    [Theory]
    [InlineData("admin@inventario.local", "wrong")]
    [InlineData("nobody@inventario.local", "Secret123!")]
    public async Task Login_with_invalid_credentials_returns_same_error(string email, string password)
    {
        var result = await _service.LoginAsync(new LoginRequest(email, password), default);

        Assert.Equal("auth.invalid_credentials", result.Error?.Code);
    }

    public void Dispose() => _db.Dispose();
}
