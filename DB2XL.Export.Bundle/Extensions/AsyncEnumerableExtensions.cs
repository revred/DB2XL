namespace DB2XL.Export.Bundle.Extensions;

/// <summary>
/// Extension methods for converting collections to async enumerables.
/// Provides compatibility across different collection types.
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// Converts a list to an async enumerable.
    /// </summary>
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IReadOnlyList<T> list)
    {
        foreach (var item in list)
        {
            yield return item;
        }
        await Task.CompletedTask; // Satisfy async requirement
    }

    /// <summary>
    /// Converts a list to an async enumerable.
    /// </summary>
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this List<T> list)
    {
        foreach (var item in list)
        {
            yield return item;
        }
        await Task.CompletedTask; // Satisfy async requirement
    }

    /// <summary>
    /// Creates an empty async enumerable.
    /// </summary>
    public static async IAsyncEnumerable<T> EmptyAsync<T>()
    {
        yield break;
        #pragma warning disable CS0162 // Unreachable code detected
        await Task.CompletedTask; // Satisfy async requirement
        #pragma warning restore CS0162
    }
}