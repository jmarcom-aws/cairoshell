using CairoDesktop.Application.Interfaces;
using CairoDesktop.Infrastructure.Logging;
using CairoDesktop.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;

namespace CairoDesktop.Infrastructure.DependencyInjection
{
    public static class DependencyInjectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IExtensionService, ExtensionService>();
            services.AddSingleton<IApplicationUpdateService, WinSparkleApplicationUpdateService>();

            return services;
        }

        public static IServiceCollection AddDependencyLoadingServices(this IServiceCollection services, IConfiguration configuration, string path, string pattern = null)
        {
            services.LoadDependencies(path, pattern);

            return services;
        }

        public static ILoggingBuilder AddInfrastructureLogging(this ILoggingBuilder builder, Action<CairoFileLoggerOptions> configure = null)
        {
            // Add legacy CairoLogger support
            builder.Services.AddSingleton<LegacyCairoLogObserver>();

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, CairoFileLoggerProvider>());

            if (configure != null)
            {
                builder.Services.Configure(configure);
            }


            return builder;
        }
    }
}