using DB2XL.Data.Query;
using DB2XL.Data.Schema;
using DB2XL.Core.Enums;

namespace DB2XL;

/// <summary>
/// Metadata row for Excel export metadata sheet
/// </summary>
internal sealed record MetaRow(
    string TableName,
    string Type,
    int RowCount,
    int ColumnCount,
    int SplitSheets,
    OrderMode OrderMode,
    string ChecksumSha256);