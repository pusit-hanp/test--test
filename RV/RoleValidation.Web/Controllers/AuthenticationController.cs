using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RoleValidation.Web.Authentication;

namespace RoleValidation.Web.Controllers;

[Route("authentication")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthenticationController : Controller
{
    private readonly CompanyLoginChallengeService _challengeService;
    private readonly CompanyLoginStateProtector _stateProtector;
    private readonly AuthenticationFlowService _flowService;
    private readonly CompanyLoginOptions _options;

    public AuthenticationController(
        CompanyLoginChallengeService challengeService,
        CompanyLoginStateProtector stateProtector,
        AuthenticationFlowService flowService,
        CompanyLoginOptions options)
    {
        _challengeService = challengeService
            ?? throw new ArgumentNullException(nameof(challengeService));
        _stateProtector = stateProtector
            ?? throw new ArgumentNullException(nameof(stateProtector));
        _flowService = flowService
            ?? throw new ArgumentNullException(nameof(flowService));
        _options = options
            ?? throw new ArgumentNullException(nameof(options));
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        base.OnActionExecuting(context);
    }

    [AllowAnonymous]
    [HttpGet("challenge")]
    public IActionResult Begin(string? returnUrl = null)
    {
        string? safeReturnUrl = CompanyLoginStateProtector.IsLocalReturnUrl(returnUrl)
            ? returnUrl
            : Request.PathBase.Add("/").Value;
        CompanyLoginChallenge challenge = _challengeService.Create(safeReturnUrl);
        Response.Cookies.Append(
            _options.CorrelationCookieName,
            CreateCorrelationCookieValue(challenge),
            CreateCorrelationCookieOptions());

        return Redirect(challenge.RedirectUrl);
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        string? upar,
        CancellationToken cancellationToken = default)
    {
        string? correlationCookieValue = Request.Cookies[
            _options.CorrelationCookieName];
        DeleteCorrelationCookie();
        TryParseCorrelationCookieValue(
            correlationCookieValue,
            out string? correlationToken,
            out string? protectedState);

        CompanyLoginStateValidation stateValidation =
            _stateProtector.Validate(protectedState, correlationToken);

        if (!stateValidation.IsValid)
        {
            await _flowService.RecordDeniedAsync(
                stateValidation.CorrelationId
                    ?? Guid.NewGuid().ToString("N"),
                stateValidation.FailureCode ?? "CALLBACK_INVALID",
                cancellationToken);

            return Redirect(
                Request.PathBase.Add(
                    RoleValidationAuthenticationDefaults.AccessDeniedPath).Value!);
        }

        AuthenticationFlowResult result =
            await _flowService.AuthenticateAsync(
                upar,
                stateValidation.CorrelationId!,
                cancellationToken);

        if (!result.IsAllowed)
        {
            return Redirect(
                Request.PathBase.Add(
                    RoleValidationAuthenticationDefaults.AccessDeniedPath).Value!);
        }

        await HttpContext.SignInAsync(
            RoleValidationAuthenticationDefaults.CookieScheme,
            CreatePrincipal(result),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false
            });

        return LocalRedirect(stateValidation.ReturnUrl!);
    }

    [AllowAnonymous]
    [HttpGet("denied")]
    public IActionResult Denied()
    {
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [Authorize(
        Policy = RoleValidationAuthorizationPolicies.RoleValidationUser)]
    [ValidateAntiForgeryToken]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            RoleValidationAuthenticationDefaults.CookieScheme);
        DeleteCorrelationCookie();

        return Redirect(Request.PathBase.Add(
            RoleValidationAuthenticationDefaults.LoginPath).Value!);
    }

    private ClaimsPrincipal CreatePrincipal(AuthenticationFlowResult result)
    {
        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                result.Identity!.CanonicalUserName),
            new(ClaimTypes.Name, ResolveDisplayName(result)),
            new(
                RoleValidationAuthenticationDefaults.EmployeeNoClaimType,
                result.Employee!.EmployeeNo),
            new(ClaimTypes.Role, result.AccessRole!.Value)
        };

        AddOptionalClaim(claims, ClaimTypes.GivenName, result.FirstName);
        AddOptionalClaim(claims, ClaimTypes.Surname, result.LastName);
        AddOptionalClaim(
            claims,
            ClaimTypes.Email,
            result.Email ?? result.Employee.Email);

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                RoleValidationAuthenticationDefaults.CookieScheme,
                ClaimTypes.Name,
                ClaimTypes.Role));
    }

    private static string ResolveDisplayName(AuthenticationFlowResult result)
    {
        string? firstName = result.FirstName?.Trim();
        string? lastName = result.LastName?.Trim();

        return !string.IsNullOrWhiteSpace(firstName) &&
               !string.IsNullOrWhiteSpace(lastName)
            ? $"{firstName} {lastName}"
            : result.Identity!.CanonicalUserName;
    }

    private CookieOptions CreateCorrelationCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(_options.StateLifetimeMinutes)
        };
    }

    private static string CreateCorrelationCookieValue(
        CompanyLoginChallenge challenge)
    {
        return challenge.CorrelationToken + "." + challenge.ProtectedState;
    }

    private static bool TryParseCorrelationCookieValue(
        string? value,
        out string? correlationToken,
        out string? protectedState)
    {
        correlationToken = null;
        protectedState = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separator = value.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 ||
            separator == value.Length - 1 ||
            value.IndexOf('.', separator + 1) >= 0)
        {
            return false;
        }

        correlationToken = value[..separator];
        protectedState = value[(separator + 1)..];
        return true;
    }

    private void DeleteCorrelationCookie()
    {
        Response.Cookies.Delete(
            _options.CorrelationCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/"
            });
    }

    private static void AddOptionalClaim(
        ICollection<Claim> claims,
        string type,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims.Add(new Claim(type, value.Trim()));
        }
    }
}
