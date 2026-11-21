using System.Text.Json;
using BgInventario.Core.Features.Assistant;
using BgInventario.Core.Features.Products;
using BgInventario.Infrastructure.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace BgInventario.Tests;

public sealed class InventoryAssistantTests : IDisposable
{
    private readonly TestDb _db = new();

    [Fact]
    public async Task Runs_the_tool_the_model_asks_for_and_returns_its_final_answer()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        var products = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);
        await products.CreateAsync(new SaveProductRequest("P1", "Monitor", null,
            [new(null, a.Id, "L1", 300m, 2), new(null, b.Id, "L2", 250m, 4)]), default);

        var model = new ScriptedChatClient(
            ToolCall("compare_supplier_prices", new() { ["productCode"] = "P1" }),
            Text("Proveedor B lo vende más barato, a 250 dólares."));
        var assistant = CreateAssistant(model);

        var result = await assistant.AskAsync([Question("¿Quién vende más barato el monitor?")], default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Proveedor B lo vende más barato, a 250 dólares.", result.Value.Content);

        // The second call carries what the database answered, which is what the model read.
        var toolResult = model.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        var resultJson = JsonSerializer.Serialize(toolResult.Result);
        Assert.Contains(b.Name, resultJson);
        Assert.Contains("250", resultJson);
    }

    [Fact]
    public async Task A_reply_longer_than_the_history_limit_is_cut_so_it_can_be_sent_back()
    {
        var model = new ScriptedChatClient(Text(new string('a', AssistantLimits.MessageMaxLength + 500)));
        var assistant = CreateAssistant(model);

        var result = await assistant.AskAsync([Question("Lista todo")], default);

        Assert.Equal(AssistantLimits.MessageMaxLength, result.Value.Content.Length);
        Assert.EndsWith("…", result.Value.Content);
    }

    [Fact]
    public async Task A_model_that_keeps_calling_tools_is_stopped_at_the_call_limit()
    {
        var model = new ScriptedChatClient(ToolCall("low_stock_products", []));
        var assistant = CreateAssistant(model);

        var result = await assistant.AskAsync([Question("¿Qué debo reponer?")], default);

        Assert.Equal(AssistantErrors.NoAnswer, result.Error);
        Assert.True(model.Requests.Count <= LlmInventoryAssistant.MaxModelCallsPerQuestion);
    }

    [Fact]
    public async Task A_conversation_that_does_not_end_with_a_question_is_rejected_without_calling_the_model()
    {
        var model = new ScriptedChatClient(Text("no debería llamarse"));
        var assistant = CreateAssistant(model);

        AssistantMessage[] conversation = [Question("Hola"), new(AssistantRole.Assistant, "¿En qué te ayudo?")];
        var result = await assistant.AskAsync(conversation, default);

        Assert.Equal(AssistantErrors.LastMessageNotFromUser, result.Error);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task A_provider_failure_is_reported_as_unavailable()
    {
        var model = new ScriptedChatClient { Failure = new HttpRequestException("provider down") };
        var assistant = CreateAssistant(model);

        var result = await assistant.AskAsync([Question("¿Qué debo reponer?")], default);

        Assert.Equal(AssistantErrors.Failed, result.Error);
    }

    private LlmInventoryAssistant CreateAssistant(IChatClient model) => new(
        model, new AssistantOptions(), new InventoryTools(_db.Context), NullLogger<LlmInventoryAssistant>.Instance);

    private static AssistantMessage Question(string text) => new(AssistantRole.User, text);

    private static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private static ChatResponse ToolCall(string name, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), name, arguments)]));

    private sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];
        public Exception? Failure { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            if (Failure is not null)
            {
                throw Failure;
            }

            var index = Math.Min(Requests.Count, responses.Length) - 1;
            return Task.FromResult(responses[index]);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    public void Dispose() => _db.Dispose();
}
