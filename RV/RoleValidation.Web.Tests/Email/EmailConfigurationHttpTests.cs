using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleValidation.Application.Email;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Configuration;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Tests.Email;

public sealed class EmailConfigurationHttpTests
{
    private const string SecretSentinel = "secret-value-must-never-appear-7319";
    private const string TestScheme = "EmailConfigurationHttpTest";

    [Theory]
    [InlineData("QA", "missing", "Email:TransportMode")]
    [InlineData("Production", "missing", "Email:TransportMode")]
    [InlineData("QA", "invalid", "Email:ApiEmail:TimeoutSeconds")]
    [InlineData("Production", "invalid", "Email:ApiEmail:TimeoutSeconds")]
    [InlineData("QA", "binding", "Email")]
    [InlineData("Production", "binding", "Email")]
    public async Task InvalidEmailConfiguration_ShouldStayWithinAuthorizedEmailEndpoints(
        string environmentName, string configurationCase, string expectedSafeKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(EmailSchedulesController).Assembly.GetName().Name,
            EnvironmentName = environmentName,
            ContentRootPath = AppContext.BaseDirectory
        });
        // Never inherit deployment settings, environment secrets, or external services.
        builder.Configuration.Sources.Clear();
        var configuration = new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Oracle"
        };
        if (configurationCase != "missing")
        {
            configuration["Email:TransportMode"] = "ApiEmail";
            configuration["Email:RecipientMode"] = environmentName == "QA"
                ? "SafeRedirect" : "RoleOwner";
            configuration["Email:ApiEmail:BaseUrl"] = "https://unused.example.invalid";
            configuration["Email:ApiEmail:BearerToken"] = SecretSentinel;
            configuration["Email:ApiEmail:TimeoutSeconds"] = configurationCase == "binding"
                ? SecretSentinel : "0";
        }
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(EmailSchedulesController).Assembly);
        // Real antiforgery remains active; ephemeral keys keep this fixture off disk.
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(TestScheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                TestScheme, _ => { });
        builder.Services.AddRoleValidationAuthorization();
        EmailProcessingCapability capability =
            RoleValidationServiceRegistration.CaptureEmailProcessingCapability(
                builder.Configuration, builder.Environment);
        Assert.True(capability.HasConfigurationErrors);
        Assert.False(capability.IsEnabled);
        Assert.Contains(expectedSafeKey, capability.ConfigurationKeys);
        if (configurationCase == "binding")
        {
            Assert.Equal(["Email"], capability.ConfigurationKeys);
        }
        builder.Services.AddSingleton(capability);
        builder.Services.AddRoleValidationEmailOptions(builder.Configuration, builder.Environment);
        int forbiddenResolutions = 0;
        builder.Services.AddSingleton<IEmailManagementReader>(_ =>
        {
            Interlocked.Increment(ref forbiddenResolutions);
            throw new InvalidOperationException("Email reader must not be resolved.");
        });
        builder.Services.AddSingleton<IEmailExecutionStore>(_ =>
        {
            Interlocked.Increment(ref forbiddenResolutions);
            throw new InvalidOperationException("Email execution store must not be resolved.");
        });
        builder.Services.AddSingleton<IEmailArtifactStore>(_ =>
        {
            Interlocked.Increment(ref forbiddenResolutions);
            throw new InvalidOperationException("Email artifact store must not be resolved.");
        });
        builder.Services.AddSingleton<IEmailTransport>(_ =>
        {
            Interlocked.Increment(ref forbiddenResolutions);
            throw new InvalidOperationException("Email transport must not be resolved.");
        });

        await using WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller}/{action=Index}/{id?}");
        app.MapGet("/_test/ordinary", () => Results.Text("ordinary-ok")).AllowAnonymous();
        app.MapGet("/_test/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
            Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken!))
            .RequireAuthorization();
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(address),
            Timeout = TimeSpan.FromSeconds(15)
        };

        using (HttpResponseMessage ordinary = await client.GetAsync("/_test/ordinary"))
        {
            Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
            Assert.Equal("ordinary-ok", await ordinary.Content.ReadAsStringAsync());
        }
        string[] emailPages = ["/EmailSchedules", "/EmailRuns?id=42", "/DeliveryFiles?id=42"];
        foreach (string path in emailPages)
        {
            using HttpResponseMessage localIt = await SendAsync(client, HttpMethod.Get, path, "Local_IT_Admin");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, localIt.StatusCode);
            Assert.True(localIt.Headers.CacheControl?.NoStore == true);
            string body = await localIt.Content.ReadAsStringAsync();
            Assert.Contains("EMAIL_CONFIGURATION_INVALID", body, StringComparison.Ordinal);
            Assert.Contains(expectedSafeKey, body, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretSentinel, body, StringComparison.Ordinal);

            using HttpResponseMessage anonymous = await SendAsync(client, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.DoesNotContain("EMAIL_CONFIGURATION_INVALID", await anonymous.Content.ReadAsStringAsync());

            using HttpResponseMessage admin = await SendAsync(client, HttpMethod.Get, path, "Admin");
            Assert.Equal(path.StartsWith("/DeliveryFiles", StringComparison.Ordinal)
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden, admin.StatusCode);
            string adminBody = await admin.Content.ReadAsStringAsync();
            // Configuration keys are identifiers. Thai collation can match
            // "Email:" to the harmless title "Email configuration unavailable".
            Assert.DoesNotContain("Email:", adminBody, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretSentinel, adminBody, StringComparison.Ordinal);
            string[] displayedKeys = Regex.Matches(adminBody,
                    @"<li\b[^>]*>\s*(?<key>[^<]*)\s*</li>", RegexOptions.CultureInvariant)
                .Select(match => WebUtility.HtmlDecode(match.Groups["key"].Value.Trim()))
                .ToArray();
            Assert.DoesNotContain(displayedKeys,
                key => capability.ConfigurationKeys.Contains(key, StringComparer.Ordinal));
            if (path.StartsWith("/DeliveryFiles", StringComparison.Ordinal))
            {
                Assert.Contains("Contact a Local IT Admin", adminBody, StringComparison.Ordinal);
            }
        }

        using HttpResponseMessage tokenResponse = await SendAsync(
            client, HttpMethod.Get, "/_test/antiforgery", "Local_IT_Admin");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        string token = await tokenResponse.Content.ReadAsStringAsync();
        string[] mutations =
        [
            "/EmailSchedules/RunNow?applicationId=17",
            "/EmailSchedules/SetActive?applicationId=17&emailScheduleId=1&isActive=true",
            "/EmailRuns/Retry?emailRunId=42&emailDeliveryId=9&expectedAttemptCount=1"
        ];
        foreach (string path in mutations)
        {
            using HttpResponseMessage localIt = await SendAsync(client, HttpMethod.Post, path, "Local_IT_Admin", token);
            Assert.Equal(HttpStatusCode.Conflict, localIt.StatusCode);
            Assert.True(localIt.Headers.CacheControl?.NoStore == true);
            Assert.Equal("EMAIL_CONFIGURATION_INVALID", await localIt.Content.ReadAsStringAsync());
            using HttpResponseMessage admin = await SendAsync(client, HttpMethod.Post, path, "Admin", token);
            Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
            using HttpResponseMessage anonymous = await SendAsync(client, HttpMethod.Post, path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        using (HttpResponseMessage noToken = await SendAsync(client, HttpMethod.Post,
            "/EmailSchedules/RunNow?applicationId=17", "Local_IT_Admin"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        }
        Assert.Equal(0, forbiddenResolutions);
        await app.StopAsync();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string? role = null, string? antiforgeryToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (role is not null)
        {
            request.Headers.Add("X-Test-Role", role);
        }
        if (antiforgeryToken is not null)
        {
            request.Headers.Add("RequestVerificationToken", antiforgeryToken);
        }
        return await client.SendAsync(request);
    }

    public sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public HeaderAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrEmpty(role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "http-test-user"),
                new Claim(ClaimTypes.Role, role),
                new Claim(RoleValidationAuthenticationDefaults.EmployeeNoClaimType, "TEST0001")
            ], TestScheme));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, TestScheme)));
        }
    }
}
