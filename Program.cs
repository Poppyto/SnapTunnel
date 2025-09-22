using SnapTunnel.Configurations;
using SnapTunnel.Interfaces;
using SnapTunnel.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.Runtime;

namespace SnapTunnel
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var switchMappings = new Dictionary<string, string>()
            {
                { "-t", "tunnel" },
                { "--tunnel", "tunnel" },
                { "-v", "verbose" },
                { "--verbose", "verbose" },
            };

            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddScoped<ICertificateService, CertificateService>();
            builder.Services.AddScoped<ITunnelService, TunnelService>();
            builder.Services.AddScoped<IEtcHostService, EtcHostService>();
            builder.Services.AddScoped<IHttpProtocolService, HttpProtocolService>();

            builder.Configuration.AddCommandLine(args, switchMappings);

            // Options / AOT compatible
            AddConfigurations(builder);

            builder.Services.AddHostedService<ApplicationService>();
            var host = builder.Build();


            await host.RunAsync();
        }

        //AOT compatibility
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TunnelsConfiguration))]
        private static void AddConfigurations(HostApplicationBuilder builder)
        {
            builder.Services.Configure<TunnelsConfiguration>(builder.Configuration.GetSection(TunnelsConfiguration.SectionName));
        }
    }
}
