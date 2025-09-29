using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnapTunnel.Configurations;
using SnapTunnel.Interfaces;
using SnapTunnel.Models;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace SnapTunnel.Services
{
    public partial class ApplicationService : IApplicationService
    {
        const string CertificateSubjectRoot = "SnapTunnel Secure Certificate Authority";
        const string CertificateSubjectDomain = "SnapTunnel Wildcard Domain Secure Certificate Authority";

        private readonly ILogger<ApplicationService> _logger;
        private readonly ICertificateService _certificateService;
        private readonly ITunnelService _tunnelService;
        private readonly IEtcHostService _etcHostService;
        private readonly IOptions<TunnelsConfiguration> _tunnelsConfiguration;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;

        public ApplicationService(ICertificateService certificateService, ILogger<ApplicationService> logger, IEtcHostService etcHostService, ITunnelService tunnelService, IOptions<TunnelsConfiguration> tunnelsConfiguration, IHostApplicationLifetime hostApplicationLifetime)
        {
            _certificateService = certificateService;
            _logger = logger;
            _etcHostService = etcHostService;
            _tunnelService = tunnelService;
            _tunnelsConfiguration = tunnelsConfiguration;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start Application");

            StoreLocation storeLocation = StoreLocation.CurrentUser;

            var tunnelsConfiguration = _tunnelsConfiguration.Value;
            if (tunnelsConfiguration == null)
            {
                _hostApplicationLifetime.StopApplication();
                return;
            }

            var isInstallRootCert = tunnelsConfiguration.IsInstallCertificate;
            var isUninstallRootCert = tunnelsConfiguration.IsUninstallCertificate;

            if (isUninstallRootCert)
            {
                RemoveRootCertificate(storeLocation, CertificateSubjectRoot);

                _hostApplicationLifetime.StopApplication();
                return;
            }

            X509Certificate2? rootCert = null;
            if (isInstallRootCert && !GetOrCreateRootCertificate(storeLocation, CertificateSubjectRoot, out rootCert))
            {
                _hostApplicationLifetime.StopApplication();
                return;
            }

            if (rootCert == null)
            {
                GetRootCertificate(storeLocation, CertificateSubjectRoot, out rootCert);
            }

            if (rootCert == null)
            {
                _logger.LogError("Root certificate is required to create domain certificates. Please install the root certificate first.");
                _hostApplicationLifetime.StopApplication();
                return;
            }

            var tunnelHosts = _tunnelsConfiguration.Value.Tunnels;

            if (tunnelHosts == null || !tunnelHosts.Any())
            {
                _hostApplicationLifetime.StopApplication();
                return;
            }

            var domainsSource = tunnelHosts.Select(t => t.DomainSource).Distinct(StringComparer.OrdinalIgnoreCase);

            if (!GetOrCreateSignedCertificate(storeLocation, rootCert, CertificateSubjectDomain, domainsSource, out var certificateDomains))
            {
                _hostApplicationLifetime.StopApplication();
                return;
            }

            var isAppendDomainToEtcHosts = tunnelsConfiguration.IsAppendDomainToEtcHosts;

            foreach (var tunnelHost in tunnelHosts)
            {
                if (isAppendDomainToEtcHosts)
                {
                    _etcHostService.RemoveHostEntry(tunnelHost.DomainSource);
                }

                var ipHostEntry = await Dns.GetHostEntryAsync(tunnelHost.DomainDestination);
                tunnelHost.IpDestination = ipHostEntry;

                if (isAppendDomainToEtcHosts)
                {
                    if (!_etcHostService.AddOrUpdateHostEntry("127.0.0.1", tunnelHost.DomainSource))
                    {
                        _logger.LogError("Failed to add or update hosts file entry.");

                        _hostApplicationLifetime.StopApplication();
                        return;
                    }
                }
            }

            ValidatePorts(tunnelHosts);

            _logger.LogInformation("Let's Start Tunnels");

            var taskTunnels = new List<Task>();
            foreach (var portSource in tunnelHosts.Select(a => a.PortSource).Distinct())
            {
                var createTunnelHostModel = new CreateTunnelHostModel
                {
                    ServerAddress = IPAddress.Any,
                    ServerPort = portSource,
                    UseHttps = tunnelHosts.Any(t => t.PortSource == portSource && t.UseHttpsSource),
                    CertificateDomains = certificateDomains!,
                    TunnelsConfiguration = tunnelHosts.Where(t => t.PortSource == portSource)
                };

                var taskTunnel = _tunnelService.StartTunnelAsync(createTunnelHostModel, cancellationToken);
                taskTunnels.Add(taskTunnel);
            }

            await Task.WhenAll(taskTunnels);
        }

        private void ValidatePorts(IEnumerable<TunnelConfiguration> tunnelHosts)
        {
            var differentSchemesSamePort = tunnelHosts.GroupBy(a => a.PortSource)
                       .Where(a =>
                            a.GroupBy(b => b.UseHttpsDestination).Count() > 1
                            );

            if (differentSchemesSamePort.Any())
            {
                foreach (var group in differentSchemesSamePort)
                {
                    _logger.LogError("Port {Port} is used with different schemes: http & https", group.Key);
                }

                throw new InvalidOperationException("Ports are used with different schemes.");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stop Application");

            var tunnelsConfiguration = _tunnelsConfiguration.Value;


            var isAppendDomainToEtcHosts = tunnelsConfiguration.IsAppendDomainToEtcHosts;

            if (isAppendDomainToEtcHosts)
            {
                var tunnelHosts = tunnelsConfiguration.Tunnels;

                if (tunnelHosts != null)
                {
                    _logger.LogInformation("Unregistering domains in etc/hosts");

                    foreach (var tunnelHost in tunnelHosts)
                    {
                        _etcHostService.RemoveHostEntry(tunnelHost.DomainSource);
                    }
                }
            }
        }

        private bool GetOrCreateRootCertificate(StoreLocation storeLocation, string subject, out X509Certificate2? cert)
        {
            var storeName = StoreName.Root;

            if (!_certificateService.IsCertificateInstalled(subject, storeName, storeLocation))
            {
                cert = _certificateService.CreateRootCertificate(subject);

                if (!_certificateService.InstallCertificate(cert, storeName, storeLocation))
                {
                    _logger.LogError("Failed to install the certificate.");
                    return false;
                }

                _logger.LogInformation("certificate created and installed in the Store.");
            }
            else
            {
                _logger.LogInformation("certificate already present in the Store.");

                cert = _certificateService.GetCertificate(subject, storeName, storeLocation);
            }

            return true;
        }

        private bool GetRootCertificate(StoreLocation storeLocation, string subject, out X509Certificate2? cert)
        {
            var storeName = StoreName.Root;

            if (_certificateService.IsCertificateInstalled(subject, storeName, storeLocation))
            {
                cert = _certificateService.GetCertificate(subject, storeName, storeLocation);
                return true;
            }

            cert = null;
            return false;
        }

        private bool GetOrCreateSignedCertificate(StoreLocation storeLocation, X509Certificate2? rootCert, string certname, IEnumerable<string> domains, out X509Certificate2? cert)
        {
            cert = null;
            bool isRoot = rootCert == null;

            var storeName = StoreName.My;

            if (!_certificateService.IsCertificateInstalled(certname, storeName, storeLocation))
            {
                cert = _certificateService.CreateSignedCertificate(rootCert, certname, domains);

                _logger.LogInformation("certificate created.");
            }
            else
            {
                _logger.LogInformation("certificate already present in the Store.");

                cert = _certificateService.GetCertificate(certname, storeName, storeLocation);
            }

            return true;
        }

        private bool RemoveRootCertificate(StoreLocation storeLocation, string subject)
        {
            var storeName = StoreName.Root;

            if (!_certificateService.IsCertificateInstalled(subject, storeName, storeLocation))
            {
                _logger.LogInformation("Certificate is not present in the Store.");
                return true;
            }

            var cert = _certificateService.GetCertificate(subject, storeName, storeLocation);
            if (!_certificateService.UninstallCertificate(cert, storeName, storeLocation))
            {
                _logger.LogError("Cannot remove the certificate");
                return false;
            }

            return true;
        }
    }
}