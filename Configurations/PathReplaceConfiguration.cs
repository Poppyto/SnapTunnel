using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace SnapTunnel.Configurations
{
    public class PathReplaceConfiguration
    {
        public Regex PathRegexMatch { get; set; }
        public string PathRegexReplace { get; set; }
    }
}