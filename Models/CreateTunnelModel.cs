using SnapTunnel.Configurations;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace SnapTunnel.Models
{
    public class CreateTunnelHostModel
    {
        public IPAddress ServerAddress { get; set; }
        public int ServerPort { get; set; }
        public bool UseHttps { get; set; }
        public X509Certificate2 CertificateDomains { get; set; }
        public IEnumerable<TunnelConfiguration> TunnelsConfiguration { get; set; }
    }
}