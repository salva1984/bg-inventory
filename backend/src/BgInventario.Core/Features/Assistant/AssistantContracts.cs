using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Common;

namespace BgInventario.Core.Features.Assistant;

public interface IInventoryAssistant
{
    Task<Result<AssistantReply>> AskAsync(IReadOnlyList<AssistantMessage> conversation, CancellationToken ct);
}

public enum AssistantRole
{
    User,
    Assistant
}

public static class AssistantLimits
{
    // Every request carries the whole conversation: these bound the tokens one question can spend.
    public const int MaxMessages = 20;
    public const int MessageMaxLength = 4000;
}

public sealed record AssistantRequest(
    [property: Required, MinLength(1), MaxLength(AssistantLimits.MaxMessages)] IReadOnlyList<AssistantMessage> Messages);

public sealed record AssistantMessage(
    AssistantRole Role,
    [property: Required, StringLength(AssistantLimits.MessageMaxLength)] string Content);

public sealed record AssistantReply(string Content);

public static class AssistantErrors
{
    public static readonly ErrorDetail LastMessageNotFromUser = ErrorDetail.Validation("assistant.last_message_not_from_user", "La conversación debe terminar con una pregunta del usuario.");
    public static readonly ErrorDetail NotConfigured = ErrorDetail.Unavailable("assistant.not_configured", "El asistente no está configurado en el servidor.");
    public static readonly ErrorDetail Failed = ErrorDetail.Unavailable("assistant.failed", "El asistente no respondió. Intente nuevamente en unos minutos.");
    public static readonly ErrorDetail NoAnswer = ErrorDetail.Unavailable("assistant.no_answer", "El asistente no llegó a una respuesta. Intente con una pregunta más concreta.");
}
