using ExpenseTracker.Web.Models;

namespace ExpenseTracker.Web.Services;

public sealed class ChatSessionService
{
    public List<ChatSession> Sessions { get; } = [];
    public ChatSession ActiveSession { get; private set; }

    public ChatSessionService()
    {
        ActiveSession = CreateNewSession();
    }

    public ChatSession CreateNewSession()
    {
        var session = new ChatSession();
        Sessions.Insert(0, session);
        ActiveSession = session;
        return session;
    }

    public void SwitchSession(string id)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == id);
        if (session is not null)
            ActiveSession = session;
    }

    public void AddMessage(string text, bool isUser)
    {
        ActiveSession.Messages.Add(new ChatMessage { Text = text, IsUser = isUser });
        ActiveSession.LastActivity = DateTime.Now;
        if (isUser && ActiveSession.Name == "New Chat" && !string.IsNullOrWhiteSpace(text))
            ActiveSession.Name = text.Length > 28 ? text[..28] + "…" : text;
    }
}
