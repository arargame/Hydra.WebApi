using Hydra.Core;
using Hydra.IdentityAndAccess;
using Hydra.Services;
using Hydra.Services.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Hydra.WebApi.Services
{
    /// <summary>
    /// Yüklü controller'ları gezip her uç için eksik olan <see cref="Permission"/> satırlarını
    /// oluşturur.
    ///
    /// Neden gerekli: sistem deny-by-default. Bir ucu bir role vermek için önce o ucu tarif eden
    /// bir Permission kaydının VAR olması gerekiyor — ve bunları elle açmak, on beş controller ×
    /// sekiz action ölçeğinde sürdürülebilir değil. Üstelik elle yazılan bir "Action" değeri
    /// gerçek route'dan bir harf saparsa izin sessizce hiçbir şeyi açmaz; kimse de fark etmez.
    ///
    /// Tarayıcı bu iki sorunu birden çözüyor: isimler route'un kendisinden okunduğu için
    /// PermissionEvaluator'ın karşılaştırdığı değerlerle birebir aynı oluyor.
    ///
    /// Idempotent: her açılışta çalışır, sadece EKSİK olanları ekler. Var olan kayıtlara, elle
    /// yapılmış düzenlemelere ve rol bağlantılarına dokunmaz.
    /// </summary>
    public static class EndpointPermissionScanner
    {
        /// <summary>
        /// Kapatma anahtarı. Üretimde izin listesinin dışarıdan yönetildiği bir kurulumda
        /// "Hydra:Permissions:AutoDiscover": false ile kapatılabilir. Varsayılan açık — çünkü
        /// eksik izin kaydı, sessizce erişilemeyen bir ekran demek.
        /// </summary>
        public const string AutoDiscoverConfigKey = "Hydra:Permissions:AutoDiscover";

        public static async Task<int> SyncAsync(IServiceProvider serviceProvider, params Assembly[] additionalAssemblies)
        {
            using var scope = serviceProvider.CreateScope();

            var services = scope.ServiceProvider;

            var logService = services.GetService<ILogService>();

            try
            {
                var configuration = services.GetRequiredService<ICustomConfigurationService>();

                if (string.Equals(configuration.Get(AutoDiscoverConfigKey, "true"), "false", StringComparison.OrdinalIgnoreCase))
                    return 0;

                var permissionService = services.GetRequiredService<IService<Permission>>();

                var existing = await permissionService.SelectThenCache(p => p.Type == PermissionType.ControllerActionBased);

                //Aynı uç iki kez eklenmesin diye "Controller/Action" anahtarıyla karşılaştırıyoruz;
                //Name'e bakmak yetmez, çünkü Name elle değiştirilmiş olabilir.
                var known = new HashSet<string>(
                    existing.Where(p => !string.IsNullOrWhiteSpace(p.Controller) && !string.IsNullOrWhiteSpace(p.Action))
                            .Select(p => $"{p.Controller}/{p.Action}"),
                    StringComparer.OrdinalIgnoreCase);

                var discovered = Discover(additionalAssemblies);

                var created = 0;

                foreach (var endpoint in discovered)
                {
                    var key = $"{endpoint.Controller}/{endpoint.Action}";

                    if (!known.Add(key))
                        continue;

                    var permission = new Permission
                    {
                        Name = key,
                        Type = PermissionType.ControllerActionBased,
                        Controller = endpoint.Controller,
                        Action = endpoint.Action,
                        AllowAnonymous = false,
                        Enabled = true,
                        IsActive = true,
                        Description = "Auto-discovered endpoint.",
                        AddedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now
                    };

                    var response = await permissionService.CreateAsync(permission);

                    if (response?.Success == true)
                        created++;
                }

                if (created > 0 && logService is not null)
                {
                    await logService.SaveAsync(
                        LogFactory.Info("Startup", "PermissionScan", $"Discovered {discovered.Count} endpoint(s), created {created} new permission(s)."),
                        LogRecordType.Console);
                }

                return created;
            }
            catch (Exception ex)
            {
                //Tarama bir aksilikte uygulamayı düşürmemeli: izinler eksik kalır, sistem yine
                //ayakta olur ve log'da neden yazar.
                if (logService is not null)
                {
                    await logService.SaveAsync(LogFactory.Error($"Permission scan failed: {ex.Message}"), LogRecordType.Console);
                }

                return 0;
            }
        }

        private sealed record EndpointInfo(string Controller, string Action);

        private static List<EndpointInfo> Discover(Assembly[] additionalAssemblies)
        {
            var assemblies = new List<Assembly> { typeof(EndpointPermissionScanner).Assembly };

            assemblies.AddRange(additionalAssemblies ?? Array.Empty<Assembly>());

            var endpoints = new HashSet<EndpointInfo>();

            foreach (var controllerType in assemblies.Distinct().SelectMany(GetTypesSafely).Where(IsController))
            {
                // "RoleController" → "Role": PermissionEvaluator'ın route'tan okuduğu değerle aynı.
                var controllerName = controllerType.Name.EndsWith("Controller", StringComparison.Ordinal)
                    ? controllerType.Name[..^"Controller".Length]
                    : controllerType.Name;

                //Sınıf düzeyinde [AllowAnonymous] varsa hiçbir ucu izin istemiyor (ör. ClientLog).
                if (controllerType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null)
                    continue;

                foreach (var method in GetActionMethods(controllerType))
                {
                    if (method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null)
                        continue;

                    endpoints.Add(new EndpointInfo(controllerName, GetActionName(method)));
                }
            }

            return endpoints.OrderBy(e => e.Controller).ThenBy(e => e.Action).ToList();
        }

        private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t is not null)!;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static bool IsController(Type type)
            => type.IsClass
               && !type.IsAbstract
               && !type.IsGenericTypeDefinition
               && typeof(ControllerBase).IsAssignableFrom(type);

        /// <summary>
        /// MainController&lt;T&gt;'den MİRAS alınan action'lar da dahil (Create, Update, Select...) —
        /// DeclaredOnly kullanılsaydı generic CRUD uçlarının hiçbiri keşfedilmezdi ve tam da
        /// korunması gereken uçlar listede olmazdı.
        /// </summary>
        private static IEnumerable<MethodInfo> GetActionMethods(Type controllerType)
            => controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => !m.IsSpecialName
                                         && !m.IsDefined(typeof(NonActionAttribute), inherit: true)
                                         && m.DeclaringType != typeof(object)
                                         && m.DeclaringType != typeof(ControllerBase)
                                         && typeof(ControllerBase).IsAssignableFrom(m.DeclaringType));

        /// <summary>
        /// Action adı: [ActionName] varsa o, yoksa metot adı — ve "Async" soneki kırpılır.
        /// ASP.NET Core varsayılan olarak (SuppressAsyncSuffixInActionNames = true) böyle
        /// davranıyor, yani route'taki değer "LoginAsync" değil "Login". Burada kırpılmazsa
        /// üretilen izin hiçbir isteğe eşleşmez.
        /// </summary>
        private static string GetActionName(MethodInfo method)
        {
            var explicitName = method.GetCustomAttribute<ActionNameAttribute>(inherit: true)?.Name;

            if (!string.IsNullOrWhiteSpace(explicitName))
                return explicitName!;

            var name = method.Name;

            return name.EndsWith("Async", StringComparison.Ordinal) && name.Length > "Async".Length
                ? name[..^"Async".Length]
                : name;
        }
    }
}
