using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using RoleValidation.Application.Email;
using RoleValidation.Infrastructure.Email;
using RoleValidation.Web.Configuration;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Tests.Configuration;

public sealed class ApiEmailServiceRegistrationTests
{
    [Fact]
    public void Validator_Should_RequireCompleteApiEmailClientConfiguration()
    {
        EmailOptions options = CompleteOptions();
        options.ApiEmail.BearerToken = string.Empty;
        var validator = new EmailOptionsValidator("QA", "Oracle");

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_Should_AcceptCompleteQaSafeRedirectApiEmailTuple()
    {
        var validator = new EmailOptionsValidator("QA", "Oracle");

        var result = validator.Validate(null, CompleteOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_Should_RejectDifferentApiEmailApplicationName()
    {
        EmailOptions options = CompleteOptions();
        options.ApiEmail.ApplicationName = "AnotherClient";
        var validator = new EmailOptionsValidator("QA", "Oracle");

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(
                "ApplicationName",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CaptureCapability_Should_EnableOnlyCompleteApiEmailTuple()
    {
        IConfiguration complete = BuildConfiguration(includeBearerToken: true);
        IConfiguration incomplete = BuildConfiguration(includeBearerToken: false);
        IConfiguration incompleteContent = BuildConfiguration(
            includeBearerToken: true,
            includeContent: false);
        IHostEnvironment environment = Environment("QA");

        Assert.True(RoleValidationServiceRegistration
            .CaptureEmailProcessingCapability(complete, environment)
            .IsEnabled);
        Assert.False(RoleValidationServiceRegistration
            .CaptureEmailProcessingCapability(incomplete, environment)
            .IsEnabled);
        Assert.False(RoleValidationServiceRegistration
            .CaptureEmailProcessingCapability(incompleteContent, environment)
            .IsEnabled);
    }

    [Fact]
    public void AddRoleValidationData_Should_RegisterApiEmailTransportForEnabledQaTuple()
    {
        IConfiguration configuration = BuildConfiguration(includeBearerToken: true);
        IHostEnvironment environment = Environment("QA");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoleValidationEmailOptions(configuration, environment);
        EmailProcessingCapability capability = RoleValidationServiceRegistration
            .CaptureEmailProcessingCapability(configuration, environment);

        services.AddRoleValidationData(
            configuration,
            environment,
            capability);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.True(capability.IsEnabled);
        Assert.IsType<ApiEmailTransport>(
            scope.ServiceProvider.GetRequiredService<IEmailTransport>());
    }

    [Fact]
    public void AddRoleValidationData_Should_DisableApiEmailRedirects()
    {
        IConfiguration configuration = BuildConfiguration(includeBearerToken: true);
        IHostEnvironment environment = Environment("QA");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoleValidationEmailOptions(configuration, environment);
        services.AddRoleValidationData(
            configuration,
            environment,
            RoleValidationServiceRegistration.CaptureEmailProcessingCapability(
                configuration,
                environment));

        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpMessageHandlerFactory factory = provider
            .GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler handler = factory.CreateHandler(
            typeof(ApiEmailTransport).Name);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        Assert.False(Assert.IsType<SocketsHttpHandler>(handler).AllowAutoRedirect);
    }

    private static EmailOptions CompleteOptions() => new()
    {
        TransportMode = "ApiEmail",
        RecipientMode = "SafeRedirect",
        SafeRedirectEmployeeNo = "C2001234",
        ArtifactRootPath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "RoleValidationQaArtifacts")),
        PreparingStaleMinutes = 30,
        Content = ValidContent(),
        PreSubmitRetry = new PreSubmitRetryOptions
        {
            MaxAttempts = 3,
            DelayMinutes = [5, 15]
        },
        ApiEmail = new ApiEmailClientOptions
        {
            BaseUrl = "https://api-email.example",
            Route = "/API/v2/EmailCenterRequest",
            ApplicationName = "RoleValidation",
            BearerToken = "issued-machine-token",
            TimeoutSeconds = 30
        }
    };

    private static IConfiguration BuildConfiguration(
        bool includeBearerToken,
        bool includeContent = true)
    {
        const string connectionString =
            "Data Source=fake-master;User Id=fake;Password=fake";
        var values = new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Oracle",
            ["Security:TextEncryption:EncryptedConfiguration"] = "false",
            ["ConnectionStrings:Master"] = connectionString,
            ["ConnectionStrings:Material"] = connectionString,
            ["ConnectionStrings:AppSim"] = connectionString,
            ["Email:TransportMode"] = "ApiEmail",
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:ArtifactRootPath"] = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "RoleValidationQaArtifacts")),
            ["Email:PreparingStaleMinutes"] = "30",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15",
            ["Email:Content:SubjectTemplate"] = includeContent
                ? "[RoleValidation] Annual access review - {ApplicationName}"
                : null,
            ["Email:Content:BodyTemplate"] = includeContent
                ? "Please review the attached workbook for {ApplicationName}.\n" +
                    "Intended owner: {OwnerEmployeeNo}"
                : null,
            ["Email:ApiEmail:BaseUrl"] = "https://api-email.example",
            ["Email:ApiEmail:Route"] = "/API/v2/EmailCenterRequest",
            ["Email:ApiEmail:ApplicationName"] = "RoleValidation",
            ["Email:ApiEmail:TimeoutSeconds"] = "30",
            ["Email:ApiEmail:BearerToken"] = includeBearerToken
                ? "issued-machine-token"
                : null
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static EmailContentOptions ValidContent() => new()
    {
        SubjectTemplate =
            "[RoleValidation] Annual access review - {ApplicationName}",
        BodyTemplate =
            "Please review the attached workbook for {ApplicationName}.\n" +
            "Intended owner: {OwnerEmployeeNo}"
    };

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns(name);
        environment.SetupGet(item => item.ContentRootPath)
            .Returns(Path.GetTempPath());
        return environment.Object;
    }
}
