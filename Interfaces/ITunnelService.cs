using SnapTunnel.Models;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace SnapTunnel.Interfaces
{
    public interface ITunnelService
    {
        Task StartTunnelAsync(CreateTunnelHostModel createTunnelHostModel, CancellationToken cancellationToken = default);
    }
}