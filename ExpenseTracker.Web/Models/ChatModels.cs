namespace ExpenseTracker.Web.Models;

public sealed class ChatMessage
{
    public string Text { get; init; } = string.Empty;
    public bool IsUser { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public sealed class ChatSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Yeni Sohbet";
    public List<ChatMessage> Messages { get; } = [];
    public DateTime CreatedAt { get; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;
}
