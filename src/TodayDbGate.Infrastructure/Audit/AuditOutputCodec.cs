using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Infrastructure.Audit;

public sealed record EncodedOutput(byte[] Blob, int JsonBytes, bool Truncated);

/// <summary>
/// Turns a tool result into a small stored blob: cap rows and bytes, serialise to JSON,
/// compress with Brotli, then encrypt (results may contain personal or business data).
/// </summary>
public sealed class AuditOutputCodec(IDataProtectionProvider provider, AuditOptions options)
{
    private readonly IDataProtector _protector = provider.CreateProtector("TodayDbGate.AuditOutput.v1");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public EncodedOutput Encode(object output)
    {
        var maxBytes = Math.Max(1, options.MaxStoredOutputKb) * 1024;
        byte[] json;
        var truncated = false;

        if (output is QueryResult result)
        {
            var take = Math.Clamp(options.MaxStoredRows, 0, result.Rows.Count);
            truncated = take < result.Rows.Count;
            json = SerializeResult(result, take);

            // Wide rows: halve the stored rows until the entry fits.
            while (json.Length > maxBytes && take > 0)
            {
                take /= 2;
                truncated = true;
                json = SerializeResult(result, take);
            }
        }
        else
        {
            json = JsonSerializer.SerializeToUtf8Bytes(output, Json);
        }

        // Still too big (huge single values, a giant schema): keep the start. The viewer shows it as text.
        if (json.Length > maxBytes)
        {
            json = json[..maxBytes];
            truncated = true;
        }

        return new EncodedOutput(_protector.Protect(Compress(json)), json.Length, truncated);
    }

    public string Decode(byte[] blob) => Encoding.UTF8.GetString(Decompress(_protector.Unprotect(blob)));

    private static byte[] SerializeResult(QueryResult r, int take) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        columns = r.Columns,
        rows = r.Rows.Take(take),
        storedRows = take,
        returnedRows = r.Rows.Count,
        rowLimitReached = r.Truncated,
        recordsAffected = r.RecordsAffected >= 0 ? r.RecordsAffected : (int?)null
    }, Json);

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(data);
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }
}
