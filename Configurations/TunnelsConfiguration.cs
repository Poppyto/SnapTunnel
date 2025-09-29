using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnapTunnel.Configurations
{
    public class TunnelsConfiguration
    {
        public const string SectionName = "TunnelsConfiguration";

        public bool IsAppendDomainToEtcHosts { get; set; }
        public bool IsInstallCertificate { get; set; }
        public bool IsUninstallCertificate { get; set; }
        public IEnumerable<TunnelConfiguration> Tunnels { get; set; }
    }
}
