using System.Net.Http.Json;
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
                return "İsteğiniz şu an işlenemiyor, lütfen tekrar deneyin.";
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.SessionId))
                _sessionId = payload.SessionId;

            return payload?.Response ?? "Üzgünüm, bir yanıt oluşturulamadı.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat API call failed.");
            return "Bir hata oluştu, lütfen tekrar deneyin.";
        }
    }

    private  record ChatRequest(string Message, string SessionId);
    private  record ChatResponse(string Response, string SessionId);
}
