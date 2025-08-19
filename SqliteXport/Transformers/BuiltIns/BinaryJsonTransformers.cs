using System.Globalization;
using System.Text.Json;
using System.IO.Compression;
using System.Text;

namespace DB2XL.Transformers.BuiltIns;

/// <summary>
/// Decodes binary JSON formats (base64, hex, compressed) into readable JSON text
/// </summary>
public class BinaryJsonDecodeTransformer : CellTransformerBase
{
    public BinaryJsonDecodeTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        // Primarily for BLOB columns that might contain encoded JSON
        var columnName = ctx.Column.ToLowerInvariant();
        var isBinaryJsonColumn = columnName.Contains("json") || 
                               columnName.Contains("bson") ||
                               columnName.Contains("msgpack") ||
                               columnName.Contains("data") ||
                               columnName.Contains("payload") ||
                               columnName.Contains("content");
        
        return ctx.Affinity == SqliteAffinity.Blob && 
               (isBinaryJsonColumn || GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var encoding = GetConfig("encoding", "auto").ToLowerInvariant();
        var format = GetConfig("format", "auto").ToLowerInvariant(); // auto, compact, pretty
        var compression = GetConfig("compression", "auto").ToLowerInvariant(); // auto, gzip, deflate, none

        try
        {
            // First, decode the binary format
            var jsonBytes = DecodeToBytes(raw, encoding);
            if (jsonBytes == null) return raw;

            // Then, decompress if needed
            var decompressedBytes = DecompressIfNeeded(jsonBytes, compression);
            
            // Convert to string
            var jsonString = Encoding.UTF8.GetString(decompressedBytes);
            
            // Validate and optionally reformat
            return FormatJson(jsonString, format);
        }
        catch (Exception ex)
        {
            throw new TransformerException("binary-json-decode", ctx, $"Failed to decode binary JSON '{raw}': {ex.Message}", ex);
        }
    }

    private byte[]? DecodeToBytes(string raw, string encoding)
    {
        return encoding switch
        {
            "base64" => Convert.FromBase64String(raw),
            "hex" => Convert.FromHexString(raw),
            "auto" => AutoDetectAndDecodeToBytes(raw),
            "none" => Encoding.UTF8.GetBytes(raw),
            _ => null
        };
    }

    private byte[]? AutoDetectAndDecodeToBytes(string raw)
    {
        // Try base64 first
        if (IsBase64(raw))
        {
            try
            {
                return Convert.FromBase64String(raw);
            }
            catch { }
        }

        // Try hex
        if (IsHex(raw))
        {
            try
            {
                return Convert.FromHexString(raw);
            }
            catch { }
        }

        // Fall back to treating as UTF8 text
        return Encoding.UTF8.GetBytes(raw);
    }

    private byte[] DecompressIfNeeded(byte[] data, string compression)
    {
        if (compression == "none")
            return data;

        // Try to auto-detect compression
        if (compression == "auto")
        {
            // Check for gzip magic bytes
            if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
                compression = "gzip";
            // Check for deflate (less reliable detection)
            else if (data.Length >= 2 && data[0] == 0x78)
                compression = "deflate";
            else
                return data; // No compression detected
        }

        try
        {
            using var input = new MemoryStream(data);
            using var output = new MemoryStream();
            
            Stream decompressionStream = compression switch
            {
                "gzip" => new GZipStream(input, CompressionMode.Decompress),
                "deflate" => new DeflateStream(input, CompressionMode.Decompress),
                _ => throw new NotSupportedException($"Compression type '{compression}' not supported")
            };

            using (decompressionStream)
            {
                decompressionStream.CopyTo(output);
            }
            
            return output.ToArray();
        }
        catch
        {
            // Decompression failed, return original data
            return data;
        }
    }

    private string FormatJson(string jsonString, string format)
    {
        // Validate JSON first
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            
            return format switch
            {
                "compact" => JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false }),
                "pretty" => JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }),
                "auto" => jsonString, // Return as-is if valid
                _ => jsonString
            };
        }
        catch (JsonException)
        {
            // Not valid JSON, but maybe it's other structured data
            return jsonString;
        }
    }

    private bool IsBase64(string str)
    {
        return !string.IsNullOrEmpty(str) && 
               str.Length % 4 == 0 && 
               System.Text.RegularExpressions.Regex.IsMatch(str, @"^[A-Za-z0-9+/]*={0,2}$");
    }

    private bool IsHex(string str)
    {
        return !string.IsNullOrEmpty(str) && 
               str.Length % 2 == 0 && 
               System.Text.RegularExpressions.Regex.IsMatch(str, @"^[0-9A-Fa-f]+$");
    }
}

/// <summary>
/// Encodes JSON text into various binary formats (base64, hex, compressed)
/// </summary>
public class BinaryJsonEncodeTransformer : CellTransformerBase
{
    public BinaryJsonEncodeTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        // Works with TEXT columns containing JSON
        var columnName = ctx.Column.ToLowerInvariant();
        var isJsonColumn = columnName.Contains("json") || 
                          columnName.Contains("data") ||
                          columnName.Contains("config");
        
        return ctx.Affinity == SqliteAffinity.Text && 
               (isJsonColumn || GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var encoding = GetConfig("encoding", "base64").ToLowerInvariant(); // base64, hex
        var compression = GetConfig("compression", "none").ToLowerInvariant(); // none, gzip, deflate
        var compact = GetConfigBool("compact", true);

        try
        {
            // First, validate and optionally compact the JSON
            var jsonString = compact ? CompactJson(raw) : raw;
            
            // Convert to bytes
            var jsonBytes = Encoding.UTF8.GetBytes(jsonString);
            
            // Compress if requested
            var finalBytes = CompressIfNeeded(jsonBytes, compression);
            
            // Encode to target format
            return encoding switch
            {
                "base64" => Convert.ToBase64String(finalBytes),
                "hex" => Convert.ToHexString(finalBytes).ToLowerInvariant(),
                _ => raw // Unknown encoding
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("binary-json-encode", ctx, $"Failed to encode JSON '{raw}': {ex.Message}", ex);
        }
    }

    private string CompactJson(string jsonString)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            // Not valid JSON, return original
            return jsonString;
        }
    }

    private byte[] CompressIfNeeded(byte[] data, string compression)
    {
        if (compression == "none")
            return data;

        try
        {
            using var output = new MemoryStream();
            
            Stream compressionStream = compression switch
            {
                "gzip" => new GZipStream(output, CompressionLevel.Optimal),
                "deflate" => new DeflateStream(output, CompressionLevel.Optimal),
                _ => throw new NotSupportedException($"Compression type '{compression}' not supported")
            };

            using (compressionStream)
            {
                compressionStream.Write(data);
            }
            
            return output.ToArray();
        }
        catch
        {
            // Compression failed, return original
            return data;
        }
    }
}