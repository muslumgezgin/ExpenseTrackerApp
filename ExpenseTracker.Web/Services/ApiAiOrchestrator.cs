using ExpenseTracker.Agent.Interfaces;

namespace ExpenseTracker.Web.Services;

public sealed class ApiAiOrchestrator(IHttpClientFactory httpClientFactory, ILogger<ApiAiOrchestrator> logger) : IAiOrchestrator
{
    private string _sessionId = Guid.NewGuid().ToString("N");

    public async Task<string> ProcessUserRequestAsync(string userMessage, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Api");
            var activeSessionId = string.IsNullOrWhiteSpace(sessionId) ? _sessionId : sessionId;
            var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(userMessage, activeSessionId), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Chat API returned {StatusCode}.", response.StatusCode);
                return "Your request cannot be processed right now, please try again.";
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.SessionId))
                _sessionId = payload.SessionId;

            return payload?.Response ?? "Sorry, a response could not be generated.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat API call failed.");
            return "An error occurred, please try again.";
        }
    }

    private  record ChatRequest(string Message, string SessionId);
    private  record ChatResponse(string Response, string SessionId);
}
