using System.Text;
using System.Text.Json;

namespace AIManager.Api.Services.Jira;

/// <summary>Extracts readable plain text from an Atlassian Document Format (ADF) comment body.</summary>
public static class Adf
{
    public static string ToPlainText(JsonElement body)
    {
        // Some APIs may return a plain string body instead of ADF.
        if (body.ValueKind == JsonValueKind.String)
            return body.GetString() ?? "";

        var sb = new StringBuilder();
        Walk(body, sb);
        return sb.ToString().Trim();
    }

    private static void Walk(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "text" && node.TryGetProperty("text", out var txt))
                sb.Append(txt.GetString());

            if (type == "hardBreak")
                sb.Append('\n');

            if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var child in content.EnumerateArray())
                    Walk(child, sb);

            // Block-level nodes end with a newline for readability.
            if (type is "paragraph" or "heading" or "listItem" or "blockquote" or "codeBlock")
                sb.Append('\n');
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                Walk(child, sb);
        }
    }
}
