﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.IoTSolutions.IotHubManager.WebService.Auth;
using Microsoft.Azure.IoTSolutions.IotHubManager.WebService.Runtime;
using Microsoft.Azure.IoTSolutions.IotHubManager.RecurringTasksAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Azure.IoTSolutions.IotHubManager.Services.Diagnostics.ILogger;
using DeviceTwinUpdateAgent;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Microsoft.Azure.IoTSolutions.IotHubManager.WebService
{
    public class Startup
    {
        // Initialized in `Startup`
        public IConfigurationRoot Configuration { get; }
        private DeviceTwinUpdateAgent.IAgent _StatusUpdateAgent;
        private RecurringTasksAgent.IAgent _RecurringTasksAgent;
        // Initialized in `ConfigureServices`
        public IContainer ApplicationContainer { get; private set; }

        // Invoked by `Program.cs`
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddIniFile("appsettings.ini", optional: false, reloadOnChange: true);
            this.Configuration = builder.Build();
        }

        // This is where you register dependencies, add services to the
        // container. This method is called by the runtime, before the
        // Configure method below.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            // Setup (not enabling yet) CORS
            services.AddCors();

            // Add controllers as services so they'll be resolved.
            services.AddMvc().AddControllersAsServices();

            // Prepare DI container
            this.ApplicationContainer = DependencyResolution.Setup(services);

            // Print some useful information at bootstrap time
            this.PrintBootstrapInfo(this.ApplicationContainer);

            // Create the IServiceProvider based on the container
            return new AutofacServiceProvider(this.ApplicationContainer);
        }

        // This method is called by the runtime, after the ConfigureServices
        // method above. Use this method to add middleware.
        public void Configure(
            IApplicationBuilder app,
            IHostingEnvironment env,
            ILoggerFactory loggerFactory,
            ICorsSetup corsSetup,
            IApplicationLifetime appLifetime)
        {
            loggerFactory.AddConsole(this.Configuration.GetSection("Logging"));

            // Check for Authorization header before dispatching requests
            app.UseMiddleware<AuthMiddleware>();

            // Enable CORS - Must be before UseMvc
            // see: https://docs.microsoft.com/en-us/aspnet/core/security/cors
            corsSetup.UseMiddleware(app);

            app.UseMvc();

            // If you want to dispose of resources that have been resolved in the
            // application container, register for the "ApplicationStopped" event.
            appLifetime.ApplicationStopped.Register(() => this.ApplicationContainer.Dispose());
            // Run a recurring tasks which updates the device properties in CosmosDB every 1 hour
          
            appLifetime.ApplicationStarted.Register(this.StartAgents);
           
        }
        private void StartAgents()
        {
            Task.Run(async () => {
                this._StatusUpdateAgent = this.ApplicationContainer.Resolve<DeviceTwinUpdateAgent.IAgent>();
                this._StatusUpdateAgent.Run();
            });
            Task.Run(async () => {
                this._RecurringTasksAgent = this.ApplicationContainer.Resolve<RecurringTasksAgent.IAgent>();
                this._RecurringTasksAgent.Run();
            });

        }
        private void PrintBootstrapInfo(IContainer container)
        {
            var log = container.Resolve<ILogger>();
            log.Info("Web service started", () => new { Uptime.ProcessId });
        }
    }
}
