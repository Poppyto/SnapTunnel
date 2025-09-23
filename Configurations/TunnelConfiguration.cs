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

        public static TunnelConfiguration Parse(string tunnelStr)
        {
            var parts = tunnelStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var core = parts[0].Trim();

            // src>dest
            var sides = core.Split('>');
            if (sides.Length != 2)
                throw new FormatException("Invalid tunnel, src>dst expected.");

            var srcParts = sides[0].Split(':');
            var dstParts = sides[1].Split(':');

            if (!(srcParts[0] is "http" or "https"))
                throw new FormatException("Invalid source scheme, could only be http or https");

            if (!(dstParts[0] is "http" or "https"))
                throw new FormatException("Invalid dest scheme, could only be http or https");

            var tunnel = new TunnelConfiguration
            {
                UseHttpsSource = srcParts[0].Equals("https", StringComparison.OrdinalIgnoreCase),
                DomainSource = srcParts[1],
                PortSource = int.Parse(srcParts[2]),
                UseHttpsDestination = dstParts[0].Equals("https", StringComparison.OrdinalIgnoreCase),
                DomainDestination = dstParts[1],
                PortDestination = int.Parse(dstParts[2]),
            };

            // options
            foreach (var opt in parts.Skip(1))
            {
                var kv = opt.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim().ToLower();
                var val = kv[1].Trim();

                switch (key)
                {
                    case "rewritepath":
                        {
                            var rr = val.Split(">", 2);
                            if (rr.Length != 2)
                                throw new FormatException($"Rewrite invalide: {val}, attendu pattern=>remplacement");

                            tunnel.PathReplaces.Add(new PathReplaceConfiguration
                            {
                                PathRegexMatch = new Regex(rr[0], RegexOptions.Compiled),
                                PathRegexReplace = rr[1]
                            });
                        }
                        break;

                    case "overwrite":
                        {
                            var sep = val.IndexOf('>');
                            if (sep > 0)
                                tunnel.OverrideContents[val[..sep]] = val[(sep + 1)..];
                        }
                        break;
                }
            }

            return tunnel;
        }
    }
}
