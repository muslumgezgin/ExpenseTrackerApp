using System;
using System.Collections.Generic;
using System.Text;

namespace ExpenseTracker.Agent.Interfaces;

public interface IAiOrchestrator
{
    Task<string> ProcessUserRequestAsync(string userMessage, string sessionId = null, CancellationToken cancellationToken = default);
}
