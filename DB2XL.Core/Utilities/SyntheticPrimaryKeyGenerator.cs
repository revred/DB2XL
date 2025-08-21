using System.Security.Cryptography;
using System.Text;

namespace DB2XL.Core.Utilities;

/// <summary>
/// Utilities for generating synthetic primary keys
/// </summary>
public static class SyntheticPrimaryKeyGenerator
{
    /// <summary>
    /// Generates a deterministic hash from row values for synthetic primary key
    /// </summary>
    public static string GenerateRowHash(IReadOnlyList<object?> columnValues)
    {
        using var sha256 = SHA256.Create();
        var combined = new StringBuilder();
        
        for (int i = 0; i < columnValues.Count; i++)
        {
            if (i > 0)
            {
                combined.Append('\x1F');
            }
            
            var value = columnValues[i];
            if (value == null)
            {
                combined.Append('\x00');
            }
            else
            {
                combined.Append(value.ToString());
            }
        }
        
        var bytes = Encoding.UTF8.GetBytes(combined.ToString());
        var hash = sha256.ComputeHash(bytes);
        
        return Convert.ToHexString(hash);
    }
}