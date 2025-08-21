using DB2XL.Core.Models;
using System.Text;

namespace DB2XL.Core.Services;

/// <summary>
/// Interface for managing bundle directory structures and file paths.
/// Ensures consistent, sanitized, and relative path handling across bundle operations.
/// </summary>
public interface IBundlePathManager
{
    /// <summary>
    /// Creates a complete bundle layout from the provided options.
    /// Generates timestamp-based directory structure when root path is not specified.
    /// </summary>
    /// <param name="options">Bundle export configuration options</param>
    /// <returns>Complete bundle layout with absolute paths</returns>
    BundleLayout CreateBundleLayout(BundleExportOptions options);

    /// <summary>
    /// Generates the full file path for a table partition.
    /// </summary>
    /// <param name="layout">Bundle layout containing directory structure</param>
    /// <param name="tableName">Name of the database table</param>
    /// <param name="partitionLabel">Human-readable partition identifier</param>
    /// <param name="extension">File extension (with or without dot)</param>
    /// <returns>Absolute path to the partition file</returns>
    string GetPartitionFilePath(BundleLayout layout, string tableName, string partitionLabel, string extension);

    /// <summary>
    /// Generates the full file path for a table sample file.
    /// </summary>
    /// <param name="layout">Bundle layout containing directory structure</param>
    /// <param name="tableName">Name of the database table</param>
    /// <returns>Absolute path to the sample file</returns>
    string GetSampleFilePath(BundleLayout layout, string tableName);

    /// <summary>
    /// Converts an absolute path to a relative path from the bundle root.
    /// Ensures portable bundle structures that work across different systems.
    /// </summary>
    /// <param name="bundleRoot">Absolute path to the bundle root directory</param>
    /// <param name="absolutePath">Absolute path to convert</param>
    /// <returns>Relative path from bundle root, using forward slashes</returns>
    string ToRelativePath(string bundleRoot, string absolutePath);

    /// <summary>
    /// Creates the complete directory structure for a bundle.
    /// Safe to call multiple times - will not fail if directories already exist.
    /// </summary>
    /// <param name="layout">Bundle layout containing directory paths</param>
    void EnsureDirectoryStructure(BundleLayout layout);

    /// <summary>
    /// Gets the manifest file path for a specific manifest type.
    /// </summary>
    /// <param name="layout">Bundle layout containing manifest directory</param>
    /// <param name="manifestName">Name of the manifest file (e.g., "schema.json")</param>
    /// <returns>Absolute path to the manifest file</returns>
    string GetManifestFilePath(BundleLayout layout, string manifestName);
}

/// <summary>
/// Production implementation of bundle path management.
/// Handles directory creation, path sanitization, and relative path conversion.
/// </summary>
public sealed class BundlePathManager : IBundlePathManager
{
    /// <summary>
    /// Creates a complete bundle layout from the provided options.
    /// Uses deterministic timestamps for testing or UTC now for production.
    /// </summary>
    public BundleLayout CreateBundleLayout(BundleExportOptions options)
    {
        var timestamp = options.DeterministicTimestamps 
            ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) // For testing
            : DateTime.UtcNow;

        var rootPath = string.IsNullOrEmpty(options.BundleRootPath)
            ? Path.Combine(Path.GetTempPath(), $"export_run_{timestamp:yyyy-MM-ddTHH-mm-ssZ}")
            : Path.GetFullPath(options.BundleRootPath); // Ensure absolute path

