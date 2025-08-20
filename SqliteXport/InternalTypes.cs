namespace DB2XL;

internal enum OrderMode
{
    None,
    PrimaryKey,
    Rowid
}

internal sealed record Col(string Name, string Type, bool NotNull, object? DefaultValue, bool IsPrimaryKey);

internal sealed record OrderInfo(OrderMode Mode, IReadOnlyList<string> Columns);

public sealed record TableInfo(string Name, string Type);

internal sealed record MetaRow(
    string TableName,
    string Type,
    int RowCount,
    int ColumnCount,
    int SplitSheets,
    OrderMode OrderMode,
    string ChecksumSha256);