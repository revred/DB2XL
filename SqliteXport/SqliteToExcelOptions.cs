using System.Globalization;

namespace DB2XL;

public enum BlobRenderMode
{
    Skip,
    Hex,
    Base64
}

public sealed class SqliteToExcelOptions
{
    public bool WriteAllAsText { get; init; } = true;
    public bool PreserveNumericTypes { get; init; } = false;
    public bool IncludeMetadataSheet { get; init; } = true;
    public string MetadataSheetName { get; init; } = "_Export_Metadata";
    public int ReadBatchSize { get; init; } = 25_000;
    public int CommandTimeoutSeconds { get; init; } = 180;
    public string? TableNameLikeFilter { get; init; } = null;
    public bool IncludeViews { get; init; } = false;
    public BlobRenderMode BlobMode { get; init; } = BlobRenderMode.Hex;
    public bool OrderRowsDeterministically { get; init; } = true;
    public bool SplitOversizeSheets { get; init; } = true;
    public CultureInfo InvariantCulture { get; init; } = CultureInfo.InvariantCulture;
}