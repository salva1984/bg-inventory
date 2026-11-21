using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BgInventario.Api.Setup;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BgInventario.Tests;

/// <summary>
/// Rate limits over the real HTTP pipeline of Program.cs (WebApplicationFactory). They check the
/// middleware order: the invoice-analysis limit must count per signed-in user, which only works
/// when UseRateLimiter runs after UseAuthentication.
///
/// The requests are deliberately invalid (no file, malformed login) so they end in a 400 at model
/// binding or validation without touching the database or the model. The limiter runs before the
/// endpoint, so each one still spends a permit; the tests only look at whether the answer is 429.
/// </summary>
[Collection(ApiHostCollection.Name)]
public sealed class RateLimitTests : IDisposable
{
    private const string AnalyzeUrl = "/api/invoices/analyze";
    private const string LoginUrl = "/api/auth/login";

    // A new factory per test (xUnit creates one instance per test): each test starts with empty
    // rate-limit windows.
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            // Not "Development": that environment migrates and seeds a SQL Server at startup.
            builder.UseEnvironment("Testing");
            // Required by startup; never used, since no request reaches the database.
            builder.UseSetting("ConnectionStrings:Default", "Server=unused");
            builder.UseSetting("Jwt:Key", new string('k', 32));
        });

    [Fact]
    public async Task Invoice_analysis_limit_is_counted_per_user()
    {
        var adminA = CreateClient(Admin(id: 1, "a@inventario.local"));
        var adminB = CreateClient(Admin(id: 2, "b@inventario.local"));

        // Admin A uses the whole quota and then is rejected.
        for (var attempt = 0; attempt < ServiceCollectionExtensions.InvoiceAnalysesPerWindow; attempt++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await PostAnalyzeAsync(adminA));
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostAnalyzeAsync(adminA));

        // Admin B has their own counter: A's quota does not block them.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await PostAnalyzeAsync(adminB));
    }

    [Fact]
    public async Task Unauthenticated_requests_do_not_spend_the_invoice_analysis_quota()
    {
        var anonymous = _factory.CreateClient();
        var admin = CreateClient(Admin(id: 1, "a@inventario.local"));

        // More anonymous calls than the quota: all are rejected by auth, none by the limiter.
        for (var attempt = 0; attempt <= ServiceCollectionExtensions.InvoiceAnalysesPerWindow; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await PostAnalyzeAsync(anonymous));
        }

        // The admin still has the full quota.
        for (var attempt = 0; attempt < ServiceCollectionExtensions.InvoiceAnalysesPerWindow; attempt++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await PostAnalyzeAsync(admin));
        }
    }

    [Fact]
    public async Task Login_limit_rejects_attempts_beyond_the_window()
    {
        var client = _factory.CreateClient();

        for (var attempt = 0; attempt < ServiceCollectionExtensions.LoginAttemptsPerWindow; attempt++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await PostLoginAsync(client));
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostLoginAsync(client));
    }

    private static User Admin(int id, string email) =>
        new() { Id = id, Email = email, FullName = email, PasswordHash = "", Role = UserRole.Admin };

    /// <summary>Client that sends a real token for the user, issued by the app's own token service.</summary>
    private HttpClient CreateClient(User user)
    {
        var token = _factory.Services.GetRequiredService<ITokenService>().CreateToken(user);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    /// <summary>Multipart form without the file: binding answers 400 before the endpoint runs.</summary>
    private static async Task<HttpStatusCode> PostAnalyzeAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent { { new StringContent("1"), "supplierId" } };
        using var response = await client.PostAsync(AnalyzeUrl, form);
        return response.StatusCode;
    }

    /// <summary>Malformed e-mail: validation answers 400 before looking up the user.</summary>
    private static async Task<HttpStatusCode> PostLoginAsync(HttpClient client)
    {
        var request = new LoginRequest("not-an-email", "whatever");
        using var response = await client.PostAsJsonAsync(LoginUrl, request);
        return response.StatusCode;
    }

    public void Dispose() => _factory.Dispose();
}
