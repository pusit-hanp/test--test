using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RoleValidation.Application.Authentication;
using RoleValidation.Application.Employees;
using RoleValidation.Core.Features.Authorization;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Infrastructure.Security;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;

namespace RoleValidation.Web.Tests.Authentication;

public sealed class AuthenticationControllerTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        27,
        3,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void AuthenticationResponses_ShouldDisableCachingAndReferrerForwarding()
    {
        ControllerDependencies dependencies = CreateDependencies([]);
        AuthenticationController controller = CreateController(dependencies);
        var context = new ActionExecutingContext(
            new ActionContext(controller.HttpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);
        ResponseCacheAttribute cachePolicy = Assert.Single(
            typeof(AuthenticationController)
                .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
                .Cast<ResponseCacheAttribute>());
        var cacheFilter = Assert.IsAssignableFrom<IActionFilter>(
            cachePolicy.CreateInstance(controller.HttpContext.RequestServices));
        context.Filters.Add(cacheFilter);

        cacheFilter.OnActionExecuting(context);
        controller.OnActionExecuting(context);

        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", controller.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public void Begin_Should_SetHostOnlyCorrelationCookieAndRedirectToLoginWeb()
    {
        ControllerDependencies dependencies = CreateDependencies(
            [new AuthorizedUserRecord("62032665", "Admin", true)]);
        AuthenticationController controller = CreateController(dependencies);

        IActionResult action = controller.Begin(
            "/ApplicationUsers?applicationId=2");

        RedirectResult redirect = Assert.IsType<RedirectResult>(action);
        var loginUri = new Uri(redirect.Url!);
        Assert.Equal(
            "https://login.qa.example/Login",
            loginUri.GetLeftPart(UriPartial.Path));
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> loginQuery =
            QueryHelpers.ParseQuery(loginUri.Query);
        string encryptedCallback = Assert.Single(loginQuery["uri"])!;
        Assert.False(loginQuery.ContainsKey("url"));
        var callbackUri = new Uri(
            dependencies.Encryption.Decrypt(encryptedCallback));
        Assert.Equal(
            "https://rolevalidation.qa.example/RoleValidation/authentication/callback",
            callbackUri.GetLeftPart(UriPartial.Path));
        Assert.Equal(string.Empty, callbackUri.Query);

        string setCookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(
            dependencies.Options.CorrelationCookieName + "=",
            setCookie,
            StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_Should_IssueNonPersistentCookieOnlyAfterApproval()
    {
        ControllerDependencies dependencies = CreateDependencies(
            [new AuthorizedUserRecord(
                "62032665",
                "Local_IT_Admin",
                true)]);
        AuthenticationController controller = CreateController(dependencies);
        CompanyLoginChallenge challenge = dependencies.ChallengeService.Create(
            "/ApplicationUsers?applicationId=2");
        controller.Request.Headers.Cookie =
            $"{dependencies.Options.CorrelationCookieName}=" +
            challenge.CorrelationToken + "." + challenge.ProtectedState;

        IActionResult action = await controller.Callback(
            CreateValidResponse(dependencies.Encryption),
            CancellationToken.None);

        LocalRedirectResult redirect = Assert.IsType<LocalRedirectResult>(action);
        Assert.Equal(
            "/ApplicationUsers?applicationId=2",
            redirect.Url);
        Assert.Equal(
            RoleValidationAuthenticationDefaults.CookieScheme,
            dependencies.SignedInScheme);
        Assert.NotNull(dependencies.SignedInPrincipal);
        Assert.Equal(
            "person.user",
            dependencies.SignedInPrincipal.FindFirstValue(
                ClaimTypes.NameIdentifier));
        Assert.Equal(
            "62032665",
            dependencies.SignedInPrincipal.FindFirstValue(
                RoleValidationAuthenticationDefaults.EmployeeNoClaimType));
        Assert.Equal(
            "Local_IT_Admin",
            dependencies.SignedInPrincipal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(
            "Person User",
            dependencies.SignedInPrincipal.Identity!.Name);
        Assert.False(dependencies.SignInProperties!.IsPersistent);
        Assert.False(dependencies.SignInProperties.AllowRefresh);

        LoginAccessEvent loginEvent = Assert.Single(dependencies.Recorder.Events);
        Assert.Equal(LoginAccessResult.Success, loginEvent.Result);
        Assert.Equal(challenge.CorrelationId, loginEvent.CorrelationId);
    }

    [Theory]
    [InlineData("", "/authentication/denied")]
    [InlineData("/RoleValidation", "/RoleValidation/authentication/denied")]
    public async Task Callback_Should_NotIssueCookieWhenUserIsNotAuthorized(
        string pathBase, string deniedLocation)
    {
        ControllerDependencies dependencies = CreateDependencies([]);
        AuthenticationController controller = CreateController(dependencies);
        controller.Request.PathBase = pathBase;
        CompanyLoginChallenge challenge = dependencies.ChallengeService.Create("/");
        controller.Request.Headers.Cookie =
            $"{dependencies.Options.CorrelationCookieName}=" +
            challenge.CorrelationToken + "." + challenge.ProtectedState;

        IActionResult action = await controller.Callback(
            CreateValidResponse(dependencies.Encryption),
            CancellationToken.None);

        RedirectResult redirect = Assert.IsType<RedirectResult>(action);
        Assert.Equal(
            deniedLocation,
            redirect.Url);
        Assert.Null(dependencies.SignedInPrincipal);
        LoginAccessEvent loginEvent = Assert.Single(dependencies.Recorder.Events);
        Assert.Equal(LoginAccessResult.Denied, loginEvent.Result);
        Assert.Equal("USER_NOT_AUTHORIZED", loginEvent.FailureCode);
    }

    [Fact]
    public async Task Callback_Should_DenyReplayBeforeReadingLoginResponse()
    {
        ControllerDependencies dependencies = CreateDependencies(
            [new AuthorizedUserRecord("62032665", "Admin", true)]);
        AuthenticationController controller = CreateController(dependencies);
        CompanyLoginChallenge challenge = dependencies.ChallengeService.Create("/");
        controller.Request.Headers.Cookie =
            $"{dependencies.Options.CorrelationCookieName}=wrong-token." +
            challenge.ProtectedState;

        IActionResult action = await controller.Callback(
            CreateValidResponse(dependencies.Encryption),
            CancellationToken.None);

        RedirectResult redirect = Assert.IsType<RedirectResult>(action);
        Assert.Equal(
            RoleValidationAuthenticationDefaults.AccessDeniedPath,
            redirect.Url);
        Assert.Null(dependencies.SignedInPrincipal);
        LoginAccessEvent loginEvent = Assert.Single(dependencies.Recorder.Events);
        Assert.Equal("CALLBACK_REPLAYED", loginEvent.FailureCode);
        Assert.Null(loginEvent.EmployeeNo);
        Assert.Equal(challenge.CorrelationId, loginEvent.CorrelationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://other.example/")]
    public async Task BeginWithoutSafeReturnUrl_ShouldReturnToApplicationRootAfterLogin(
        string? returnUrl)
    {
        ControllerDependencies dependencies = CreateDependencies(
            [new AuthorizedUserRecord("62032665", "Admin", true)]);
        AuthenticationController controller = CreateController(dependencies);
        controller.Request.PathBase = "/RoleValidation";

        controller.Begin(returnUrl);
        string cookie = controller.Response.Headers.SetCookie.ToString().Split(';')[0];
        controller.Request.Headers.Cookie = cookie;
        IActionResult action = await controller.Callback(
            CreateValidResponse(dependencies.Encryption));

        Assert.Equal("/RoleValidation/", Assert.IsType<LocalRedirectResult>(action).Url);
    }

    [Theory]
    [InlineData("", "/authentication/challenge")]
    [InlineData("/RoleValidation", "/RoleValidation/authentication/challenge")]
    public async Task Logout_Should_ClearLocalSessionAndRedirectWithinApplication(
        string pathBase,
        string expectedLocation)
    {
        ControllerDependencies dependencies = CreateDependencies(
            [new AuthorizedUserRecord("62032665", "Admin", true)]);
        AuthenticationController controller = CreateController(dependencies);
        controller.Request.PathBase = pathBase;

        IActionResult action = await controller.Logout();

        RedirectResult redirect = Assert.IsType<RedirectResult>(action);
        Assert.Equal(
            expectedLocation,
            redirect.Url);
        Assert.Equal(
            RoleValidationAuthenticationDefaults.CookieScheme,
            dependencies.SignedOutScheme);
        string setCookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(
            dependencies.Options.CorrelationCookieName + "=",
            setCookie,
            StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private static AuthenticationController CreateController(
        ControllerDependencies dependencies)
    {
        var controller = new AuthenticationController(
            dependencies.ChallengeService,
            dependencies.StateProtector,
            dependencies.FlowService,
            dependencies.Options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthenticationService>(
            dependencies.AuthenticationService.Object);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    private static ControllerDependencies CreateDependencies(
        IReadOnlyList<AuthorizedUserRecord> authorizedUsers)
    {
        CompanyLoginOptions options = new()
        {
            LoginUrl = "https://login.qa.example/Login",
            PublicOrigin = "https://rolevalidation.qa.example/RoleValidation",
            CallbackPath = "/authentication/callback",
            Audience = "RoleValidation-QA",
            ResponseLifetimeMinutes = 5,
            StateLifetimeMinutes = 5,
            ClockSkewSeconds = 30
        };
        var timeProvider = new FixedTimeProvider(Now);
        var encryption = new AesTextEncryptionService(
            "safe-test-passphrase");
        var stateStore = new InMemoryCompanyLoginStateStore(timeProvider);
        var stateProtector = new CompanyLoginStateProtector(
            new EphemeralDataProtectionProvider(),
            options,
            timeProvider,
            stateStore);
        var challengeService = new CompanyLoginChallengeService(
            options,
            encryption,
            stateProtector);
        var recorder = new RecordingLoginAccessRecorder();
        var flowService = new AuthenticationFlowService(
            new CompanyLoginResponseValidator(
                encryption,
                options,
                timeProvider),
            new AuthenticationAccessEvaluator(
                new EmployeeIdentityResolver(
                    new StubEmployeeReader(
                        [CreateEmployee("62032665", "person.user")])),
                new StubAuthorizedUserReader(authorizedUsers)),
            recorder,
            timeProvider);
        var authenticationService = new Mock<IAuthenticationService>();
        var dependencies = new ControllerDependencies(
            options,
            encryption,
            stateProtector,
            challengeService,
            flowService,
            recorder,
            authenticationService);
        authenticationService
            .Setup(service => service.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties?>()))
            .Callback<HttpContext, string, ClaimsPrincipal,
                AuthenticationProperties?>((_, scheme, principal, properties) =>
                {
                    dependencies.SignedInScheme = scheme;
                    dependencies.SignedInPrincipal = principal;
                    dependencies.SignInProperties = properties;
                })
            .Returns(Task.CompletedTask);
        authenticationService
            .Setup(service => service.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string?>(),
                It.IsAny<AuthenticationProperties?>()))
            .Callback<HttpContext, string?, AuthenticationProperties?>(
                (_, scheme, _) => dependencies.SignedOutScheme = scheme)
            .Returns(Task.CompletedTask);

        return dependencies;
    }

    private static string CreateValidResponse(
        AesTextEncryptionService encryption)
    {
        return encryption.Encrypt(
            "$user=person.user,fname=Person,lname=User," +
            "mail=person.user@example.com,time=08/27/2026 10:04:00");
    }

    private static EmployeeRecord CreateEmployee(
        string employeeNo,
        string userName)
    {
        return new EmployeeRecord(
            employeeNo,
            "Person User",
            new EmployeeStatus("A"),
            email: "person.user@example.com",
            userName: userName,
            joinDate: new DateTime(2026, 7, 1));
    }

    private sealed class ControllerDependencies
    {
        public ControllerDependencies(
            CompanyLoginOptions options,
            AesTextEncryptionService encryption,
            CompanyLoginStateProtector stateProtector,
            CompanyLoginChallengeService challengeService,
            AuthenticationFlowService flowService,
            RecordingLoginAccessRecorder recorder,
            Mock<IAuthenticationService> authenticationService)
        {
            Options = options;
            Encryption = encryption;
            StateProtector = stateProtector;
            ChallengeService = challengeService;
            FlowService = flowService;
            Recorder = recorder;
            AuthenticationService = authenticationService;
        }

        public CompanyLoginOptions Options { get; }

        public AesTextEncryptionService Encryption { get; }

        public CompanyLoginStateProtector StateProtector { get; }

        public CompanyLoginChallengeService ChallengeService { get; }

        public AuthenticationFlowService FlowService { get; }

        public RecordingLoginAccessRecorder Recorder { get; }

        public Mock<IAuthenticationService> AuthenticationService { get; }

        public string? SignedInScheme { get; set; }

        public ClaimsPrincipal? SignedInPrincipal { get; set; }

        public AuthenticationProperties? SignInProperties { get; set; }

        public string? SignedOutScheme { get; set; }
    }

    private sealed class RecordingLoginAccessRecorder : ILoginAccessRecorder
    {
        public List<LoginAccessEvent> Events { get; } = [];

        public Task RecordAsync(
            LoginAccessEvent loginAccessEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(loginAccessEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthorizedUserReader : IAuthorizedUserReader
    {
        private readonly IReadOnlyList<AuthorizedUserRecord> _records;

        public StubAuthorizedUserReader(
            IReadOnlyList<AuthorizedUserRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<AuthorizedUserRecord>> FindByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records);
        }
    }

    private sealed class StubEmployeeReader : IEmployeeReader
    {
        private readonly IReadOnlyList<EmployeeRecord> _records;

        public StubEmployeeReader(IReadOnlyList<EmployeeRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EmployeeRecord>>([]);
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone(
                "TestTimeZone",
                TimeSpan.FromHours(7),
                "TestTimeZone",
                "TestTimeZone");

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
