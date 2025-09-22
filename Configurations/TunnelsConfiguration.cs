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

        public List<TunnelConfiguration> Tunnels { get; set; } = new();
    }
}
