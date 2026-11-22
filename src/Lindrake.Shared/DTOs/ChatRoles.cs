// ============================================================
// Lindrake.Shared — Conversation role wire strings
// ============================================================

namespace Lindrake.Shared.DTOs;

/// <summary>
/// The values of <see cref="ConversationMessage.Role"/> and <see cref="ReportTurn.Role"/>.
/// The API validates <see cref="ConversationMessage.Role"/> case-sensitively; the agent's
/// chat history and the report renderers read both roles in any case.
/// </summary>
public static class ChatRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}
