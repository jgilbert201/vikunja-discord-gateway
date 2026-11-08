using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VikunjaDiscordGateway.Services;

public class DiscordService(HttpClient httpClient, IOptions<Config> options, ILogger<DiscordService> logger)
{
    private readonly Config _config = options.Value;

    public async Task<bool> SendWebhook(JsonElement payload)
    {
        JsonElement data = Get(payload, "data");
        JsonElement task = Get(data, "task");
        JsonElement project = Get(data, "project");
        int? projectId = Number(task, "project_id") ?? Number(project, "id") ?? Number(data, "project_id");
        ProjectConfig? configuredProject = projectId is int id && _config.Projects.TryGetValue(id, out var match) ? match : null;
        string webhookKey = configuredProject?.Webhook ?? "DEFAULT";
        if (!_config.Webhooks.TryGetValue(webhookKey, out string? webhookUrl))
            return false;

        string eventName = String(payload, "event_name")!;
        string title = eventName.Replace('.', ' ').Replace('_', ' ');
        if (title.Length > 0) title = char.ToUpperInvariant(title[0]) + title[1..];

        string? projectName = configuredProject?.Name;
        if (string.IsNullOrWhiteSpace(projectName)) projectName = String(project, "title") ?? String(project, "name");
        if (string.IsNullOrWhiteSpace(projectName) && projectId.HasValue) projectName = $"Project #{projectId}";

        var lines = new List<string>();
        Add(lines, "Project", projectName);
        int? taskId = Number(task, "id");
        Add(lines, "Task", String(task, "title") ?? (taskId.HasValue ? $"#{taskId}" : null));

        JsonElement doer = Get(data, "doer");
        Add(lines, "By", String(doer, "name") ?? String(doer, "username"));
        JsonElement assignee = Get(data, "assignee");
        Add(lines, "Assignee", String(assignee, "name") ?? String(assignee, "username"));
        JsonElement comment = Get(data, "comment");
        Add(lines, "Comment", String(comment, "comment") ?? String(comment, "text"));
        JsonElement attachment = Get(data, "attachment");
        Add(lines, "Attachment", String(attachment, "file_name") ?? String(attachment, "name"));
        if (task.ValueKind == JsonValueKind.Object && task.TryGetProperty("done", out var done) &&
            done.ValueKind is JsonValueKind.True or JsonValueKind.False)
            Add(lines, "Done", done.GetBoolean() ? "Yes" : "No");

        if (_config.VikunjaUrl is not null && taskId.HasValue)
            lines.Add($"[View task]({new Uri(_config.VikunjaUrl, $"tasks/{taskId}")})");
        else if (_config.VikunjaUrl is not null && projectId.HasValue)
            lines.Add($"[View project]({new Uri(_config.VikunjaUrl, $"projects/{projectId}")})");

        if (lines.Count == 0) lines.Add($"Vikunja event: {eventName}");
        string description = string.Join("\n", lines);
        if (description.Length > 4000) description = description[..3997] + "...";
        var message = new
        {
            embeds = new[] { new { title, description, color = 0x195919 } },
            allowed_mentions = new { parse = Array.Empty<string>() }
        };
        string json = JsonSerializer.Serialize(message);
        if (_config.DryRun)
        {
            logger.LogInformation("Dry run: {Message}", json);
            return true;
        }

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(webhookUrl, content);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Discord returned HTTP {(int)response.StatusCode}");
        return true;
    }

    private static JsonElement Get(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value) ? value : default;

    private static string? String(JsonElement obj, string key)
    {
        var value = Get(obj, key);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? Number(JsonElement obj, string key)
    {
        var value = Get(obj, key);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
    }

    private static void Add(List<string> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        if (value.Length > 500) value = value[..497] + "...";
        lines.Add($"**{label}:** {value}");
    }
}
