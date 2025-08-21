using System.Security.Cryptography;
using System.Text;

namespace DB2XL.Data.Checksum;

/// <summary>
/// Calculates checksums for data integrity verification
/// </summary>
public sealed class DataChecksumCalculator : IDisposable
{
    private readonly SHA256 _sha256;
    private readonly MemoryStream _buffer;
    private bool _disposed;

    /// <summary>
    /// Creates a new checksum calculator
    /// </summary>
    public DataChecksumCalculator()
    {
        _sha256 = SHA256.Create();
        _buffer = new MemoryStream();
    }

    /// <summary>
    /// Updates the checksum with a field value
    /// </summary>
    /// <param name="value">The field value (null for NULL values)</param>
    public void AddField(string? value)
    {
        if (_disposed) 
            throw new ObjectDisposedException(nameof(DataChecksumCalculator));

        byte[] data;
        if (value == null)
        {
            data = new byte[] { 0x00 };
        }
        else
        {
            data = Encoding.UTF8.GetBytes(value);
        }

        _buffer.Write(data, 0, data.Length);
        
        // Field separator
        _buffer.WriteByte(0x1F);
    }

    /// <summary>
    /// Marks the end of a row
    /// </summary>
    public void EndRow()
    {
        if (_disposed) 
            throw new ObjectDisposedException(nameof(DataChecksumCalculator));

        // Row separator
        _buffer.WriteByte(0x1E);
    }

    /// <summary>
    /// Finalizes the checksum calculation and returns the hash
    /// </summary>
    /// <returns>SHA256 hash as hexadecimal string</returns>
    public string GetChecksum()
    {
        if (_disposed) 
            throw new ObjectDisposedException(nameof(DataChecksumCalculator));

        _buffer.Seek(0, SeekOrigin.Begin);
        var hash = _sha256.ComputeHash(_buffer);
        
        return Convert.ToHexString(hash);
    }
    
    /// <summary>
    /// Resets the calculator for reuse
    /// </summary>
    public void Reset()
    {
        if (_disposed) 
            throw new ObjectDisposedException(nameof(DataChecksumCalculator));
            
        _buffer.SetLength(0);
        _buffer.Position = 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _sha256.Dispose();
            _buffer.Dispose();
            _disposed = true;
        }
    }
}