using Hydra.AccessManagement;
using Hydra.AccessManagement.Jwt;
using Hydra.CacheManagement.Managers;
using Hydra.Core;
using Hydra.DI;
using Hydra.DTOs.ModelDTOs.SystemUserDTO;
using Hydra.Http;
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

        public SystemUserController(IControllerInjector controllerInjector, SessionInformationCacheManager cacheManager,ILogService logService) : base(controllerInjector)
        {
            _cacheManager = cacheManager;

            _logService = logService;
        }

        [HttpPost]
        [Route("Login")]
        [AllowAnonymous]
        public async Task<JsonResult> LoginAsync([FromBody] LoginViewDTO dto)
        {
            var response = new ResponseObject().UseDefaultMessages();

            var dtoUserNameOrEmailAddress = dto.UserNameOrEmailAddress?.Trim();

            //if (string.IsNullOrWhiteSpace(dto.UserNameOrEmailAddress))
            //    return BadRequest("Username or Email is required.");

            Expression<Func<SystemUser, bool>> expression = su =>
                (su.Email == dto.UserNameOrEmailAddress || su.Name == dto.UserNameOrEmailAddress)
                && su.IsActive;

            try
            {
                var user = await Service.GetAsync(expression);

                if (user != null)
                {
                    var service = Service as SystemUserService;

                    service.CacheService.Add(user.Id,user);

                    //systemuserservice içerisinde Add ler vs yap hascacheler içnide düşün

                    var roles = await service.GetRolesAsync(user.Id);

                    var permission = await service.GetPermissionsAsync(user.Id);

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(ClaimTypes.Name, user.Name)
                    };

                    roles.ForEach(role => claims.Add(new Claim(ClaimTypes.Role, role.Id.ToString())));

                    var token = _jwtTokenManager.GenerateToken(claims);
                    //time from configuration

                    dto.Id = user.Id;
                    dto.EmailAddress = user.Email;
                    dto.UserName = user.Name;

                    response.SetData(dto)
                            .SetSuccess(true);

                    _cacheManager.Login(SessionInformation);
                }
                else
                {
                    response.SetSuccess(false)
                            .AddExtraMessage(new ResponseObjectMessage(title: "Login Attempt Failed",
                                                                        text: "Your credentials(Username or Password or Token) do not match our records",
                                                                        showWhenSuccess: false));
                }
            }
            catch (Exception ex)
            {
                response.SetSuccess(false)
                        .AddExtraMessage(new ResponseObjectMessage(title: "Login Attempt Failed",
                                                                    text: ex.Message,
                                                                    showWhenSuccess: false));

            }

            _cacheManager.Login(SessionInformation);

            return new JsonResult(response);
        }

        [HttpPost("logout")]
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
    }

}
