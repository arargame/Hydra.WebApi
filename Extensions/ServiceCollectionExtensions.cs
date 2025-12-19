using Hydra.AccessManagement;
using Hydra.AccessManagement.Jwt;
using Hydra.CacheManagement.Managers;
using Hydra.DAL.Core;
using Hydra.DBAccess;
using Hydra.DI;
using Hydra.DTOs;
using Hydra.DTOs.ViewDTOs;
using Hydra.IdentityAndAccess;
using Hydra.Services;
using Hydra.Services.Cache;
using Hydra.Services.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Text;

namespace Hydra.WebApi.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddHydraDependencies(this IServiceCollection services, IConfiguration configuration, params Assembly[] additionalAssemblies)
        {
            services.AddScoped<IControllerInjector, ControllerInjector>();

            services.AddScoped<IServiceFactory, ServiceFactory>();

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddScoped<ISessionContext, SessionContext>();

            AddDataAccessLayerDependencies(services, additionalAssemblies);

            AddBusinessLayerDependencies(services);

            AddCacheDependencies(services);

            services.AddScoped<Func<string, MsSqlConnection>>(provider => name =>
            {
                var config = provider.GetRequiredService<ICustomConfigurationService>();

                var connStr = config.Get(name);

                return (MsSqlConnection)ConnectionFactory.CreateConnection(ConnectionType.MsSql, connStr);
            });

            services.AddScoped<ITableService, TableService>(provider =>
            {
                var config = provider.GetRequiredService<ICustomConfigurationService>();

                var defaultConnStr = config.Get("DefaultConnection");

                var connection = ConnectionFactory.CreateConnection(ConnectionType.MsSql, defaultConnStr);

                return new TableService(connection);
            });

            ViewDTORegistryLoader.LoadAllViewDTOs(typeof(ViewDTO).Assembly,Assembly.GetExecutingAssembly());


            return services;
        }

        public static void AddCustomServicesByAttribute<TAttribute>(this IServiceCollection services, params Assembly[] assemblies)
            where TAttribute : Attribute
        {
            var allTypesWithTAttribute = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetCustomAttribute<TAttribute>() != null)
                .ToList();


            foreach (var implType in allTypesWithTAttribute)
            {
                var attr = implType.GetCustomAttribute<TAttribute>();

                Type? interfaceType = null;

                if (attr is RegisterAsServiceAttribute serviceAttr)
                    interfaceType = serviceAttr.ServiceInterface ?? implType.GetInterfaces().FirstOrDefault();

                else if (attr is RegisterAsRepositoryAttribute repoAttr)
                    interfaceType = repoAttr.RepositoryInterface ?? implType.GetInterfaces().FirstOrDefault();

                if (interfaceType != null)
                {
                    if (services.Any(s => s.ServiceType == interfaceType))
                        throw new InvalidOperationException($"Duplicate service registration for {interfaceType.Name}");

                    services.AddScoped(interfaceType, implType);
                }
            }
        }

        private static void AddDataAccessLayerDependencies(IServiceCollection services, Assembly[] additionalAssemblies)
        {
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            //services.AddScoped<DbContext, YourDbContext>();

            services.AddScoped<EfCoreDatabaseService>();

            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            services.AddCustomServicesByAttribute<RegisterAsRepositoryAttribute>(typeof(Repository<>).Assembly, Assembly.GetExecutingAssembly());

            // services.AddScoped<IRepositoryFactoryService, RepositoryFactoryService>(); // Removed to prevent DI validation error

            services.AddScoped<IRepositoryFactoryService>(sp =>
            {
                var assembliesToScan = new[]
                {
                    typeof(Repository<>).Assembly,
                    Assembly.GetExecutingAssembly()
                }.Concat(additionalAssemblies).ToArray();

                return new RepositoryFactoryService(
                    sp,
                    assembliesToScan
                );
            });
        }

        private static void AddBusinessLayerDependencies(IServiceCollection services)
        {
            services.AddSingleton<ISecretManager, LocalSecretManager>();
            services.AddSingleton<ICustomConfigurationService, CustomConfigurationService>();

            //services.AddSingleton<ILogDbWriterService, LogDbWriterService>();
            services.AddScoped<ILogDbWriterService>(provider =>
            {
                var connectionFactory = provider.GetRequiredService<Func<string, MsSqlConnection>>();
                var connection = connectionFactory("LogDbConnection");

                return new LogDbWriterService(connection);
            });
            services.AddScoped<ILogService, LogService>();

            services.AddScoped<ServiceInjector>();
            services.AddScoped(typeof(IService<>), typeof(Service<>));
            services.AddCustomServicesByAttribute<RegisterAsServiceAttribute>(typeof(Service<>).Assembly, Assembly.GetExecutingAssembly());
        }

        private static void AddCacheDependencies(IServiceCollection services)
        {
            services.AddMemoryCache();

            //services.AddSingleton<MemoryCacheService<Guid, SessionInformation>>(sp =>
            //        new MemoryCacheService<Guid, SessionInformation>(
            //            sp.GetRequiredService<IMemoryCache>(),
            //            TimeSpan.FromMinutes(30)));


            services.AddSingleton(provider =>
                new SessionInformationCacheManager(
                    provider.GetRequiredService<IMemoryCache>(),
                    TimeSpan.FromMinutes(30)));

            //// SessionInformation → MemoryCache
            //services.AddSingleton<ICacheService<Guid, SessionInformation>>(sp =>
            //{
            //    var memoryCache = sp.GetRequiredService<IMemoryCache>();
            //    return new MemoryCacheService<Guid, SessionInformation>(memoryCache, TimeSpan.FromMinutes(30));
            //});

            // SystemUser → LRUCache
            services.AddSingleton<IQueryableCacheService<Guid, SystemUser>>(
                sp => new LRUCacheService<Guid, SystemUser>(capacity: 500));
            services.AddSingleton<ICacheService<Guid, SystemUser>>(
                sp => sp.GetRequiredService<IQueryableCacheService<Guid, SystemUser>>());

            // Role → LRUCache
            services.AddSingleton<IQueryableCacheService<Guid, Role>>(
                sp => new LRUCacheService<Guid, Role>(capacity: 500));
            services.AddSingleton<ICacheService<Guid, Role>>(
                sp => sp.GetRequiredService<IQueryableCacheService<Guid, Role>>());

            // Permission → LRUCache
            services.AddSingleton<IQueryableCacheService<Guid, Permission>>(
                sp => new LRUCacheService<Guid, Permission>(capacity: 500));
            services.AddSingleton<ICacheService<Guid, Permission>>(
                sp => sp.GetRequiredService<IQueryableCacheService<Guid, Permission>>());

            // RolePermission → LRUCache
            services.AddSingleton<IQueryableCacheService<Guid, RolePermission>>(
                sp => new LRUCacheService<Guid, RolePermission>(capacity: 500));
            services.AddSingleton<ICacheService<Guid, RolePermission>>(
                sp => sp.GetRequiredService<IQueryableCacheService<Guid, RolePermission>>());

            // RoleSystemUser → LRUCache
            services.AddSingleton<IQueryableCacheService<Guid, RoleSystemUser>>(
                sp => new LRUCacheService<Guid, RoleSystemUser>(capacity: 500));
            services.AddSingleton<ICacheService<Guid, RoleSystemUser>>(
                sp => sp.GetRequiredService<IQueryableCacheService<Guid, RoleSystemUser>>());

            // SystemUserPermission → LRUCache
            services.AddSingleton<IQueryableCacheService<Guid, SystemUserPermission>>(
                sp => new LRUCacheService<Guid, SystemUserPermission>(capacity: 500));
            services.AddSingleton<ICacheService<Guid, SystemUserPermission>>(
                sp => sp.GetRequiredService<IQueryableCacheService<Guid, SystemUserPermission>>());
        }

        private static void JwtDependencies(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IJwtTokenManager, JwtTokenManager>();

            services.AddAuthentication("Bearer")
                    .AddJwtBearer("Bearer", options =>
                    {
                        var secretKey = configuration["Secrets:JwtSecretKey"];
                        var key = Encoding.UTF8.GetBytes(secretKey ?? throw new Exception("JwtSecretKey missing"));

                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidIssuer = "hydra-api",
                            ValidAudience = "hydra-clients",
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(key)
                        };
                    });

            services.AddAuthorization();
        }
    }
}
