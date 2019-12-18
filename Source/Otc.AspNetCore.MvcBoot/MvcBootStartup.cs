using Graceterm;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Otc.AuthorizationContext.AspNetCore.Jwt;
using Otc.Caching.DistributedCache.All;
using Otc.Extensions.Configuration;
using Otc.RequestTracking.AspNetCore;
using Serilog;
using Serilog.Exceptions;
using Serilog.Formatting.Json;
using System;
using System.Text.RegularExpressions;

namespace Otc.AspNetCore.MvcBoot
{
    public abstract class MvcBootStartup
    {
        protected MvcBootStartup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            MvcBootOptions = Configuration.SafeGet<MvcBootOptions>();
        }

        public IConfiguration Configuration { get; }

        public MvcBootOptions MvcBootOptions { get; }

        private string requestTrackDisableBodyCapturingForUrl;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddAspNetCoreHttpClientFactoryWithCorrelation();

            services.AddLogging(configure =>
            {
                configure.ClearProviders();

                if (MvcBootOptions.EnableLogging)
                {
                    var loggerConfiguration = new LoggerConfiguration()
                        .ReadFrom.Configuration(Configuration)
                        .Enrich.WithExceptionDetails();

                    if (MvcBootOptions.LoggingType != LoggingType.SerilogRawConfiguration)
                    {
                        loggerConfiguration = loggerConfiguration
                            .Enrich.FromLogContext()
                            .Enrich.WithProcessId()
                            .Enrich.WithProcessName()
                            .Enrich.WithThreadId()
                            .Enrich.WithEnvironmentUserName()
                            .Enrich.WithMachineName();

                        if (MvcBootOptions.LoggingType == LoggingType.MvcBootFile)
                            loggerConfiguration =
                                loggerConfiguration.WriteTo.Async(a =>
                                    a.File($"logs/log-.txt", rollingInterval: RollingInterval.Day));
                        else
                            loggerConfiguration =
                                loggerConfiguration.WriteTo.Async(a =>
                                    a.Console(new JsonFormatter()));
                    }

                    Log.Logger = loggerConfiguration.CreateLogger();

                    configure.AddSerilog();
                    configure.AddDebug();
                }
            });

            services.AddOtcAspNetCoreJwtAuthorizationContext(Configuration.SafeGet<JwtConfiguration>());

            services.AddExceptionHandling();

            var requestTrackerConfiguration = Configuration.SafeGet<RequestTrackerConfiguration>();

            if (string.IsNullOrEmpty(requestTrackerConfiguration.ExcludeUrl))
            {
                requestTrackerConfiguration.ExcludeUrl = Regex.Escape(HealthChecksController.RoutePath);
            }
            else
            {
                requestTrackerConfiguration.ExcludeUrl =
                    $"({requestTrackerConfiguration.ExcludeUrl})|" +
                    $"({Regex.Escape(HealthChecksController.RoutePath)})";
            }

            if (!string.IsNullOrEmpty(requestTrackDisableBodyCapturingForUrl))
            {
                if (string.IsNullOrEmpty(requestTrackerConfiguration.DisableBodyCapturingForUrl))
                {
                    requestTrackerConfiguration.DisableBodyCapturingForUrl =
                        requestTrackDisableBodyCapturingForUrl;
                }
                else
                {
                    requestTrackerConfiguration.DisableBodyCapturingForUrl =
                        $"({requestTrackerConfiguration.DisableBodyCapturingForUrl})|" +
                        $"({requestTrackDisableBodyCapturingForUrl})";
                }
            }

            services.AddRequestTracking(requestTracker =>
            {
                requestTracker.Configure(requestTrackerConfiguration);
            });

            services.AddOtcDistributedCache(Configuration.SafeGet<DistributedCacheConfiguration>());

            services
                .AddMvc(options =>
                {
                    ConfigureMvcOptionsService(options);
                })
                .AddJsonOptions(options =>
                {
                    if (MvcBootOptions.EnableStringEnumConverter)
                    {
                        options.SerializerSettings.Converters.Add(
                            new Newtonsoft.Json.Converters.StringEnumConverter());
                    }

                    options.SerializerSettings.NullValueHandling =
                        Newtonsoft.Json.NullValueHandling.Ignore;
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            ConfigureMvcServices(services);
        }

        protected abstract void ConfigureMvcServices(IServiceCollection services);

        public virtual void ConfigureMvcOptionsService(MvcOptions options) { }

        public virtual void ConfigureMvcApp(IRouteBuilder configureRoutes) { }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRequestTracking();

            app.UseGraceterm(options =>
            {
                options.IgnorePath(HealthChecksController.RoutePath);
            });

            app.UseStaticFiles();
            app.UseMvcWithDefaultRoute();
            app.UseMvc(m =>
            {
                ConfigureMvcApp(m);
            });
        }
    }
}
