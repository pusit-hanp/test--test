using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleValidation.Web.Configuration;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Tests.Email;

public sealed class EmailOptionsStartupTests
{
    [Theory]
    [InlineData("Development", "Hybrid")]
    [InlineData("QA", "Oracle")]
    [InlineData("Production", "Oracle")]
    public async Task StartAsync_Should_AllowMissingEmailSectionWithoutEnablingProcessing(
        string environmentName,
        string dataSource)
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = dataSource
        }, environmentName);

        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<EmailProcessingCapability>().IsEnabled);
        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<EmailOptions>());
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Should_AllowPartialEmailSectionWithoutEnablingProcessing()
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Hybrid",
            ["Email:TransportMode"] = "Fake",
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234"
        });

        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<EmailProcessingCapability>().IsEnabled);
        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<EmailOptions>());
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Should_StartWithApprovedCompleteTuple()
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Hybrid",
            ["Email:TransportMode"] = "Fake",
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15"
        });

        await host.StartAsync();

        EmailOptions options =
            host.Services.GetRequiredService<EmailOptions>();
        Assert.Equal("Fake", options.TransportMode);
        Assert.Equal([5, 15], options.PreSubmitRetry.DelayMinutes);

        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Should_StartDevelopmentOracleFakeWithoutArtifactRoot()
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Oracle",
            ["Email:TransportMode"] = "Fake",
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15"
        });

        await host.StartAsync();

        Assert.Empty(host.Services.GetRequiredService<EmailOptions>()
            .ArtifactRootPath);
        Assert.True(host.Services
            .GetRequiredService<EmailProcessingCapability>()
            .IsEnabled);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Oracle")]
    public async Task StartAsync_Should_DisableDevelopmentApiEmailWithoutStoppingHost(
        string dataSource)
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = dataSource,
            ["Email:TransportMode"] = "ApiEmail",
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:ArtifactRootPath"] = CanonicalArtifactRootOfLength(100),
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15",
            ["Email:ApiEmail:BaseUrl"] = "https://api-email.example",
            ["Email:ApiEmail:Route"] = "/API/v2/EmailCenterRequest",
            ["Email:ApiEmail:ApplicationName"] = "RoleValidation",
            ["Email:ApiEmail:BearerToken"] = "issued-machine-token",
            ["Email:ApiEmail:TimeoutSeconds"] = "30"
        });

        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<EmailProcessingCapability>().IsEnabled);
        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<EmailOptions>());
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Should_UsePreparingStaleMinutesCodeDefaultWhenOmitted()
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Hybrid",
            ["Email:TransportMode"] = "Fake",
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15"
        });

        await host.StartAsync();

        Assert.Equal(
            30,
            host.Services.GetRequiredService<EmailOptions>()
                .PreparingStaleMinutes);
        await host.StopAsync();
    }

    [Fact]
    public async Task DevelopmentConfiguration_Should_BindPreparingStaleMinutesThirty()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AppContext.BaseDirectory
            });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(
            Path.Combine(
                AppContext.BaseDirectory,
                "ConfigurationArtifacts",
                "appsettings.Development.json"),
            optional: false,
            reloadOnChange: false);
        builder.Logging.ClearProviders();
        builder.Services.AddRoleValidationEmailOptions(
            builder.Configuration,
            builder.Environment);
        using IHost host = builder.Build();

        await host.StartAsync();

        Assert.Equal(
            30,
            host.Services.GetRequiredService<EmailOptions>()
                .PreparingStaleMinutes);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-artifacts")]
    [InlineData(@"D:\artifact-root\..\different-root")]
    [InlineData("D:\\artifact\u00a0root")]
    [InlineData("D:\\artifact\0root")]
    public async Task StartAsync_Should_DisableInvalidOracleArtifactRootWithoutStoppingHost(
        string artifactRoot)
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Oracle",
            ["Email:TransportMode"] = "ApiEmail",
            ["Email:RecipientMode"] = "RoleOwner",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:ArtifactRootPath"] = artifactRoot,
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15",
            ["Email:ApiEmail:BaseUrl"] = "https://api-email.example",
            ["Email:ApiEmail:Route"] = "/API/v2/EmailCenterRequest",
            ["Email:ApiEmail:ApplicationName"] = "RoleValidation",
            ["Email:ApiEmail:BearerToken"] = "issued-machine-token",
            ["Email:ApiEmail:TimeoutSeconds"] = "30"
        }, Environments.Production);

        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<EmailProcessingCapability>().IsEnabled);
        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<EmailOptions>());
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Should_DisableOracleArtifactRootWithSurroundingWhitespace()
    {
        using IHost host = BuildOracleHost(
            " " + CanonicalArtifactRootOfLength(100) + " ");

        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<EmailProcessingCapability>().IsEnabled);
        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<EmailOptions>());
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Should_DisableOracleRootThatExceedsOwnerWorkbookStoragePathLimit()
    {
        string root = CanonicalArtifactRootOfLength(885);
        using IHost host = BuildOracleHost(root);

        Assert.Equal(1001, root.Length + 116);
        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<EmailProcessingCapability>().IsEnabled);
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<EmailOptions>());
        Assert.Contains("884", exception.Message, StringComparison.Ordinal);
        Assert.Contains("canonical", exception.Message, StringComparison.Ordinal);
        Assert.Contains("printable ASCII", exception.Message, StringComparison.Ordinal);
        await host.StopAsync();
    }

    private static IHost BuildOracleHost(string artifactRoot) => BuildHost(
        new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Oracle",
            ["Email:TransportMode"] = "ApiEmail",
            ["Email:RecipientMode"] = "RoleOwner",
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:ArtifactRootPath"] = artifactRoot,
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15",
            ["Email:ApiEmail:BaseUrl"] = "https://api-email.example",
            ["Email:ApiEmail:Route"] = "/API/v2/EmailCenterRequest",
            ["Email:ApiEmail:ApplicationName"] = "RoleValidation",
            ["Email:ApiEmail:BearerToken"] = "issued-machine-token",
            ["Email:ApiEmail:TimeoutSeconds"] = "30"
        }, Environments.Production);

    private static IHost BuildHost(
        IReadOnlyDictionary<string, string?> values,
        string environmentName = "Development")
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = environmentName,
                ContentRootPath = AppContext.BaseDirectory
            });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(
            RoleValidationServiceRegistration.CaptureEmailProcessingCapability(
                builder.Configuration,
                builder.Environment));
        builder.Services.AddRoleValidationEmailOptions(
            builder.Configuration,
            builder.Environment);

        return builder.Build();
    }

    private static string CanonicalArtifactRootOfLength(int length)
    {
        string prefix = Path.GetPathRoot(Path.GetTempPath())!;
        var root = new System.Text.StringBuilder(prefix);
        while (root.Length < length)
        {
            int separatorLength = root.Length == prefix.Length ? 0 : 1;
            int segmentLength = Math.Min(
                200,
                length - root.Length - separatorLength);
            if (separatorLength == 1)
            {
                root.Append(Path.DirectorySeparatorChar);
            }

            root.Append('a', segmentLength);
        }

        string result = root.ToString();
        Assert.Equal(length, result.Length);
        Assert.Equal(result, Path.GetFullPath(result));
        return result;
    }
}

public sealed class EmailOptionsProgramContractTests
{
    [Fact]
    public void Program_Should_UseEmailOptionsStartupRegistration()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Program.cs"));

        Assert.Contains(
            "AddRoleValidationEmailOptions(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Program_Should_CaptureOneCapabilityAndAliasAllThreeWorkersInsideItsGate()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Program.cs"));

        const string capture = "CaptureEmailProcessingCapability(";
        Assert.Equal(
            1,
            source.Split(capture, StringSplitOptions.None).Length - 1);
        Assert.Contains("if (emailProcessingCapability.IsEnabled)", source);
        Assert.Contains(
            "AddHostedService<EmailScheduleWorker>(provider =>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddHostedService<EmailRunPreparationWorker>(provider =>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddHostedService<EmailDeliveryWorker>(provider =>",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddHostedService<EmailScheduleWorker>()",
            source,
            StringComparison.Ordinal);
    }
}
