﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics.AspNetCore;
using Baseline;
using IdentityServer4.Models;
using JasperFx.CodeGeneration;
using Lamar.IoC.Instances;
using Lamar.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Lamar.Testing.AspNetCoreIntegration
{
    public static class Program
    {
        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder()
                .UseLamar()

                // This is the override
                .OverrideServices(s => s.For<IServer>().Use<integration_with_aspnetcore.FakeServer>())
                .ConfigureWebHostDefaults(b => b.UseStartup<Startup>());
            ;
        }
    }

    public class integration_with_aspnetcore
    {
        private readonly ITestOutputHelper _output;

        public integration_with_aspnetcore(ITestOutputHelper output)
        {
            _output = output;
        }


        [Fact]
        public void default_registrations_for_service_provider_factory()
        {
            var container = Container.For(x => x.AddLamar());

            container.Model.DefaultTypeFor<IServiceProviderFactory<ServiceRegistry>>()
                .ShouldBe(typeof(LamarServiceProviderFactory));

            container.Model.DefaultTypeFor<IServiceProviderFactory<IServiceCollection>>()
                .ShouldBe(typeof(LamarServiceProviderFactory));
        }

        [Fact]
        public void integration_with_ef()
        {
            var container = new Container(_ =>
            {
                _.AddDbContext<AppDbContext>(opts => { opts.UseSqlServer("connection string"); });
            });

            container.GetInstance<AppDbContext>().ShouldNotBeNull();

            container.GetInstance<Foo>().Context.ShouldNotBeNull();
        }


        [Fact]
        public void see_singleton_registrations()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Startup>();
            });


            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();

                var singletons = container.Model.AllInstances
                    .Where(x => x.Lifetime == ServiceLifetime.Singleton)
                    .Where(x => !x.ServiceType.IsOpenGeneric())
                    .Where(x => x.Instance is GeneratedInstance);

                foreach (var singleton in singletons)
                    _output.WriteLine($"{singleton.ServiceType.FullNameInCode()} --> {singleton.Instance}");
            }
        }

        [Fact]
        public void use_in_app()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Startup>();
            });

            var failures = new List<Type>();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            long bootstrappingTime;

            long starting;
            long ending;

            using (var host = builder.Start())
            {
                bootstrappingTime = stopwatch.ElapsedMilliseconds;
                var container = host.Services.ShouldBeOfType<Container>();


                var errors = container.Model.AllInstances.Where(x => x.Instance.ErrorMessages.Any())
                    .SelectMany(x => x.Instance.ErrorMessages).ToArray();

                if (errors.Any())
                {
                    throw new Exception(errors.Join(", "));
                }


                starting = stopwatch.ElapsedMilliseconds;
                foreach (var instance in container.Model.AllInstances.Where(x => !x.ServiceType.IsOpenGeneric()))
                    instance.Resolve().ShouldNotBeNull();

                ending = stopwatch.ElapsedMilliseconds;
                stopwatch.Stop();

                var writer = new StringWriter();

                _output.WriteLine(writer.ToString());
                _output.WriteLine(container.WhatDoIHave());
            }

            _output.WriteLine("Bootstrapping: " + bootstrappingTime);
            _output.WriteLine("Building all:  " + (ending - starting));


            if (failures.Any())
            {
                throw new Exception(failures.Select(x => x.FullNameInCode()).Join(Environment.NewLine));
            }
        }

        [Fact]
        public void can_override_registrations_on_host_builder()
        {
            var builder = Host.CreateDefaultBuilder();
            builder
                .UseLamar()

                // This is the override
                .OverrideServices(s => s.For<IServer>().Use<FakeServer>())
                .ConfigureWebHostDefaults(b => b.UseStartup<Startup>());

            using var host = builder.Build();

            host.Services.GetRequiredService<IServer>()
                .ShouldBeOfType<FakeServer>();
        }


        #region sample_usage_of_overrides

        [Fact]
        public void sample_usage_of_overrides()
        {
            var builder = Program
                .CreateHostBuilder(Array.Empty<string>())

                // This is our chance to make service overrides
                .OverrideServices(s => { s.For<IServer>().Use<FakeServer>(); });

            using var host = builder.Build();

            host.Services.GetRequiredService<IServer>()
                .ShouldBeOfType<FakeServer>();
        }

        #endregion


        [Fact]
        public void can_override_registrations()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
                {
                    x.UseServer(new NulloServer());
                    x.UseStartup<Startup>();
                })

                // This is the override
                .OverrideServices(s => s.For<IServer>().Use<FakeServer>());

            using var host = builder.Build();

            host.Services.GetRequiredService<IServer>()
                .ShouldBeOfType<FakeServer>();
        }

        [Fact]
        public void how_do_i_build_with_everything()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Startup>();
            });

            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();


                _output.WriteLine(container.HowDoIBuild());
            }
        }

        [Fact]
        public void bug_103_multithreaded_access_to_options()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Startup>();
            });


            using (var host = builder.Build())
            {
                var container = host.Services.As<Container>();

                var optionTypes = container.Model.AllInstances
                    .Select(x => x.ServiceType)
                    .Where(x => !x.IsOpenGeneric())
                    .Where(x => x.Closes(typeof(IOptions<>)))
                    .ToArray();

                void tryToResolveAll()
                {
                    foreach (var optionType in optionTypes) container.GetInstance(optionType).ShouldNotBeNull();
                }

                var list = new List<Task>();

                for (var i = 0; i < 10; i++)
                {
                    list.Add(Task.Factory.StartNew(tryToResolveAll));
                }

                Task.WaitAll(list.ToArray());
            }
        }

        [Fact]
        public void bug_159_register_service_using_ServiceDescriptor_instance()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Bug159.Startup>();
            });
            

            using (var host = builder.Build())
            {
                var service = host.Services.GetRequiredService<Bug159>();
            }
        }

        [Fact]
        public void can_assert_configuration_is_valid_config_only()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Bug159.Startup>();
            });


            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();


                var errors = container.Model.AllInstances.Where(x => x.Instance.ErrorMessages.Any())
                    .SelectMany(x => x.Instance.ErrorMessages).ToArray();

                if (errors.Any())
                {
                    throw new Exception(errors.Join(", "));
                }


                container.AssertConfigurationIsValid(AssertMode.ConfigOnly);
            }
        }

        [Fact]
        public void can_assert_configuration_is_valid_config_full()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Bug159.Startup>();
            });


            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();


                var errors = container.Model.AllInstances.Where(x => x.Instance.ErrorMessages.Any())
                    .SelectMany(x => x.Instance.ErrorMessages).ToArray();

                if (errors.Any())
                {
                    throw new Exception(errors.Join(", "));
                }


                container.AssertConfigurationIsValid();
            }
        }

        [Fact]
        public void can_assert_configuration_is_valid_with_service_that_requires_IServiceScopeFactory()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
                {
                    x.UseServer(new NulloServer());
                    x.UseStartup<Bug159.Startup>();
                })
                .ConfigureServices(services =>
                {
                    // AddHealthChecks configures DefaultHealthCheckService which depends on IServiceScopeFactory
                    services.AddHealthChecks();
                });

            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();
                var errors = container.Model.AllInstances.Where(x => x.Instance.ErrorMessages.Any())
                    .SelectMany(x => x.Instance.ErrorMessages).ToArray();

                if (errors.Any())
                {
                    throw new Exception(errors.Join(", "));
                }

                container.AssertConfigurationIsValid();
            }
        }

        [Fact]
        public void use_in_app_with_ambigious_references()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Bug159.Startup>();
            }).UseMetrics();

            var failures = new List<Type>();

            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();


                var errors = container.Model.AllInstances.Where(x => x.Instance.ErrorMessages.Any())
                    .SelectMany(x => x.Instance.ErrorMessages).ToArray();

                if (errors.Any())
                {
                    throw new Exception(errors.Join(", "));
                }


                foreach (var instance in container.Model.AllInstances.Where(x => !x.ServiceType.IsOpenGeneric()))
                    instance.Resolve().ShouldNotBeNull();

                //                    try
                //                    {
                //
                //                    }
                //                    catch (Exception e)
                //                    {
                //                        failures.Add(instance.ServiceType);
                //                    }
            }

            if (failures.Any())
            {
                throw new Exception(failures.Select(x => x.FullNameInCode()).Join(Environment.NewLine));
            }
        }


        [Fact]
        public void can_build_resolvers_for_everything()
        {
            var builder = Host.CreateDefaultBuilder().UseLamar().ConfigureWebHostDefaults(x =>
            {
                x.UseServer(new NulloServer());
                x.UseStartup<Bug159.Startup>();
            });


            using (var host = builder.Start())
            {
                var container = host.Services.ShouldBeOfType<Container>();
                var instances = container.Model.AllInstances.Select(x => x.Instance)
                    .Where(x => !x.ServiceType.IsOpenGeneric()).OfType<GeneratedInstance>().ToArray();


                foreach (var instance in instances)
                {
                    _output.WriteLine($"{instance.ServiceType} / {instance.ImplementationType}");
                    instance.BuildFuncResolver(container).ShouldNotBeNull();
                }
            }
        }

        public class Foo
        {
            public Foo(AppDbContext context)
            {
                Context = context;
            }

            public AppDbContext Context { get; }
        }

        public class FakeServer : NulloServer
        {
        }

        public class Bug159
        {
            private Bug159(IMessageMaker msg)
            {
            }

            public class Startup
            {
                public void ConfigureContainer(ServiceRegistry services)
                {
                    services.Add(new ServiceDescriptor(
                        typeof(IMessageMaker),
                        sp => new MessageMaker("Bug159"),
                        ServiceLifetime.Transient)
                    );
                }

                public void Configure(IApplicationBuilder app)
                {
                }
            }
        }
    }

    public class AppDbContext : DbContext
    {
        protected AppDbContext()
        {
        }

        public AppDbContext(DbContextOptions options) : base(options)
        {
        }
    }

    public class NulloServer : IServer
    {
        public void Dispose()
        {
        }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();
    }


    public class CachedStartup
    {
        public void ConfigureContainer(ServiceRegistry services)
        {
            services.AddMvc();
            services.AddLogging();
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryApiResources(Config.GetApiResources())
                .AddInMemoryClients(Config.GetClients());
            services.For<IMessageMaker>().Use(new MessageMaker("Hey there."));

            services.AddHealthChecks();

            services.AddHealthChecksUI();

            services.AddAuthentication()
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = "auth";
                    options.RequireHttpsMetadata = true;
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseIdentityServer();

            app.UseHealthChecks("/hc",
                new HealthCheckOptions
                {
                    Predicate = hc => true
                });

            app.UseHealthChecks("/hc-cache",
                new HealthCheckOptions
                {
                    Predicate = hc => hc.Tags.Contains("cache")
                });

            app.UseHealthChecks("/hc-db",
                new HealthCheckOptions
                {
                    Predicate = hc => hc.Tags.Contains("database")
                });

            app.UseHealthChecks("/hc-domain",
                new HealthCheckOptions
                {
                    Predicate = hc => hc.Tags.Contains("domainservice")
                });


            app.Run(c =>
            {
                var maker = c.RequestServices.GetService<IMessageMaker>();
                return c.Response.WriteAsync(maker.ToString());
            });
        }
    }

    public class Startup
    {
        public void ConfigureContainer(ServiceRegistry services)
        {
            services.AddMvc();
            services.AddLogging();
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryApiResources(Config.GetApiResources())
                .AddInMemoryClients(Config.GetClients());
            services.For<IMessageMaker>().Use(new MessageMaker("Hey there."));

            services.AddAuthentication()
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = "auth";
                    options.RequireHttpsMetadata = true;
                });

            services.AddHttpClient();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseIdentityServer();

            app.Run(c =>
            {
                var maker = c.RequestServices.GetService<IMessageMaker>();
                return c.Response.WriteAsync(maker.ToString());
            });
        }
    }

    public class Config
    {
        public static IEnumerable<ApiResource> GetApiResources()
        {
            return new List<ApiResource>
            {
                new("api1", "My API")
            };
        }

        public static IEnumerable<Client> GetClients()
        {
            return new List<Client>
            {
                new()
                {
                    ClientId = "client",

                    // no interactive user, use the clientid/secret for authentication
                    AllowedGrantTypes = GrantTypes.ClientCredentials,

                    // secret for authentication
                    ClientSecrets =
                    {
                        new Secret("secret".Sha256())
                    },

                    // scopes that client has access to
                    AllowedScopes =
                    {
                        "api1"
                    }
                }
            };
        }
    }

    public interface IMessageMaker
    {
    }

    public class MessageMaker : IMessageMaker
    {
        private readonly string _message;

        public MessageMaker(string message)
        {
            _message = message;
        }

        public override string ToString()
        {
            return _message;
        }
    }
}