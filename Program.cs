using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using SnapTunnel.Configurations;
using SnapTunnel.Interfaces;
using SnapTunnel.Services;
using System;
using System.CommandLine;
using System.CommandLine.Help;

namespace SnapTunnel
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            var builder = Host.CreateApplicationBuilder(args);

            if (!AddCommandLine(builder, args))
            {
                return;
            }

            builder.Services.AddScoped<ICertificateService, CertificateService>();
            builder.Services.AddScoped<ITunnelService, TunnelService>();
            builder.Services.AddScoped<IEtcHostService, EtcHostService>();
            builder.Services.AddScoped<IHttpProtocolService, HttpProtocolService>();


            builder.Services.AddHostedService<ApplicationService>();
            var host = builder.Build();

            await host.RunAsync();
        }

        /// <summary>
        /// The whole CLI args
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="args"></param>
        private static bool AddCommandLine(HostApplicationBuilder builder, string[] args)
        {
            var verbosityOption = new Option<int?>("-v", "--verbosity")
            {
                Description = $"Log verbosity ({string.Join(", ", Enum.GetValues<LogLevel>().Select((a => $"{(int)a}: {a}")))})",
                HelpName = "0-6",
                Required = false,
            };

            var hostEtcOption = new Option<bool>("-a", "--addtohosts")
            {
                Description = "Append the domains as 127.0.0.1 into %System32%\\drivers\\etc\\hosts and remove them when the app exits)",
                Required = false,
            };

            var installRootCertOption = new Option<bool>("-i", "--installrootcert")
            {
                Description = "Install the root certificate in the current user trusted root certificate authorities (CAs)",
                Required = false,
            };

            var uninstallRootCertOption = new Option<bool>("-u", "--uninstallrootcert")
            {
                Description = "Uninstall the root certificate from the current user trusted root certificate authorities (CAs)",
                Required = false,
            };

            var tunnelsOption = new Option<List<TunnelConfiguration>>("-t", "--tunnel")
            {
                Description = "Create a tunnel",
                HelpName = "[http|https]:src_host:port>[http|https]:dest_host:port[|rewritepath:/(.*)>/api/openai_compat/$1|overwrite:/index.html>c:/file/index.html]",
                CustomParser = result =>
                {
                    var listTunnelsConfiguration = new List<TunnelConfiguration>();
                    foreach (var token in result.Tokens)
                    {
                        try
                        {
                            listTunnelsConfiguration.Add(TunnelConfiguration.Parse(token.Value)); // to avoid null if no value
                        }
                        catch (Exception ex)
                        {
                            result.AddError(ex.Message);
                        }
                    }
                    return listTunnelsConfiguration;
                },
                AllowMultipleArgumentsPerToken = true,
                Required = false,
            };

            RootCommand rootCommand = new("SnapTunnel - Still Not A Proxy, but a tunnel");
            rootCommand.Options.Add(verbosityOption);
            rootCommand.Options.Add(hostEtcOption);
            rootCommand.Options.Add(installRootCertOption);
            rootCommand.Options.Add(uninstallRootCertOption);
            rootCommand.Options.Add(tunnelsOption);

            rootCommand.SetAction(parseResult =>
            {
                var isAppendDomainToEtcHosts = parseResult.GetValue(hostEtcOption);
                var isInstallCertificate = parseResult.GetValue(installRootCertOption);
                var isUninstallCertificate = parseResult.GetValue(uninstallRootCertOption);
                var tunnelsConfigurations = parseResult.GetValue(tunnelsOption);
                var verbosity = parseResult.GetValue(verbosityOption);

                if (verbosity.HasValue)
                {
                    var minimumLogLevel = (LogLevel)verbosity.Value;

                    builder.Logging.ClearProviders();
                    // careful, appsettings.json could override this
                    builder.Logging.SetMinimumLevel(minimumLogLevel);
                }

                builder.Services.Configure<TunnelsConfiguration>(options =>
                {
                    options.IsAppendDomainToEtcHosts = isAppendDomainToEtcHosts;
                    options.IsInstallCertificate = isInstallCertificate;
                    options.IsUninstallCertificate = isUninstallCertificate;
                    options.Tunnels = tunnelsConfigurations;
                });

                return 0;
            });

            ParseResult parseResult = rootCommand.Parse(args);

            // Help -h
            if (parseResult.Action is HelpAction helpAction)
            {
                helpAction.Invoke(parseResult);
                return false;
            }

            if (0 != parseResult.Invoke())
                return false;

            return !parseResult.Errors.Any();
        }


        

    }
}
