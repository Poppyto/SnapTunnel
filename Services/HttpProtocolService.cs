using SnapTunnel.Interfaces;
using SnapTunnel.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace SnapTunnel.Services
{
    public sealed class HttpProtocolService : IHttpProtocolService
    {
        /*--------------------------------------------------------------
         *  Constant patterns – declared once, reused for every call.
         *--------------------------------------------------------------*/
        private static readonly byte[] EndOfHeaders = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
        private static readonly byte[] EndOfChunked = new byte[] { (byte)'0', (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

        private readonly ILogger<HttpProtocolService> _logger;

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="logger"></param>
        public HttpProtocolService(ILogger<HttpProtocolService> logger)
        {
            _logger = logger;
        }

        /*--------------------------------------------------------------
         *  Helpers
         *--------------------------------------------------------------*/

        private static bool ContainsPattern(ReadOnlySpan<byte> span, ReadOnlySpan<byte> pattern) =>
            span.IndexOf(pattern) >= 0;

        private static int FindLineEnd(ReadOnlySpan<byte> span)
        {
            // Returns the index of the first '\r' that is part of a "\r\n" pair,
            // or -1 if the pair is not found.
            var idx = span.IndexOf("\r\n"u8);
            return idx == -1 ? -1 : idx;
        }

        private static void TrimWhitespace(ReadOnlySpan<byte> span, ref int start, ref int length)
        {
            // Trim left
            while (length > 0 && (span[start] is (byte)' ' or (byte)'\t'))
            {
                start++;
                length--;
            }

            // Trim right
            while (length > 0 && (span[start + length - 1] is (byte)' ' or (byte)'\t'))
            {
                length--;
            }
        }

        private static void ValidateBuffer(byte[] buffer, int offset, int length)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || length < 0 || offset + length > buffer.Length)
                throw new ArgumentOutOfRangeException();
        }

        /*--------------------------------------------------------------
         *  Public API
         *--------------------------------------------------------------*/
        public bool IsHttpHeadersComplete(byte[] buffer, int totalBytesRead)
        {
            ValidateBuffer(buffer, 0, totalBytesRead);

            return ContainsPattern(buffer.AsSpan(0, totalBytesRead), EndOfHeaders);
        }

        public int IndexOfZeroLengthChunk(byte[] buffer, int totalBytesRead)
        {
            ValidateBuffer(buffer, 0, totalBytesRead);

            return buffer.AsSpan(0, totalBytesRead).IndexOf(EndOfChunked);
        }

        public HttpPathMethodModel? ReadHttpPathMethod(byte[] buffer, int offset, int length)
        {
            ValidateBuffer(buffer, offset, length);

            // Quick‑reject too‑short lines (e.g. “GET / H”)
            if (length < 8) // “GET / H” is the smallest valid request line
                return null;

            ReadOnlySpan<byte> span = buffer.AsSpan(offset, length);

            // ---- Request line -------------------------------------------------
            int lineEnd = span.IndexOf((byte)'\r');
            if (lineEnd == -1) return null; // malformed

            ReadOnlySpan<byte> requestLine = span.Slice(0, lineEnd);

            // ---- Method -------------------------------------------------------
            int firstSpace = requestLine.IndexOf((byte)' ');
            if (firstSpace == -1)
            {
                _logger.LogDebug("Invalid HTTP request line: no space after method. Offset={Offset}, Length={Length}", offset, length);
                return null;
            }
            string method = Encoding.ASCII.GetString(requestLine.Slice(0, firstSpace)).ToUpperInvariant();

            // ---- Path ---------------------------------------------------------
            int pathStart = firstSpace + 1;
            while (pathStart < requestLine.Length && requestLine[pathStart] == (byte)' ') pathStart++;

            int pathEnd = pathStart;
            while (pathEnd < requestLine.Length && requestLine[pathEnd] != (byte)' ') pathEnd++;

            string path = Encoding.UTF8.GetString(requestLine.Slice(pathStart, pathEnd - pathStart));

            // ---- HTTP version -------------------------------------------------
            int versionStart = pathEnd + 1;
            while (versionStart < requestLine.Length && requestLine[versionStart] == (byte)' ') versionStart++;

            if (versionStart >= requestLine.Length)
            {
                _logger.LogDebug("Invalid HTTP request line: version not found");
                return null;
            }
            string httpVersion = Encoding.ASCII.GetString(requestLine.Slice(versionStart));

            // ---- Version validation -------------------------------------------
            if (!httpVersion.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Unrecognized HTTP version: {Version}", httpVersion);
                return null;
            }

            string version = httpVersion.Substring(5);
            if (string.IsNullOrEmpty(version))
            {
                _logger.LogError("Invalid HTTP version format: {Version}", httpVersion);
                return null;
            }

            // ---- Absolute position of the path ---------------------------------
            int pathPosition = offset + pathStart;

            return new HttpPathMethodModel
            {
                Method = method,
                Path = path,
                HttpVersion = httpVersion,
                Version = version,
                PathPosition = pathPosition
            };
        }

        public IDictionary<string, HttpHeaderValueModel> ReadHttpHeaderValues(
            byte[] buffer,
            int offset,
            int length,
            out int indexEndOfHeaders)
        {
            ValidateBuffer(buffer, offset, length);

            indexEndOfHeaders = -1;
            var headers = new Dictionary<string, HttpHeaderValueModel>(16, StringComparer.OrdinalIgnoreCase);
            ReadOnlySpan<byte> span = buffer.AsSpan(offset, length);

            int i = 0;
            while (i < span.Length)
            {
                // ---- Find the end of the current line (CRLF) -----------------
                int lineEnd = FindLineEnd(span.Slice(i));
                if (lineEnd == -1) break; // incomplete header line

                lineEnd += i; // convert to absolute index inside the original span

                // ---- Blank line => end of headers ---------------------------
                if (lineEnd == i)
                {
                    indexEndOfHeaders = i + 2; // skip the CRLF
                    break;
                }

                // ---- Locate the ':' that separates name/value ---------------
                int colon = -1;
                for (int j = i; j < lineEnd; j++)
                {
                    if (span[j] == (byte)':')
                    {
                        colon = j;
                        break;
                    }
                }

                if (colon > i)
                {
                    // ----- Header name (trimmed) ---------------------------------
                    int nameStart = i;
                    int nameLength = colon - i;
                    TrimWhitespace(span, ref nameStart, ref nameLength);
                    string headerName = Encoding.ASCII.GetString(span.Slice(nameStart, nameLength));

                    // ----- Header value (trim leading + trailing whitespace) -----
                    int valueStart = colon + 1;
                    while (valueStart < lineEnd &&
                           (span[valueStart] == (byte)' ' || span[valueStart] == (byte)'\t'))
                        valueStart++;

                    int valueLength = lineEnd - valueStart;
                    // Trim trailing whitespace using the same helper
                    TrimWhitespace(span, ref valueStart, ref valueLength);
                    // Note: after the call `valueStart` is relative to the slice, so we need the original offset:
                    int absoluteValueStart = colon + 1 + (valueStart - (colon + 1));

                    string headerValue = Encoding.UTF8.GetString(span.Slice(absoluteValueStart, valueLength));

                    headers[headerName] = new HttpHeaderValueModel
                    {
                        Value = headerValue,
                        Position = absoluteValueStart,
                        Length = valueLength
                    };
                }

                // ---- Move to the next header line ----------------------------
                i = lineEnd + 2; // skip "\r\n"
            }

            return headers;
        }
    }
}
