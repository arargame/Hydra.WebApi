using Hydra.AccessManagement;
using Hydra.AccessManagement.Authorization;
using Hydra.AccessManagement.Jwt;
using Hydra.CacheManagement.Managers;
using Hydra.Core;
using Hydra.DI;
using Hydra.DTOs.ModelDTOs.SystemUserDTO;
using Hydra.Http;
using Hydra.IdentityAndAccess;
using Hydra.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq.Expressions;
using System.Security.Claims;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemUserController : MainController<SystemUser>
    {
        private readonly SessionInformationCacheManager _cacheManager;

        private readonly ILogService _logService;

        private readonly IJwtTokenManager _jwtTokenManager;

        private readonly IPermissionEvaluator _permissionEvaluator;

        public SystemUserController(IControllerInjector controllerInjector,
                                    SessionInformationCacheManager cacheManager,
                                    ILogService logService,
                                    IJwtTokenManager jwtTokenManager,
                                    IPermissionEvaluator permissionEvaluator) : base(controllerInjector)
        {
            _cacheManager = cacheManager;

            _logService = logService;

            //Bu ikisi daha önce alanlar olarak tanımlıydı ama constructor'da HİÇ atanmıyordu.
            //Sonuç: kullanıcı bulunduğunda GenerateToken bir NullReferenceException atıyor,
            //catch bloğu bunu yutuyor ve login "credentials do not match" gibi görünüyordu.
            _jwtTokenManager = jwtTokenManager;

            _permissionEvaluator = permissionEvaluator;
        }

        [HttpPost]
        [Route("Login")]
        [AllowAnonymous]
        public async Task<JsonResult> LoginAsync([FromBody] LoginViewDTO dto)
        {
            var response = new ResponseObject().SetActionName("Login").UseDefaultMessages();

            var dtoUserNameOrEmailAddress = dto.UserNameOrEmailAddress?.Trim();

            Expression<Func<SystemUser, bool>> expression = su =>
                (su.Email == dtoUserNameOrEmailAddress || su.Name == dtoUserNameOrEmailAddress)
                && su.IsActive;

            try
            {
                var user = string.IsNullOrWhiteSpace(dtoUserNameOrEmailAddress)
                    ? null
                    : await Service.GetAsync(expression);

                //Şifre doğrulaması. Bu kontrol daha önce HİÇ yoktu: kullanıcı adı tutan herkes,
                //şifre alanı boş bile olsa içeri girebiliyordu (Academy 5.4'te belgelenen açık).
                //PasswordHasher.Verify, Default Admin seed'inin ürettiği PBKDF2 formatını okur.
                var passwordIsValid = user != null && PasswordHasher.Verify(dto.Password ?? string.Empty, user.PasswordHash);

                if (user != null && passwordIsValid)
                {
                    var service = Service as SystemUserService;

                    service?.CacheService?.Add(user.Id, user);

                    var roles = await GetRolesSafeAsync(service, user.Id);

                    var permissions = await _permissionEvaluator.GetEffectivePermissionsAsync(user.Id);

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                        new Claim(ClaimTypes.Name, user.Name ?? user.Email ?? string.Empty)
                    };

                    //Rol ADI standart claim'e gider (Authorize(Roles="Admin") ve AuthorizeView
                    //bunu okur), rol Id'si ayrı bir Hydra claim'inde korunur.
                    foreach (var role in roles)
                    {
                        if (!string.IsNullOrWhiteSpace(role.Name))
                            claims.Add(new Claim(ClaimTypes.Role, role.Name));

                        claims.Add(new Claim(HydraClaimTypes.RoleId, role.Id.ToString()));
                    }

                    //Etkin izinler (rolden gelenler + doğrudan verilenler). Önceden izinler
                    //çekiliyor ama token'a hiç konmuyordu.
                    foreach (var permission in permissions)
                    {
                        if (!string.IsNullOrWhiteSpace(permission.Name))
                            claims.Add(new Claim(HydraClaimTypes.Permission, permission.Name));

                        //Ayrıca "hangi ucu açıyor" bilgisi: istemci menüyü buna göre gizliyor.
                        //Sadece ControllerActionBased izinler — sunucudaki PermissionEvaluator'ın
                        //uç kararında dikkate aldığı tipin AYNISI, ki menü ile API aynı şeyi söylesin.
                        if (permission.Type == PermissionType.ControllerActionBased)
                        {
                            var controller = string.IsNullOrWhiteSpace(permission.Controller) ? HydraClaimTypes.Wildcard : permission.Controller;
                            var action = string.IsNullOrWhiteSpace(permission.Action) ? HydraClaimTypes.Wildcard : permission.Action;

                            claims.Add(new Claim(HydraClaimTypes.Endpoint, $"{controller}/{action}"));
                        }
                    }

                    var token = _jwtTokenManager.GenerateToken(claims);

                    dto.Id = user.Id;
                    dto.EmailAddress = user.Email;
                    dto.UserName = user.Name;

                    //Token'ın istemciye ULAŞMASI gerekiyor: AuthenticationService dönen DTO'daki
                    //Token alanını okuyup LocalStorage'a yazıyor. Bu atama önceden yoktu, yani
                    //üretilen token hiçbir zaman istemciye gitmiyordu.
                    dto.Token = token;
                    dto.JwtToken = token;

                    //Gönderilen şifreyi geri yansıtma.
                    dto.Password = null;

                    response.SetData(dto)
                            .SetSuccess(true)
                            .SetStatusCode(200);

                    _cacheManager.Login(SessionInformation);
                }
                else
                {
                    //Kullanıcı yok ile şifre yanlış AYNI cevabı döner: hangisinin olduğunu
                    //söylemek, saldırgana geçerli kullanıcı adlarını sayma imkânı verir.
                    dto.Password = null;

                    response.SetSuccess(false)
                            .SetStatusCode(401)
                            .AddExtraMessage(new ResponseObjectMessage(title: "Login Attempt Failed",
                                                                        text: "Your credentials(Username or Password or Token) do not match our records",
                                                                        showWhenSuccess: false));
                }
            }
            catch (Exception ex)
            {
                dto.Password = null;

                await SafeLogAsync(LogFactory.Error($"Login failed for '{dtoUserNameOrEmailAddress}': {ex.Message}"));

                response.SetSuccess(false)
                        .SetStatusCode(500)
                        .AddExtraMessage(new ResponseObjectMessage(title: "Login Attempt Failed",
                                                                    text: HydraStatusCatalog.Get(500).UserMessage,
                                                                    showWhenSuccess: false));
            }

            return new JsonResult(response) { StatusCode = response.StatusCode ?? 200 };
        }

        [HttpPost("logout")]
        [HydraAllowAuthenticated]
        public async Task<JsonResult> Logout([FromHeader(Name = "X-User-Id")] Guid userId)
        {
            await _logService.SaveAsync(LogFactory.Info(category: Name,
                                                        name: ActionName,
                                                        description: "Logout Attempt",
                                                        entityId: userId.ToString(),
                                                        processType: LogProcessType.Logout,
                                                        sessionInformation: SessionInformation),
                                        LogRecordType.Database);

            var response = new ResponseObject()
                    .SetActionName(ActionName)
                    .SetSuccess(_cacheManager.Logout(userId))
                    .UseDefaultMessages();


            return new JsonResult(response);
        }

        private static async Task<List<Role>> GetRolesSafeAsync(SystemUserService? service, Guid userId)
        {
            if (service is null)
                return new List<Role>();

            return await service.GetRolesAsync(userId);
        }

        private async Task SafeLogAsync(Log log)
        {
            try
            {
                await _logService.SaveAsync(log, LogRecordType.Console);
            }
            catch
            {
                // Logging must never turn a failed login into a crash.
            }
        }
    }

}
