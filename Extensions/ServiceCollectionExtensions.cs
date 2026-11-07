using Hydra.AccessManagement;
using Hydra.AccessManagement.Authorization;
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
using Hydra.WebApi.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

            AddBusinessLayerDependencies(services, additionalAssemblies);

            AddCacheDependencies(services);

            //Kimlik doğrulama (JWT bearer) ve yetkilendirme (permission filtresi).
            //Bu çağrı önceden YOKTU: JwtDependencies yazılmış ama hiçbir yerden çağrılmıyordu,
            //yani üretilen token'ı doğrulayacak bir şema hiç kayıtlı değildi (bkz. Academy 5.4).
            AddAuthenticationAndAuthorization(services, configuration);

            services.AddScoped<Func<string, MsSqlConnection>>(provider => name =>
            {
                var config = provider.GetRequiredService<ICustomConfigurationService>();

                var connStr = config.Get(name);

                return (MsSqlConnection)ConnectionFactory.CreateConnection(ConnectionType.MsSql, connStr);
            });

            services.AddScoped<ITableService, TableService>(provider =>
            {
                var config = provider.GetRequiredService<ICustomConfigurationService>();

                var connectionFactory = provider.GetRequiredService<Func<string, MsSqlConnection>>();

                var defaultConnStr = config.Get("DefaultConnection");

                var connection = ConnectionFactory.CreateConnection(ConnectionType.MsSql, defaultConnStr);

                //Tablo bazında bağlantı yönlendirmesi: bazı tablolar (ör. Log) ayrı bir
                //veritabanında yaşar. Eşleme appsettings'ten okunur:
                //  "Hydra": { "TableConnections": { "Log": "LogDbConnection" } }
                //Eşleme tanımlı değilse varsayılan bağlantı kullanılır.
                return new TableService(
                    defaultConnection: connection,
                    connectionByName: name => connectionFactory(name),
                    connectionNameForTable: tableName => config.Get($"Hydra:TableConnections:{tableName}"));
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

                    //SOMUT tip de kayıtlı olmalı. Servisler birbirini somut tip üzerinden
                    //çağırıyor (ör. SystemUserService → _injector.GetServiceLazy<RoleSystemUserService>()),
                    //ServiceFactory ise GetRequiredService<T>() yapıyor. Sadece arayüz kaydı
                    //olduğunda bu çağrılar "No service for type 'RoleSystemUserService'" ile
                    //patlıyordu — login ve izin zinciri bu yüzden hiç çalışamamıştı.
                    if (!services.Any(s => s.ServiceType == implType))
                        services.AddScoped(implType);

                    //Arayüz AYNI scoped örneğe yönlendiriliyor. İki bağımsız kayıt yapılsaydı
                    //aynı istek içinde iki ayrı örnek oluşur, servislerin LRU cache'leri
                    //birbirinden habersiz olurdu.
                    services.AddScoped(interfaceType, sp => sp.GetRequiredService(implType));
                }
            }
        }

        private static void AddDataAccessLayerDependencies(IServiceCollection services, Assembly[] additionalAssemblies)
        {
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            //services.AddScoped<DbContext, YourDbContext>();

            services.AddScoped<EfCoreDatabaseService>();

            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

            var assembliesToScan = new[] { typeof(Repository<>).Assembly, Assembly.GetExecutingAssembly() }.Concat(additionalAssemblies).ToArray();
            services.AddCustomServicesByAttribute<RegisterAsRepositoryAttribute>(assembliesToScan);

            // services.AddScoped<IRepositoryFactoryService, RepositoryFactoryService>(); // Removed to prevent DI validation error

            services.AddScoped<IRepositoryFactoryService>(sp =>
            {
                return new RepositoryFactoryService(
                    sp,
                    assembliesToScan
                );
            });
        }

        private static void AddBusinessLayerDependencies(IServiceCollection services, Assembly[] additionalAssemblies)
        {
            services.AddSingleton<ISecretManager, LocalSecretManager>();
            services.AddSingleton<ICustomConfigurationService, CustomConfigurationService>();

            services.AddScoped<FileLogWriterService>();

            //services.AddSingleton<ILogDbWriterService, LogDbWriterService>();
            services.AddScoped<ILogDbWriterService>(provider =>
            {
                var connectionFactory = provider.GetRequiredService<Func<string, MsSqlConnection>>();
                var connection = connectionFactory("LogDbConnection");

                return new LogDbWriterService(connection);
            });
            services.AddScoped<ILogService, LogService>();

            //Çoka-çok köprü kayıtlarını kuran/koparan generic servis (LinkToCollection uçları).
            services.AddScoped<ICollectionLinkService, CollectionLinkService>();

            services.AddScoped<ServiceInjector>();
            services.AddScoped(typeof(IService<>), typeof(Service<>));

            var assembliesToScan = new[] { typeof(Service<>).Assembly, Assembly.GetExecutingAssembly() }.Concat(additionalAssemblies).ToArray();
            services.AddCustomServicesByAttribute<RegisterAsServiceAttribute>(assembliesToScan);
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

        /// <summary>
        /// Registers the JWT bearer scheme (so issued tokens are actually validated) and the
        /// global deny-by-default permission filter.
        ///
        /// The signing key is resolved exactly the way <see cref="JwtTokenManager"/> resolves it —
        /// secret store first, then configuration — because the issuing side and the validating
        /// side signing with different keys is a failure mode that only shows up at runtime, as
        /// "every token is invalid" with no explanation.
        ///
        /// A missing key no longer throws at startup: it falls back to the clearly-labelled
        /// development key. Refusing to boot because a dev machine has no secret configured is not
        /// safety, it is just a broken F5.
        /// </summary>
        private static void AddAuthenticationAndAuthorization(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IJwtTokenManager, JwtTokenManager>();

            services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();

            var secretKey = configuration[$"Secrets:{JwtTokenManager.SecretKeyConfigName}"]
                            ?? configuration[JwtTokenManager.SecretKeyConfigName];

            if (string.IsNullOrWhiteSpace(secretKey))
                secretKey = JwtTokenManager.DevelopmentFallbackSecret;

            var key = Encoding.UTF8.GetBytes(secretKey);

            services.AddAuthentication("Bearer")
                    .AddJwtBearer("Bearer", options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidIssuer = JwtTokenManager.Issuer,
                            ValidAudience = JwtTokenManager.Audience,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(key)
                        };
                    });

            services.AddAuthorization();

            //Global filtre. Configure<MvcOptions> kullanılıyor (AddControllers(...) yerine) çünkü
            //AddControllers'ı tüketen uygulama kendi Program.cs'inde çağırıyor — böylece çağrı
            //sırasından bağımsız olarak filtre eklenmiş oluyor.
            services.Configure<MvcOptions>(options =>
            {
                options.Filters.Add<PermissionAuthorizationFilter>();
            });
        }
    }
}
