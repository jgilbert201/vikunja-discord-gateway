using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VikunjaDiscordGateway;
using VikunjaDiscordGateway.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddHttpClient<DiscordService>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.Configure<Config>(builder.Configuration.GetSection("VikunjaDiscordGateway"));

WebApplication app = builder.Build();
Config config = app.Services.GetRequiredService<IOptions<Config>>().Value;
if (config.Webhooks.Count == 0)
    throw new InvalidOperationException("Configure at least one Discord webhook.");
foreach (var (id, project) in config.Projects)
{
    if (!config.Webhooks.ContainsKey(project.Webhook))
        throw new InvalidOperationException($"Project {id} references missing webhook '{project.Webhook}'.");
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/webhook", async (HttpRequest request, DiscordService discord, ILogger<Program> logger) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    byte[] body = buffer.ToArray();

    if (!string.IsNullOrEmpty(config.WebhookSecret))
    {
        string signature = request.Headers["X-Vikunja-Signature"].ToString();
        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.WebhookSecret), body);
        byte[] provided;
        try { provided = Convert.FromHexString(signature); }
        catch (FormatException) { return Results.Unauthorized(); }
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
            return Results.Unauthorized();
    }

    JsonDocument document;
    try { document = JsonDocument.Parse(body); }
    catch (JsonException) { return Results.BadRequest(new { message = "Invalid JSON" }); }
    using (document)
    {
        JsonElement payload = document.RootElement;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("event_name", out JsonElement eventName) ||
            eventName.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(eventName.GetString()))
            return Results.BadRequest(new { message = "Missing event_name" });

        try
        {
            bool sent = await discord.SendWebhook(payload);
            if (!sent)
                logger.LogWarning("No Discord route for event {Event}", eventName.GetString());
            return sent ? Results.Ok() : Results.UnprocessableEntity(new { message = "No Discord route configured" });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Discord delivery failed for {Event}", eventName.GetString());
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Discord delivery timed out for {Event}", eventName.GetString());
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
});

app.Run();
