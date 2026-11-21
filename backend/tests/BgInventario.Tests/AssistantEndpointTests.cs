using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Assistant;
using BgInventario.Core.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BgInventario.Tests;

[Collection(ApiHostCollection.Name)]
public sealed class AssistantEndpointTests : IDisposable
{
    private const string ChatUrl = "/api/assistant/chat";

    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", "Server=unused");
            builder.UseSetting("Jwt:Key", new string('k', 32));
        });

    [Fact]
    public async Task Requires_a_signed_in_user()
    {
        var anonymous = _factory.CreateClient();

        var status = await PostAsync(anonymous, [Question("¿Qué debo reponer?")]);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task A_message_over_the_length_limit_is_rejected()
    {
        var viewer = CreateViewerClient();
        var tooLong = new string('a', AssistantLimits.MessageMaxLength + 1);

        var status = await PostAsync(viewer, [Question(tooLong)]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task A_conversation_over_the_message_limit_is_rejected()
    {
        var viewer = CreateViewerClient();
        var conversation = Enumerable.Range(0, AssistantLimits.MaxMessages + 1)
            .Select(_ => Question("hola"))
            .ToArray();

        var status = await PostAsync(viewer, conversation);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Without_a_model_key_a_valid_question_answers_unavailable()
    {
        var viewer = CreateViewerClient();

        var status = await PostAsync(viewer, [Question("¿Qué debo reponer?")]);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    private static AssistantMessage Question(string text) => new(AssistantRole.User, text);

    private HttpClient CreateViewerClient()
    {
        var viewer = new User { Id = 1, Email = "v@inventario.local", FullName = "Viewer", PasswordHash = "", Role = UserRole.Viewer };
        var token = _factory.Services.GetRequiredService<ITokenService>().CreateToken(viewer);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, AssistantMessage[] conversation)
    {
        using var response = await client.PostAsJsonAsync(ChatUrl, new AssistantRequest(conversation));
        return response.StatusCode;
    }

    public void Dispose() => _factory.Dispose();
}
