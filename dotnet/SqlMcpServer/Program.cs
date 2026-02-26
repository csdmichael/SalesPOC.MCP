var builder = WebApplication.CreateBuilder(args);

// Add the MCP services: the transport to use (http) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<SqlTools>();

var app = builder.Build();

app.MapGet("/.well-known/mcp.json", (HttpRequest request) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";

    return Results.Json(new
    {
        servers = new
        {
            SqlMcpServer = new
            {
                type = "http",
                url = baseUrl
            }
        }
    });
});

app.MapGet("/mcp.json", (HttpRequest request) =>
    Results.Redirect($"{request.Scheme}://{request.Host}/.well-known/mcp.json", permanent: false));

app.MapMcp();

app.Run();
