using ExpenseTracker.Web.Components;
using ExpenseTracker.Web.Services;
using ExpenseTracker.Agent.Interfaces;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]);

#if !DEBUG
    client.Timeout = TimeSpan.FromSeconds(10);
#endif

    var apiKey = builder.Configuration["ApiAuth:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

builder.Services.AddScoped<IAiOrchestrator, ApiAiOrchestrator>();
builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<ExpenseApiService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
