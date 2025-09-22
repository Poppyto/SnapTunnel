using SnapTunnel.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SnapTunnel.Models
{
    public class StreamTunnel
    {
        public TcpClient TcpClient { get; set; }
        public Stream Stream { get; set; }
        public HttpPathMethodModel HttpPathMethod { get; set; }
        public IDictionary<string, HttpHeaderValueModel> Headers { get; set; }
        public long ContentLength { get; set; }
        public bool IsChunked { get; set; }
        public byte[] RemainingBytes { get; set; }
    }
}
