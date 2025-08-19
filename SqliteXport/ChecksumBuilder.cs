using System.Security.Cryptography;
using System.Text;

namespace DB2XL;

internal sealed class ChecksumBuilder : IDisposable
{
    private readonly SHA256 _sha256;
    private bool _disposed;

    internal ChecksumBuilder()
    {
        _sha256 = SHA256.Create();
    }

    internal void UpdateField(string? value)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChecksumBuilder));

        byte[] data;
        if (value == null)
        {
            data = new byte[] { 0x00 };
        }
        else
        {
            data = Encoding.UTF8.GetBytes(value);
        }

        _sha256.TransformBlock(data, 0, data.Length, null, 0);
        
        var separator = new byte[] { 0x1F };
        _sha256.TransformBlock(separator, 0, separator.Length, null, 0);
    }

    internal void EndRow()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChecksumBuilder));

        var rowSeparator = new byte[] { 0x1E };
        _sha256.TransformBlock(rowSeparator, 0, rowSeparator.Length, null, 0);
    }

    internal string FinalizeHex()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChecksumBuilder));

        _sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = _sha256.Hash!;
        
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _sha256.Dispose();
            _disposed = true;
        }
    }
}