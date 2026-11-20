using BgInventario.Core.Entities;
using Microsoft.AspNetCore.Identity;
using IPasswordHasher = BgInventario.Core.Features.Auth.IPasswordHasher;

namespace BgInventario.Infrastructure.Security;

// nuestra implementación de PasswordHasher 
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(null!, hash, password) is not PasswordVerificationResult.Failed;
}
