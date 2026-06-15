using ExpenseTracker.Api.Api.Endpoints;
using ExpenseTracker.Api.Application.Interfaces;
using ExpenseTracker.Api.Application.Services;
using ExpenseTracker.Api.Infrastructure.Data;
using ExpenseTracker.Api.Infrastructure.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<ExpenseDbContext>(options =>
{
    options.UseInMemoryDatabase("InMemoryDb");
});

#if DEBUG
builder.Services.AddCors(options =>
{
    options.AddPolicy("McpInspector", policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("Mcp-Session-Id"));
});
#endif

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .AddMcpServer()
    .WithToolsFromAssembly()
    .WithHttpTransport();

builder.Services.AddHttpClient();
builder.Services.AddScoped<IExpenseApplicationService, ExpenseApplicationService>();


var app = builder.Build();

#if DEBUG
app.UseCors("McpInspector");
#endif

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await DataSeeder.SeedAsync(app.Services);
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.MapExpenseEndpoints();

app.MapMcp("/mcp");

app.Run();
