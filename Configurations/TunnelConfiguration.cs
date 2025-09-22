using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace SnapTunnel.Configurations
{
    public class TunnelConfiguration
    {
        public IPHostEntry IpDestination { get; set; }
        public string DomainSource { get; set; }
        public int PortSource { get; set; }
        public string DomainDestination { get; set; }
        public int PortDestination { get; set; }
        public bool UseHttpsDestination { get; set; }
        public bool UseHttpsSource { get; set; }
        public IList<PathReplaceConfiguration> PathReplaces { get; set; } = new List<PathReplaceConfiguration>();
        public IDictionary<string, string> OverrideContents { get; set; } = new Dictionary<string, string>();
    }
}