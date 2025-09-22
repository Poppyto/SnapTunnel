using SnapTunnel.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;

namespace SnapTunnel.Services
{
    public class EtcHostService : IEtcHostService
    {
        private readonly ILogger<EtcHostService> _logger;
        private readonly string _hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

        public EtcHostService(ILogger<EtcHostService> logger)
        {
            _logger = logger;
        }

        public bool AddOrUpdateHostEntry(string ipAddress, string domain)
        {
            try
            {
                var lines = File.ReadAllLines(_hostsPath).ToList();
                var entry = $"{ipAddress}\t{domain}";
                bool updated = false;

                // Remove existing entry for the domain
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (lines[i].Trim().EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.RemoveAt(i);
                        updated = true;
                    }
                }

                lines.Add(entry);
                File.WriteAllLines(_hostsPath, lines);
                _logger.LogInformation(updated ? $"Updated host entry: {entry}" : $"Added host entry: {entry}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to add or update host entry for {domain}");
                return false;
            }
        }

        public bool RemoveHostEntry(string domain)
        {
            try
            {
                var lines = File.ReadAllLines(_hostsPath).ToList();
                int removed = lines.RemoveAll(line => line.Trim().EndsWith(domain, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    File.WriteAllLines(_hostsPath, lines);
                    _logger.LogInformation($"Removed host entry for {domain}");
                }
                else
                {
                    _logger.LogInformation($"No host entry found for {domain} to remove.");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to remove host entry for {domain}");
                return false;
            }
        }
    }
}