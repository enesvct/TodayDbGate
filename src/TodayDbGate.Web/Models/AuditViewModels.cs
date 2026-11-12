using System.Text.Json;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Web.Models;

public sealed record AuditIndexModel(AuditFilter Filter, AuditPage Page, AuditStats Stats)
{
    /// <summary>Query string values for a link to another page with the same filter.</summary>
    public Dictionary<string, string> RouteFor(int page)
    {
        var values = new Dictionary<string, string> { ["page"] = page.ToString() };
        if (!string.IsNullOrWhiteSpace(Filter.Token)) values["token"] = Filter.Token;
        if (!string.IsNullOrWhiteSpace(Filter.Alias)) values["alias"] = Filter.Alias;
        if (Filter.Status is { } s) values["status"] = s.ToString();
        if (!string.IsNullOrWhiteSpace(Filter.Search)) values["search"] = Filter.Search;
        return values;
    }
}

public sealed record AuditCell(string Text, bool IsNull);

public sealed class AuditDetailsModel
{
    public required AuditLogDetail Detail { get; init; }

    // Set when the output is a query result.
    public IReadOnlyList<string>? Columns { get; private init; }
    public IReadOnlyList<IReadOnlyList<AuditCell>>? Rows { get; private init; }
    public int? StoredRows { get; private init; }
    public int? ReturnedRows { get; private init; }
    public bool RowLimitReached { get; private init; }
    public IReadOnlyList<string> MaskedColumns { get; private init; } = [];

    /// <summary>Set for any other output (schema, connection list) or when the JSON was cut short.</summary>
    public string? RawOutput { get; private init; }

    public static AuditDetailsModel From(AuditLogDetail detail)
    {
        if (detail.OutputJson is null) return new AuditDetailsModel { Detail = detail };

        try
        {
            using var doc = JsonDocument.Parse(detail.OutputJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("columns", out var columns)
                && root.TryGetProperty("rows", out var rows))
            {
                return new AuditDetailsModel
                {
                    Detail = detail,
                    Columns = columns.EnumerateArray().Select(c => c.GetString() ?? "").ToList(),
                    Rows = rows.EnumerateArray().Select(r => (IReadOnlyList<AuditCell>)r.EnumerateArray().Select(ToCell).ToList()).ToList(),
                    StoredRows = root.TryGetProperty("storedRows", out var s) ? s.GetInt32() : null,
                    ReturnedRows = root.TryGetProperty("returnedRows", out var r2) ? r2.GetInt32() : null,
                    RowLimitReached = root.TryGetProperty("rowLimitReached", out var l) && l.GetBoolean(),
                    MaskedColumns = root.TryGetProperty("maskedColumns", out var m) && m.ValueKind == JsonValueKind.Array
                        ? m.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                        : []
                };
            }

            return new AuditDetailsModel
            {
                Detail = detail,
                RawOutput = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (JsonException)
        {
            // Cut at the size limit, so not valid JSON any more: show the text as stored.
            return new AuditDetailsModel { Detail = detail, RawOutput = detail.OutputJson };
        }
    }

    private static AuditCell ToCell(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => new AuditCell("NULL", true),
        JsonValueKind.String => new AuditCell(e.GetString() ?? "", false),
        _ => new AuditCell(e.GetRawText(), false)
    };
}
