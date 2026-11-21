using System.Security.Claims;
using BgInventario.Api.Setup;
using BgInventario.Core.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BgInventario.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login)
            .AllowAnonymous()
            .RequireRateLimiting(ServiceCollectionExtensions.LoginRateLimit)
            .WithSummary("Valida credenciales y devuelve un JWT")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/my-profile", MyProfile)
            .WithSummary("Usuario autenticado actual")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> Login(
        LoginRequest request, AuthService auth, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<UserSummary>, ProblemHttpResult>> MyProfile(
        ClaimsPrincipal user, AuthService auth, CancellationToken ct)
    {
        var userId = int.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var result = await auth.GetUserAsync(userId, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }
}
