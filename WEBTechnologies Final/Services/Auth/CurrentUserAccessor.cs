using System.Security.Claims;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Auth
{
    /// <summary>
    /// Resolves "who is calling" from whichever credential the caller actually presented.
    ///
    /// The website authenticates with a server-side session cookie; the mobile app presents a
    /// JWT bearer token. Both populate the same three facts, so controllers and services do not
    /// need to care which client they are serving.
    /// </summary>
    public interface ICurrentUser
    {
        int? UserId { get; }
        string? Username { get; }
        bool IsAdmin { get; }
        bool IsAuthenticated { get; }
    }

    public class CurrentUserAccessor : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public CurrentUserAccessor(IHttpContextAccessor accessor) => _accessor = accessor;

        private HttpContext? Ctx => _accessor.HttpContext;

        public int? UserId
        {
            get
            {
                var claim = Ctx?.User?.FindFirst(TokenService.SubClaim)?.Value
                            ?? Ctx?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(claim, out var fromToken)) return fromToken;

                var fromSession = Ctx?.Session?.GetInt32(SessionKeys.UserId);
                return fromSession;
            }
        }

        public string? Username
        {
            get
            {
                var claim = Ctx?.User?.FindFirst(TokenService.NameClaim)?.Value
                            ?? Ctx?.User?.FindFirst(ClaimTypes.Name)?.Value;
                if (!string.IsNullOrEmpty(claim)) return claim;

                return Ctx?.Session?.GetString(SessionKeys.Username);
            }
        }

        public bool IsAdmin
        {
            get
            {
                var role = Ctx?.User?.FindFirst(TokenService.RoleClaim)?.Value
                           ?? Ctx?.User?.FindFirst(ClaimTypes.Role)?.Value;
                if (string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)) return true;

                return Ctx?.Session?.GetString(SessionKeys.IsAdmin) == "true";
            }
        }

        public bool IsAuthenticated => UserId is not null;
    }
}
