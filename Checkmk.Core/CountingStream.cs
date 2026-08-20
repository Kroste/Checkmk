using System.Text;

namespace Checkmk.Core;

/// <summary>
/// Lese-Wrapper, der die durchgereichten Bytes zaehlt und gedrosselt an ein
/// <see cref="IProgress{T}"/> meldet.
///
/// Zwei Details, die hier nicht wegoptimiert werden duerfen:
/// <list type="number">
/// <item><b>Drosselung.</b> <see cref="IProgress{T}"/> ist im UI-Prozess an den
/// SynchronizationContext gebunden — jeder Report ist ein Dispatcher-Post. Bei
/// 30 MB und 8-KB-Reads waeren das ~4000 Posts, die den UI-Thread genauso
/// zustellen wie der alte synchrone Parse. Deshalb nur alle
/// <see cref="ReportEveryBytes"/> Bytes melden.</item>
/// <item><b>Kopf-Puffer.</b> Beim Streamen gibt es keinen Body-String mehr fuer
/// die Fehlermeldung. Die ersten Kilobytes werden deshalb mitgeschnitten — bei
/// einer unerwarteten Antwort (HTML-Loginseite vom Proxy statt JSON) steht
/// damit trotzdem etwas Brauchbares in der <c>CheckmkApiException</c>.</item>
/// </list>
/// </summary>
internal sealed class CountingStream(Stream inner, long? totalBytes,
    IProgress<TransferProgress>? progress) : Stream
{
    private const int ReportEveryBytes = 256 * 1024;
    private const int HeadBufferSize = 2048;

    private readonly byte[] _head = new byte[HeadBufferSize];
    private int _headLength;
    private long _bytesRead;
    private long _lastReported;

    /// <summary>Die ersten Kilobytes der Antwort — Diagnose-Kontext fuer Fehlermeldungen.</summary>
    public string HeadSnippet => Encoding.UTF8.GetString(_head, 0, _headLength);

    public long BytesRead => _bytesRead;

    private void Track(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            // Stream-Ende: den Rest auf jeden Fall noch melden, sonst bleibt der
            // Balken kurz vor dem Ziel stehen.
            Report(force: true);
            return;
        }

        if (_headLength < HeadBufferSize)
        {
            var take = Math.Min(chunk.Length, HeadBufferSize - _headLength);
            chunk[..take].CopyTo(_head.AsSpan(_headLength));
            _headLength += take;
        }

        _bytesRead += chunk.Length;
        Report(force: false);
    }

    private void Report(bool force)
    {
        if (progress is null) return;
        if (!force && _bytesRead - _lastReported < ReportEveryBytes) return;
        _lastReported = _bytesRead;
        progress.Report(new TransferProgress(_bytesRead, totalBytes));
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        Track(buffer.AsSpan(offset, n));
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        var n = inner.Read(buffer);
        Track(buffer[..n]);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Track(buffer.Span[..n]);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        Track(buffer.AsSpan(offset, n));
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => totalBytes ?? throw new NotSupportedException();
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
