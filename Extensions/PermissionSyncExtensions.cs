using Hydra.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using System;
using System.Reflection;

namespace Hydra.WebApi.Extensions
{
    public static class PermissionSyncExtensions
    {
        /// <summary>
        /// Controller'ları tarayıp eksik uç izinlerini veritabanına yazar.
        ///
        /// <c>DbInitializer.InitializeAsync</c>'ten SONRA çağrılmalı: tablolar oluşmadan izin
        /// yazılamaz. Tüketen uygulamanın kendi controller'ları varsa assembly'sini parametre
        /// olarak geçin (Hydra.WebApi'nin kendi controller'ları zaten taranıyor).
        ///
        /// <code>
        /// Hydra.Services.DbInitializer.InitializeAsync&lt;TentacleDbContext&gt;(app.Services, app.Configuration);
        /// app.SyncHydraPermissions();
        /// </code>
        ///
        /// Idempotent — her açılışta güvenle çalışır, yalnızca yeni uçları ekler.
        /// </summary>
        public static IApplicationBuilder SyncHydraPermissions(this IApplicationBuilder app, params Assembly[] additionalAssemblies)
        {
            //Senkron olarak bekleniyor: uygulama istek almaya başlamadan önce izin listesinin
            //tamam olması gerekiyor, aksi hâlde ilk gelen istekler eksik izinle değerlendirilir.
            EndpointPermissionScanner.SyncAsync(app.ApplicationServices, additionalAssemblies)
                                     .GetAwaiter()
                                     .GetResult();

            return app;
        }
    }
}