        return new BundleLayout
        {
            RootPath = rootPath,
            IndexWorkbookPath = Path.Combine(rootPath, options.IndexWorkbookName),
            ManifestPath = Path.Combine(rootPath, options.ManifestDirectoryName),
            TablesPath = Path.Combine(rootPath, options.TablesDirectoryName),
            ExportTimestamp = timestamp
        };
    }

    /// <summary>
    /// Generates partition file path with sanitized table and partition names.
    /// </summary>
    public string GetPartitionFilePath(BundleLayout layout, string tableName, string partitionLabel, string extension)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));

        if (string.IsNullOrWhiteSpace(partitionLabel))
            throw new ArgumentException("Partition label cannot be null or empty", nameof(partitionLabel));

        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension cannot be null or empty", nameof(extension));

        var tableDir = layout.GetTableDirectory(tableName);
        var cleanExtension = extension.TrimStart('.');
        var fileName = $"{SanitizeFileName(tableName)}_{SanitizeFileName(partitionLabel)}.{cleanExtension}";
        
        return Path.Combine(tableDir, fileName);
    }

    /// <summary>
    /// Generates sample file path with consistent naming convention.
    /// </summary>
    public string GetSampleFilePath(BundleLayout layout, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));

        var tableDir = layout.GetTableDirectory(tableName);
        var fileName = $"sample_{SanitizeFileName(tableName)}_head_10k.jsonl";
        
        return Path.Combine(tableDir, fileName);
    }

    /// <summary>
    /// Converts absolute paths to portable relative paths using forward slashes.
    /// </summary>
    public string ToRelativePath(string bundleRoot, string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot))
            throw new ArgumentException("Bundle root cannot be null or empty", nameof(bundleRoot));

        if (string.IsNullOrWhiteSpace(absolutePath))
            throw new ArgumentException("Absolute path cannot be null or empty", nameof(absolutePath));

        try
        {
            var rootUri = new Uri(EnsureTrailingSlash(Path.GetFullPath(bundleRoot)));
            var absoluteUri = new Uri(Path.GetFullPath(absolutePath));
            
            if (!absoluteUri.ToString().StartsWith(rootUri.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // Path is outside bundle root, return as-is but warn
                return Path.GetFileName(absolutePath);
            }
            
            var relative = rootUri.MakeRelativeUri(absoluteUri);
            return Uri.UnescapeDataString(relative.ToString()).Replace('\\', '/');
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create relative path from '{bundleRoot}' to '{absolutePath}'", ex);
        }
    }

    /// <summary>
    /// Creates all required directories for the bundle structure.
    /// </summary>
    public void EnsureDirectoryStructure(BundleLayout layout)
    {
        if (layout == null)
            throw new ArgumentNullException(nameof(layout));

        try
        {
            // Create directories in dependency order
            Directory.CreateDirectory(layout.RootPath);
            Directory.CreateDirectory(layout.ManifestPath);
            Directory.CreateDirectory(layout.TablesPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create bundle directory structure at '{layout.RootPath}'", ex);
        }
    }

    /// <summary>
    /// Gets the full path to a specific manifest file.
    /// </summary>
    public string GetManifestFilePath(BundleLayout layout, string manifestName)
    {
        if (layout == null)
            throw new ArgumentNullException(nameof(layout));

        if (string.IsNullOrWhiteSpace(manifestName))
            throw new ArgumentException("Manifest name cannot be null or empty", nameof(manifestName));

        return Path.Combine(layout.ManifestPath, manifestName);
    }

    /// <summary>
    /// Sanitizes a filename by replacing invalid characters with underscores.
    /// Ensures compatibility across different file systems.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "_empty_";

        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new StringBuilder(fileName.Length);

        foreach (char c in fileName)
        {
            if (invalidChars.Contains(c) || c == ' ')
            {
                result.Append('_');
            }
            else
            {
                result.Append(c);
            }
        }

        var sanitized = result.ToString().Trim('_');
        
        // Handle edge cases
        if (string.IsNullOrWhiteSpace(sanitized))
            return "_sanitized_";

        // Prevent very long filenames (Windows limit is 255)
        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200);

        return sanitized;
    }

    /// <summary>
    /// Ensures a directory path has a trailing directory separator.
    /// Required for proper URI relative path calculation.
    /// </summary>
    private static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return path.EndsWith(Path.DirectorySeparatorChar.ToString()) 
            ? path 
            : path + Path.DirectorySeparatorChar;
    }
}