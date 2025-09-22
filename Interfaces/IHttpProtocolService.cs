using SnapTunnel.Models;
using SnapTunnel.Services;

namespace SnapTunnel.Interfaces
{
    public interface IHttpProtocolService
    {
        bool IsHttpHeadersComplete(byte[] buffer, int totalBytesRead);
        HttpPathMethodModel? ReadHttpPathMethod(byte[] buffer, int offset, int length);
        IDictionary<string, HttpHeaderValueModel> ReadHttpHeaderValues(byte[] buffer, int offset, int length, out int indexEndOfHeaders);
        int IndexOfZeroLengthChunk(byte[] buffer, int totalBytesRead);
    }
}
