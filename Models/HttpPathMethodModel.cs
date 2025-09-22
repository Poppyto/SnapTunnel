using System;

namespace SnapTunnel.Models
{
    public class HttpPathMethodModel
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string HttpVersion { get; set; }   // original token, e.g., "HTTP/1.1"
        public string Version { get; set; }       // numeric part only, e.g., "1.1"
        public int PathPosition { get; set; }
    }
}
