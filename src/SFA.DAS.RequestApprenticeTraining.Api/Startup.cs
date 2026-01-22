using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NServiceBus.ObjectBuilder.MSDependencyInjection;
using SFA.DAS.Configuration.AzureTableStorage;
using SFA.DAS.RequestApprenticeTraining.Api.AppStart;
using SFA.DAS.RequestApprenticeTraining.Api.Authentication;
using SFA.DAS.RequestApprenticeTraining.Api.Authorization;
using SFA.DAS.RequestApprenticeTraining.Api.TaskQueue;
using SFA.DAS.RequestApprenticeTraining.Domain.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SFA.DAS.RequestApprenticeTraining.Api
{
    [ExcludeFromCodeCoverage]
    public class Startup
    {
        private readonly IWebHostEnvironment Environment;
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var config = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddEnvironmentVariables();

            config.AddAzureTableStorage(options =>
            {
                options.ConfigurationKeys = configuration["ConfigNames"].Split(",");
                options.StorageConnectionString = configuration["ConfigurationStorageConnectionString"];
                options.EnvironmentName = configuration["EnvironmentName"];
                options.PreFixConfigurationKeys = false;
            });
#if DEBUG
            config.AddJsonFile($"appsettings.Development.json", optional: true);
#endif

            Configuration = config.Build();
            Environment = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOpenTelemetryRegistration(Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]!);

            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromSeconds(7_776_000); // 90 days;
            });

            var applicationSettingsSection = Configuration.GetSection(nameof(ApplicationSettings));
            var applicationSettings = applicationSettingsSection.Get<ApplicationSettings>();

            services.Configure<ApplicationSettings>(applicationSettingsSection);
            services.AddSingleton(s => s.GetRequiredService<IOptions<ApplicationSettings>>().Value);

            var isDevelopment = Environment.IsDevelopment();
            services
                .AddApiAuthentication(applicationSettings, isDevelopment)
                .AddApiAuthorization(isDevelopment);

            services.AddSwaggerGen(opt =>
            {
                opt.SwaggerDoc("v1", new OpenApiInfo { Title = "SFA.DAS.RequestApprenticeTraining.Api", Version = "v1" });

                if (!isDevelopment)
                {
                    opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                    {
                        In = ParameterLocation.Header,
                        Description = "Please enter token",
                        Name = "Authorization",
                        Type = SecuritySchemeType.Http,
                        BearerFormat = "JWT",
                        Scheme = "bearer"
                    });

                    opt.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type=ReferenceType.SecurityScheme,
                                    Id="Bearer"
                                }
                            },
                            new string[]{}
                        }
                    });
                }
            });

            services.AddDatabaseRegistration(Configuration);

            services.AddHostedService<TaskQueueHostedService>();

            services.AddHealthChecks()
                .AddCheck<RequestApprenticeTrainingHealthCheck>(nameof(RequestApprenticeTrainingHealthCheck));

            services
                .AddControllers(o =>
                {
                    if (!isDevelopment)
                    {
                        o.Filters.Add(new AuthorizeFilter(PolicyNames.Default));
                    }
                })
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                });

            services.AddServices();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SFA.DAS.RequestApprenticeTraining.Api v1"));

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseMiddleware<SecurityHeadersMiddleware>();

            app.UseAuthentication();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/ping");
            });
        }

        public void ConfigureContainer(UpdateableServiceProvider serviceProvider)
        {
            serviceProvider.StartNServiceBus(Configuration).GetAwaiter().GetResult();
        }
    }
}
