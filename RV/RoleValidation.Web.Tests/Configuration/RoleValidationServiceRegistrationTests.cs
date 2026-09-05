using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using RoleValidation.Application.Administration;
using RoleValidation.Application.Authentication;
using RoleValidation.Application.Authorization;
using RoleValidation.Application.Applications;
using RoleValidation.Application.Employees;
using RoleValidation.Application.Email;
using RoleValidation.Application.Exports;
using RoleValidation.Application.History;
using RoleValidation.Application.RoleOwners;
using RoleValidation.Application.SourceMappings;
using RoleValidation.Application.Users;
using RoleValidation.Core.Features.Authorization;
using RoleValidation.Infrastructure.Authorization;
using RoleValidation.Infrastructure.Administration;
using RoleValidation.Infrastructure.Applications;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;
using RoleValidation.Infrastructure.Development;
using RoleValidation.Infrastructure.Employees;
using RoleValidation.Infrastructure.Email;
using RoleValidation.Infrastructure.Exports;
using RoleValidation.Infrastructure.History;
using RoleValidation.Infrastructure.RoleOwners;
using RoleValidation.Infrastructure.SourceMappings;
using RoleValidation.Infrastructure.Temporary;
using RoleValidation.Web.Configuration;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Tests.Configuration;

public sealed class RoleValidationServiceRegistrationTests
{
    [Fact]
    public void AddRoleValidationData_Should_RegisterScopedConfiguredContentSource()
    {
        using ServiceProvider services = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid");
        using IServiceScope first = services.CreateScope();
        using IServiceScope second = services.CreateScope();

        IEmailContentSource firstSource = first.ServiceProvider
            .GetRequiredService<IEmailContentSource>();
        IEmailContentSource secondSource = second.ServiceProvider
            .GetRequiredService<IEmailContentSource>();

        Assert.IsType<ConfiguredEmailContentSource>(firstSource);
        Assert.NotSame(firstSource, secondSource);
    }

    [Fact]
    public void AddRoleValidationData_Should_RegisterEmailExecutionLifetimes()
    {
        using ServiceProvider hybrid = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid");
        using ServiceProvider oracle = BuildEmailExecutionServices(
            Environments.Production,
            "Oracle");

        AssertEmailGraphLifetimes<DevelopmentEmailExecutionStore>(
            hybrid,
            storeIsSingleton: true);
        AssertEmailGraphLifetimes<OracleEmailExecutionStore>(
            oracle,
            storeIsSingleton: false);

        EmailConfigurationSnapshot oracleSnapshot =
            oracle.GetRequiredService<EmailConfigurationSnapshot>();
        Assert.Equal("ORACLE", oracleSnapshot.DataSource);
        Assert.Equal("API_EMAIL", oracleSnapshot.TransportMode);
        Assert.Equal("ROLE_OWNER", oracleSnapshot.RecipientMode);
    }

    [Fact]
    public void AddRoleValidationData_Should_AliasManagementReaderToExactStoreLifetime()
    {
        using ServiceProvider hybrid = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid");
        using IServiceScope hybridFirst = hybrid.CreateScope();
        using IServiceScope hybridSecond = hybrid.CreateScope();
        IEmailExecutionStore hybridStore = hybridFirst.ServiceProvider
            .GetRequiredService<IEmailExecutionStore>();
        IEmailManagementReader hybridReader = hybridFirst.ServiceProvider
            .GetRequiredService<IEmailManagementReader>();
        Assert.Same(hybridStore, hybridReader);
        Assert.Same(
            hybridReader,
            hybridSecond.ServiceProvider.GetRequiredService<IEmailManagementReader>());
        ResolveUnknownDeliveryHandler hybridHandler = hybridFirst.ServiceProvider
            .GetRequiredService<ResolveUnknownDeliveryHandler>();
        Assert.Same(
            hybridHandler,
            hybridFirst.ServiceProvider
                .GetRequiredService<ResolveUnknownDeliveryHandler>());
        Assert.NotSame(
            hybridHandler,
            hybridSecond.ServiceProvider
                .GetRequiredService<ResolveUnknownDeliveryHandler>());
        Assert.Null(hybrid.GetService<DevelopmentEmailExecutionStore>());

        var poison = new PoisonMasterOracleConnectionFactory();
        using ServiceProvider oracle = BuildEmailExecutionServices(
            Environments.Production,
            "Oracle",
            poisonFactory: poison);
        using IServiceScope oracleFirst = oracle.CreateScope();
        using IServiceScope oracleSecond = oracle.CreateScope();
        IEmailExecutionStore oracleStore = oracleFirst.ServiceProvider
            .GetRequiredService<IEmailExecutionStore>();
        IEmailManagementReader oracleReader = oracleFirst.ServiceProvider
            .GetRequiredService<IEmailManagementReader>();
        Assert.Same(oracleStore, oracleReader);
        Assert.NotSame(
            oracleReader,
            oracleSecond.ServiceProvider.GetRequiredService<IEmailManagementReader>());
        Assert.NotNull(oracleFirst.ServiceProvider
            .GetRequiredService<ResolveUnknownDeliveryHandler>());
        Assert.Null(oracle.GetService<OracleEmailExecutionStore>());
        Assert.Equal(0, poison.CreateConnectionCount);
    }

    [Fact]
    public void AddRoleValidationData_Should_KeepEmailAdministrationAbsentForTemporary()
    {
        using ServiceProvider services = BuildServices(
            Environments.Development,
            "Temporary");

        Assert.Null(services.GetService<IEmailExecutionStore>());
        Assert.Null(services.GetService<IEmailManagementReader>());
        Assert.Null(services.GetService<ResolveUnknownDeliveryHandler>());
    }

    [Fact]
    public void AddRoleValidationData_Should_ComposeOneImmutableSnapshotAndHonorTestClock()
    {
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 0, 0, TimeSpan.Zero));
        using ServiceProvider services = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid",
            clock);

        EmailConfigurationSnapshot snapshot =
            services.GetRequiredService<EmailConfigurationSnapshot>();
        Assert.Same(
            snapshot,
            services.GetRequiredService<EmailConfigurationSnapshot>());
        Assert.Equal("HYBRID", snapshot.DataSource);
        Assert.Equal("FAKE", snapshot.TransportMode);
        Assert.Equal("SAFE_REDIRECT", snapshot.RecipientMode);
        Assert.Same(clock, services.GetRequiredService<TimeProvider>());

        EmailOptions mutableOptions = services.GetRequiredService<EmailOptions>();
        mutableOptions.TransportMode = "ApiEmail";
        mutableOptions.RecipientMode = "RoleOwner";

        Assert.Equal("FAKE", snapshot.TransportMode);
        Assert.Equal("SAFE_REDIRECT", snapshot.RecipientMode);
    }

    [Theory]
    [InlineData("Development", "Hybrid", "Fake", "SafeRedirect", true)]
    [InlineData("Development", "Hybrid", "ApiEmail", "SafeRedirect", false)]
    [InlineData("Development", "Oracle", "Fake", "SafeRedirect", true)]
    [InlineData("Development", "Oracle", "ApiEmail", "SafeRedirect", false)]
    [InlineData("Development", "Hybrid", "fake", "SafeRedirect", false)]
    [InlineData("Development", "Hybrid", "Fake", "saferedirect", false)]
    [InlineData("Development", "hybrid", "Fake", "SafeRedirect", false)]
    [InlineData("development", "Hybrid", "Fake", "SafeRedirect", false)]
    [InlineData("QA", "Oracle", "ApiEmail", "SafeRedirect", true)]
    [InlineData("Production", "Oracle", "ApiEmail", "RoleOwner", true)]
    public void AddRoleValidationData_Should_UseExactImmutableCapabilityForGraphAndHostedAliases(
        string environmentName,
        string dataSource,
        string transportMode,
        string recipientMode,
        bool expectedEnabled)
    {
        using ServiceProvider services = BuildEmailExecutionServices(
            environmentName,
            dataSource,
            transportMode: transportMode,
            recipientMode: recipientMode);

        EmailProcessingCapability capability = services
            .GetRequiredService<EmailProcessingCapability>();
        Assert.Equal(expectedEnabled, capability.IsEnabled);
        Assert.Same(
            capability,
            services.GetRequiredService<EmailProcessingCapability>());

        IHostedService[] hosted = services.GetServices<IHostedService>()
            .ToArray();
        if (expectedEnabled)
        {
            EmailScheduleWorker schedule = Assert.Single(
                hosted.OfType<EmailScheduleWorker>());
            EmailRunPreparationWorker preparation = Assert.Single(
                hosted.OfType<EmailRunPreparationWorker>());
            EmailDeliveryWorker delivery = Assert.Single(
                hosted.OfType<EmailDeliveryWorker>());
            Assert.Same(
                schedule,
                services.GetRequiredService<EmailScheduleWorker>());
            Assert.Same(
                preparation,
                services.GetRequiredService<EmailRunPreparationWorker>());
            Assert.Same(
                delivery,
                services.GetRequiredService<EmailDeliveryWorker>());
            using IServiceScope scope = services.CreateScope();
            Assert.IsType<ConfiguredEmailContentSource>(
                scope.ServiceProvider.GetRequiredService<IEmailContentSource>());
            if (transportMode == "Fake")
            {
                Assert.Same(
                    services.GetRequiredService<FakeEmailTransport>(),
                    services.GetRequiredService<IEmailTransport>());
            }
            else
            {
                Assert.IsType<ApiEmailTransport>(
                    scope.ServiceProvider.GetRequiredService<IEmailTransport>());
            }

            Assert.NotNull(scope.ServiceProvider
                .GetRequiredService<EmailRecipientPolicy>());
            Assert.NotNull(scope.ServiceProvider
                .GetRequiredService<ProcessEmailDeliveryHandler>());
        }
        else
        {
            Assert.DoesNotContain(hosted, service =>
                service is EmailScheduleWorker or
                    EmailRunPreparationWorker or
                    EmailDeliveryWorker);
            Assert.Null(services.GetService<EmailScheduleWorker>());
            Assert.Null(services.GetService<EmailRunPreparationWorker>());
            Assert.Null(services.GetService<EmailDeliveryWorker>());
            Assert.Null(services.GetService<UnconfiguredEmailContentSource>());
            Assert.Null(services.GetService<IEmailContentSource>());
            Assert.Null(services.GetService<FakeEmailTransport>());
            Assert.Null(services.GetService<IEmailTransport>());
            Assert.Null(services.GetService<EmailRecipientPolicy>());
            Assert.Null(services.GetService<ProcessEmailDeliveryHandler>());
        }
    }

    [Theory]
    [InlineData("QA", "Email:Content:BodyTemplate", "")]
    [InlineData("Production", "Email:Content:BodyTemplate", "")]
    [InlineData("QA", "Email:ApiEmail:TimeoutSeconds", "not-an-integer")]
    [InlineData("Production", "Email:ApiEmail:TimeoutSeconds", "not-an-integer")]
    public void InvalidEmailConfiguration_Should_NotRegisterWorkersOrTransport(
        string environmentName, string key, string value)
    {
        using ServiceProvider services = BuildEmailExecutionServices(
            environmentName,
            "Oracle",
            configureEmail: settings => settings[key] = value);

        EmailProcessingCapability capability = services
            .GetRequiredService<EmailProcessingCapability>();
        Assert.False(capability.IsEnabled);
        Assert.True(capability.HasConfigurationErrors);
        Assert.Empty(services.GetServices<IHostedService>());
        Assert.Null(services.GetService<EmailScheduleWorker>());
        Assert.Null(services.GetService<EmailRunPreparationWorker>());
        Assert.Null(services.GetService<EmailDeliveryWorker>());
        using IServiceScope scope = services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IEmailTransport>());
        Assert.Null(scope.ServiceProvider.GetService<ProcessEmailDeliveryHandler>());
    }

    [Theory]
    [InlineData("Fake", "ApiEmail", true)]
    [InlineData("ApiEmail", "Fake", false)]
    public void AddRoleValidationData_Should_KeepCapabilityAndGraphFrozenAfterConfigurationReload(
        string startupTransportMode,
        string reloadedTransportMode,
        bool expectedEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = "Hybrid",
            ["Email:TransportMode"] = startupTransportMode,
            ["Email:RecipientMode"] = "SafeRedirect",
            ["Email:SafeRedirectEmployeeNo"] = "C1008267",
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15",
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Security:TextEncryption:EncryptedConfiguration"] = "false",
            ["ConnectionStrings:Master"] =
                "Data Source=fake-master;User Id=fake;Password=fake",
            ["ConnectionStrings:Material"] =
                "Data Source=fake-master;User Id=fake;Password=fake",
            ["ConnectionStrings:AppSim"] =
                "Data Source=fake-master;User Id=fake;Password=fake"
        };
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName)
            .Returns(Environments.Development);
        environment.SetupGet(item => item.ContentRootPath)
            .Returns(Path.GetTempPath());
        EmailProcessingCapability capability =
            RoleValidationServiceRegistration.CaptureEmailProcessingCapability(
                configuration,
                environment.Object);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new EmailOptions
        {
            TransportMode = startupTransportMode,
            RecipientMode = "SafeRedirect",
            SafeRedirectEmployeeNo = "C1008267",
            PreSubmitRetry = new PreSubmitRetryOptions
            {
                MaxAttempts = 3,
                DelayMinutes = [5, 15]
            },
            Content = new EmailContentOptions
            {
                SubjectTemplate =
                    "[RoleValidation] Annual access review - {ApplicationName}",
                BodyTemplate =
                    "Please review the attached workbook for {ApplicationName}.\n" +
                    "Intended owner: {OwnerEmployeeNo}"
            }
        });
        services.AddRoleValidationData(
            configuration,
            environment.Object,
            capability);
        if (capability.IsEnabled)
        {
            services.AddHostedService<EmailScheduleWorker>(provider =>
                provider.GetRequiredService<EmailScheduleWorker>());
            services.AddHostedService<EmailRunPreparationWorker>(provider =>
                provider.GetRequiredService<EmailRunPreparationWorker>());
            services.AddHostedService<EmailDeliveryWorker>(provider =>
                provider.GetRequiredService<EmailDeliveryWorker>());
        }
        using ServiceProvider provider = services.BuildServiceProvider();

        configuration["Email:TransportMode"] = reloadedTransportMode;
        configuration.Reload();

        Assert.Equal(expectedEnabled, capability.IsEnabled);
        Assert.Same(
            capability,
            provider.GetRequiredService<EmailProcessingCapability>());
        Assert.Equal(
            expectedEnabled ? 3 : 0,
            provider.GetServices<IHostedService>().Count(service =>
                service is EmailScheduleWorker or
                    EmailRunPreparationWorker or
                    EmailDeliveryWorker));
    }

    [Fact]
    public void AddRoleValidationData_Should_ResolveHybridPreparationGraphWithoutOracleWriter()
    {
        var poison = new PoisonMasterOracleConnectionFactory();
        using ServiceProvider services = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid",
            poisonFactory: poison);

        EmailRunPreparationWorker worker = services
            .GetRequiredService<EmailRunPreparationWorker>();
        Assert.Same(worker, services.GetRequiredService<EmailRunPreparationWorker>());
        Assert.Same(
            worker,
            Assert.Single(services.GetServices<IHostedService>()
                .OfType<EmailRunPreparationWorker>()));
        using IServiceScope scope = services.CreateScope();
        Assert.IsType<ErsnOwnerScopeBuilder>(scope.ServiceProvider
            .GetRequiredService<IOwnerScopeBuilder>());
        Assert.IsType<OpenXmlErsnOwnerWorkbookExporter>(services
            .GetRequiredService<IOwnerWorkbookExporter>());
        Assert.IsType<FileSystemEmailArtifactStore>(services
            .GetRequiredService<IEmailArtifactStore>());
        Assert.IsType<ZipApplicationWorkbookBuilder>(services
            .GetRequiredService<IEmailRunZipBuilder>());
        Assert.NotNull(scope.ServiceProvider
            .GetRequiredService<PrepareEmailRunHandler>());
        Assert.NotNull(scope.ServiceProvider
            .GetRequiredService<PrepareEmailDeliveryArtifactHandler>());
        Assert.NotNull(scope.ServiceProvider
            .GetRequiredService<BuildEmailRunZipHandler>());
        Assert.Equal(0, poison.CreateConnectionCount);
    }

    [Fact]
    public async Task HybridPreparationWorker_Should_ScanComposedGraphWithoutOracleWriter()
    {
        var poison = new PoisonMasterOracleConnectionFactory();
        using ServiceProvider services = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid",
            poisonFactory: poison);
        EmailRunPreparationWorker worker = services
            .GetRequiredService<EmailRunPreparationWorker>();
        MethodInfo? scan = typeof(EmailRunPreparationWorker).GetMethod(
            "ScanOnceAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(scan);

        Task scanTask = Assert.IsAssignableFrom<Task>(scan!.Invoke(
            worker,
            [CancellationToken.None]));
        await scanTask;

        Assert.Equal(0, poison.CreateConnectionCount);
    }

    [Fact]
    public async Task AddRoleValidationData_Should_UseEffectiveHybridAndOracleArtifactRoots()
    {
        using var hybridContentRoot = new TemporaryArtifactRoot();
        using var ignoredHybridConfiguredRoot = new TemporaryArtifactRoot();
        using ServiceProvider hybrid = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid",
            contentRoot: hybridContentRoot.Path,
            hybridArtifactRoot: ignoredHybridConfiguredRoot.Path);
        IEmailArtifactStore hybridArtifacts = hybrid
            .GetRequiredService<IEmailArtifactStore>();
        EmailArtifactMetadata hybridArtifact = await hybridArtifacts
            .PublishRunZipAsync(71, [1]);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                hybridContentRoot.Path,
                "App_Data",
                "RoleValidationEmail",
                "runs",
                "run-71",
                "zip")),
            Path.GetDirectoryName(hybridArtifact.StoragePath));
        Assert.False(Directory.Exists(ignoredHybridConfiguredRoot.Path));

        using var oracleRoot = new TemporaryArtifactRoot();
        using ServiceProvider oracle = BuildEmailExecutionServices(
            Environments.Production,
            "Oracle",
            oracleArtifactRoot: oracleRoot.Path);
        IEmailArtifactStore oracleArtifacts = oracle
            .GetRequiredService<IEmailArtifactStore>();
        EmailArtifactMetadata oracleArtifact = await oracleArtifacts
            .PublishRunZipAsync(71, [1]);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                oracleRoot.Path,
                "runs",
                "run-71",
                "zip")),
            Path.GetDirectoryName(oracleArtifact.StoragePath));
    }

    [Fact]
    public async Task DevelopmentOracle_Should_UseLocalArtifactRootWithFakeEmailProcessing()
    {
        using var contentRoot = new TemporaryArtifactRoot();
        using ServiceProvider services = BuildEmailExecutionServices(
            Environments.Development,
            "Oracle",
            contentRoot: contentRoot.Path,
            oracleArtifactRoot: string.Empty,
            transportMode: "Fake",
            recipientMode: "SafeRedirect");

        Assert.True(services
            .GetRequiredService<EmailProcessingCapability>()
            .IsEnabled);
        Assert.Single(services.GetServices<IHostedService>()
            .OfType<EmailScheduleWorker>());
        Assert.Single(services.GetServices<IHostedService>()
            .OfType<EmailRunPreparationWorker>());
        Assert.Single(services.GetServices<IHostedService>()
            .OfType<EmailDeliveryWorker>());

        IEmailArtifactStore artifacts = services
            .GetRequiredService<IEmailArtifactStore>();
        EmailArtifactMetadata artifact = await artifacts
            .PublishRunZipAsync(71, [1]);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                contentRoot.Path,
                "App_Data",
                "RoleValidationEmail",
                "runs",
                "run-71",
                "zip")),
            Path.GetDirectoryName(artifact.StoragePath));
    }

    [Fact]
    public async Task HybridWorker_Should_ScanWithoutResolvingOracleWriter()
    {
        var poison = new PoisonMasterOracleConnectionFactory();
        var scanSignal = new ScanSignalLogger();
        using ServiceProvider services = BuildEmailExecutionServices(
            Environments.Development,
            "Hybrid",
            poisonFactory: poison,
            workerLogger: scanSignal);
        EmailScheduleWorker worker = services
            .GetServices<IHostedService>()
            .OfType<EmailScheduleWorker>()
            .Single();

        await worker.StartAsync(CancellationToken.None);
        await scanSignal.ScanCompleted;
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, poison.CreateConnectionCount);
        Assert.Null(services.GetService<OracleEmailExecutionStore>());
        using IServiceScope scope = services.CreateScope();
        Assert.IsType<DevelopmentEmailExecutionStore>(
            scope.ServiceProvider.GetRequiredService<IEmailExecutionStore>());
    }

    [Fact]
    public void AddRoleValidationData_ShouldRegisterHistoryReaderForHybridAndOracle()
    {
        using ServiceProvider hybrid = BuildServices(
            Environments.Development,
            "Hybrid");
        using ServiceProvider oracle = BuildServices("QA", "Oracle");

        DevelopmentRoleVData developmentData =
            hybrid.GetRequiredService<DevelopmentRoleVData>();
        Assert.Same(
            developmentData,
            hybrid.GetRequiredService<IHistoryReader>());
        Assert.IsType<OracleHistoryReader>(
            oracle.GetRequiredService<IHistoryReader>());
        Assert.NotNull(hybrid.GetRequiredService<LoadChangeHistoryHandler>());
        Assert.NotNull(hybrid.GetRequiredService<LoadLoginHistoryHandler>());
        Assert.NotNull(oracle.GetRequiredService<LoadChangeHistoryHandler>());
        Assert.NotNull(oracle.GetRequiredService<LoadLoginHistoryHandler>());
    }

    [Fact]
    public void AddRoleValidationData_Should_RegisterHybridSourcesAndAllProviders()
    {
        using ServiceProvider services = BuildServices(
            environmentName: Environments.Development,
            dataSource: "Hybrid");

        Assert.IsType<DevelopmentRoleVData>(
            services.GetRequiredService<IApplicationReader>());
        Assert.IsType<HybridApplicationUserReader>(
            services.GetRequiredService<IApplicationUserReader>());
        DevelopmentRoleVData developmentData =
            services.GetRequiredService<DevelopmentRoleVData>();
        Assert.Same(
            developmentData,
            services.GetRequiredService<IAuthorizedUserReader>());
        Assert.Same(
            developmentData,
            services.GetRequiredService<ILoginAccessRecorder>());
        Assert.Same(
            developmentData,
            services.GetRequiredService<IRoleValidationAdministrationStore>());
        Assert.Same(
            developmentData,
            services.GetRequiredService<IRoleOwnerReader>());
        Assert.Same(
            developmentData,
            services.GetRequiredService<ISourceRoleMappingReader>());
        Assert.IsType<OracleEmployeeReader>(
            services.GetRequiredService<IEmployeeSearchReader>());
        Assert.NotNull(
            services.GetRequiredService<RoleOwnerAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<ApplicationAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<ValidationRoleAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<SourceMappingAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<AuthorizedUserAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<IMasterOracleConnectionFactory>());
        Assert.NotNull(
            services.GetRequiredService<IMaterialOracleConnectionFactory>());
        Assert.NotNull(
            services.GetRequiredService<IAppSimOracleConnectionFactory>());
        Assert.Equal(
            ["ERSN", "EVA", "ICPLB", "IDM", "MFM", "OPM", "PULL_LIST"],
            services.GetServices<ILegacyApplicationUserProvider>()
                .Select(provider => provider.ApplicationCode)
                .Order()
                .ToArray());
    }

    [Fact]
    public void AddRoleValidationData_Should_RegisterOracleReadersInQa()
    {
        using ServiceProvider services = BuildServices(
            environmentName: "QA",
            dataSource: "Oracle");

        Assert.IsType<OracleApplicationReader>(
            services.GetRequiredService<IApplicationReader>());
        Assert.IsType<OracleApplicationUserReader>(
            services.GetRequiredService<IApplicationUserReader>());
        Assert.IsType<OracleAuthorizedUserReader>(
            services.GetRequiredService<IAuthorizedUserReader>());
        Assert.IsType<OracleLoginAccessRecorder>(
            services.GetRequiredService<ILoginAccessRecorder>());
        Assert.NotNull(
            services.GetRequiredService<AuthenticationAccessEvaluator>());
        Assert.IsType<OracleRoleValidationAdministrationStore>(
            services.GetRequiredService<IRoleValidationAdministrationStore>());
        Assert.IsType<OracleRoleOwnerReader>(
            services.GetRequiredService<IRoleOwnerReader>());
        Assert.IsType<OracleSourceRoleMappingReader>(
            services.GetRequiredService<ISourceRoleMappingReader>());
        Assert.IsType<OracleEmployeeReader>(
            services.GetRequiredService<IEmployeeSearchReader>());
        Assert.NotNull(
            services.GetRequiredService<RoleOwnerAdministrationHandler>());
        Assert.Null(services.GetService<DevelopmentRoleVData>());
        Assert.NotNull(
            services.GetRequiredService<ApplicationAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<ValidationRoleAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<SourceMappingAdministrationHandler>());
        Assert.NotNull(
            services.GetRequiredService<AuthorizedUserAdministrationHandler>());
    }

    [Fact]
    public void AddRoleValidationData_Should_PreserveTemporaryDevelopmentMode()
    {
        using ServiceProvider services = BuildServices(
            environmentName: Environments.Development,
            dataSource: "Temporary",
            includeConnectionStrings: false);

        Assert.IsType<TemporaryFirstSliceData>(
            services.GetRequiredService<IApplicationReader>());
        Assert.IsType<TemporaryFirstSliceData>(
            services.GetRequiredService<IApplicationUserReader>());
        Assert.Null(services.GetService<IAuthorizedUserReader>());
        Assert.Null(services.GetService<ILoginAccessRecorder>());
        Assert.Null(services.GetService<IRoleOwnerReader>());
        Assert.Null(services.GetService<IEmployeeSearchReader>());
        Assert.Null(services.GetService<IRoleValidationAdministrationStore>());
        Assert.Null(services.GetService<RoleOwnerAdministrationHandler>());
        Assert.Null(services.GetService<SourceMappingAdministrationHandler>());
        Assert.Null(services.GetService<AuthorizedUserAdministrationHandler>());
        Assert.Null(services.GetService<IEmailExecutionStore>());
        Assert.Null(services.GetService<EmailScheduleAdministrationHandler>());
        Assert.Null(services.GetService<CreateScheduledRunHandler>());
        Assert.Null(services.GetService<CreateRunNowHandler>());
        Assert.Null(services.GetService<IOwnerScopeBuilder>());
        Assert.Null(services.GetService<IOwnerWorkbookExporter>());
        Assert.Null(services.GetService<IEmailArtifactStore>());
        Assert.Null(services.GetService<IEmailRunZipBuilder>());
        Assert.Null(services.GetService<PrepareEmailRunHandler>());
        Assert.Null(services.GetService<PrepareEmailDeliveryArtifactHandler>());
        Assert.Null(services.GetService<BuildEmailRunZipHandler>());
        Assert.Null(services.GetService<EmailRunPreparationWorker>());
        Assert.Null(services.GetService<EmailConfigurationSnapshot>());
        EmailProcessingCapability capability = services
            .GetRequiredService<EmailProcessingCapability>();
        Assert.False(capability.IsEnabled);
        Assert.Null(services.GetService<EmailScheduleWorker>());
        Assert.Null(services.GetService<EmailDeliveryWorker>());
        Assert.Null(services.GetService<ProcessEmailDeliveryHandler>());
    }

    [Fact]
    public void AddRoleValidationData_Should_DecryptEncryptedConnectionStrings()
    {
        using ServiceProvider services = BuildServices(
            environmentName: Environments.Development,
            dataSource: "Hybrid",
            encryptedConnectionStrings: true);

        using var connection = services
            .GetRequiredService<IMasterOracleConnectionFactory>()
            .CreateConnection();

        Assert.Equal(
            "Data Source=fake-master;User Id=fake;Password=fake",
            connection.ConnectionString);
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Temporary")]
    public void AddRoleValidationData_Should_RejectDevelopmentOnlySourcesInQa(
        string dataSource)
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuildServices("QA", dataSource));
    }

    private static ServiceProvider BuildServices(
        string environmentName,
        string dataSource,
        bool includeConnectionStrings = true,
        bool encryptedConnectionStrings = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = dataSource,
            ["Security:TextEncryption:EncryptedConfiguration"] =
                encryptedConnectionStrings.ToString(),
            ["Security:TextEncryption:Passphrase"] =
                encryptedConnectionStrings ? "test-passphrase" : null
        };

        if (includeConnectionStrings)
        {
            const string plainConnectionString =
                "Data Source=fake-master;User Id=fake;Password=fake";
            const string encryptedConnectionString =
                "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh91Kv5k17m6dUqMdCW+OIEVM8z4ssQruOzGOweNe6MPBxAjG2riYcH9cKrReKQgOK+wbU5IYJ/lI0U8q6sc/Zop";
            string configuredConnectionString = encryptedConnectionStrings
                ? encryptedConnectionString
                : plainConnectionString;

            settings["ConnectionStrings:Master"] =
                configuredConnectionString;
            settings["ConnectionStrings:Material"] =
                configuredConnectionString;
            settings["ConnectionStrings:AppSim"] =
                configuredConnectionString;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName)
            .Returns(environmentName);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoleValidationData(
            configuration,
            environment.Object);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildEmailExecutionServices(
        string environmentName,
        string dataSource,
        TimeProvider? timeProvider = null,
        IMasterOracleConnectionFactory? poisonFactory = null,
        ILogger<EmailScheduleWorker>? workerLogger = null,
        string? contentRoot = null,
        string? hybridArtifactRoot = null,
        string? oracleArtifactRoot = null,
        string? transportMode = null,
        string? recipientMode = null,
        Action<IDictionary<string, string?>>? configureEmail = null)
    {
        const string connectionString =
            "Data Source=fake-master;User Id=fake;Password=fake";
        bool production = environmentName == Environments.Production;
        string contentRootPath = contentRoot ?? Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "RoleValidationServiceTests"));
        string configuredOracleArtifactRoot = oracleArtifactRoot ?? Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "RoleValidationOracleArtifacts"));
        var settings = new Dictionary<string, string?>
        {
            ["RoleValidation:DataSource"] = dataSource,
            ["Security:TextEncryption:EncryptedConfiguration"] = "false",
            ["ConnectionStrings:Master"] = connectionString,
            ["ConnectionStrings:Material"] = connectionString,
            ["ConnectionStrings:AppSim"] = connectionString,
            ["Email:TransportMode"] = transportMode ??
                (string.Equals(dataSource, "Hybrid", StringComparison.OrdinalIgnoreCase)
                    ? "Fake"
                    : "ApiEmail"),
            ["Email:RecipientMode"] = recipientMode ??
                (production ? "RoleOwner" : "SafeRedirect"),
            ["Email:SafeRedirectEmployeeNo"] = "C2001234",
            ["Email:ArtifactRootPath"] = dataSource == "Hybrid"
                ? hybridArtifactRoot ?? "ignored-relative-artifacts"
                : configuredOracleArtifactRoot,
            ["Email:PreSubmitRetry:MaxAttempts"] = "3",
            ["Email:PreSubmitRetry:DelayMinutes:0"] = "5",
            ["Email:PreSubmitRetry:DelayMinutes:1"] = "15",
            ["Email:Content:SubjectTemplate"] =
                "[RoleValidation] Annual access review - {ApplicationName}",
            ["Email:Content:BodyTemplate"] =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}",
            ["Email:ApiEmail:BaseUrl"] = "https://api-email.example",
            ["Email:ApiEmail:Route"] = "/API/v2/EmailCenterRequest",
            ["Email:ApiEmail:ApplicationName"] = "RoleValidation",
            ["Email:ApiEmail:BearerToken"] = "issued-machine-token",
            ["Email:ApiEmail:TimeoutSeconds"] = "30"
        };
        configureEmail?.Invoke(settings);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName)
            .Returns(environmentName);
        environment.SetupGet(item => item.ContentRootPath)
            .Returns(contentRootPath);
        var services = new ServiceCollection();
        services.AddLogging();
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        if (workerLogger is not null)
        {
            services.AddSingleton(workerLogger);
        }

        EmailProcessingCapability capability =
            RoleValidationServiceRegistration.CaptureEmailProcessingCapability(
                configuration,
                environment.Object);
        services.AddRoleValidationData(
            configuration,
            environment.Object,
            capability);
        if (poisonFactory is not null)
        {
            services.RemoveAll<IMasterOracleConnectionFactory>();
            services.AddSingleton(poisonFactory);
        }

        services.AddRoleValidationEmailOptions(configuration, environment.Object);
        services.AddScoped<LoadApplicationUserHandler>();
        services.AddSingleton<IApplicationUserWorkbookExporter,
            OpenXmlApplicationUserWorkbookExporter>();
        if (capability.IsEnabled)
        {
            services.AddHostedService<EmailScheduleWorker>(provider =>
                provider.GetRequiredService<EmailScheduleWorker>());
            services.AddHostedService<EmailRunPreparationWorker>(provider =>
                provider.GetRequiredService<EmailRunPreparationWorker>());
            services.AddHostedService<EmailDeliveryWorker>(provider =>
                provider.GetRequiredService<EmailDeliveryWorker>());
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static void AssertEmailGraphLifetimes<TStore>(
        ServiceProvider services,
        bool storeIsSingleton)
        where TStore : IEmailExecutionStore
    {
        using IServiceScope first = services.CreateScope();
        using IServiceScope second = services.CreateScope();
        IEmailExecutionStore firstStore = first.ServiceProvider
            .GetRequiredService<IEmailExecutionStore>();
        IEmailExecutionStore sameScopeStore = first.ServiceProvider
            .GetRequiredService<IEmailExecutionStore>();
        IEmailExecutionStore secondStore = second.ServiceProvider
            .GetRequiredService<IEmailExecutionStore>();

        Assert.IsType<TStore>(firstStore);
        Assert.Same(firstStore, sameScopeStore);
        if (storeIsSingleton)
        {
            Assert.Same(firstStore, secondStore);
        }
        else
        {
            Assert.NotSame(firstStore, secondStore);
        }

        AssertScoped<EmailScheduleAdministrationHandler>(first, second);
        AssertScoped<CreateScheduledRunHandler>(first, second);
        AssertScoped<CreateRunNowHandler>(first, second);
        AssertScoped<IOwnerScopeBuilder>(first, second);
        AssertScoped<PrepareEmailRunHandler>(first, second);
        AssertScoped<PrepareEmailDeliveryArtifactHandler>(first, second);
        AssertScoped<BuildEmailRunZipHandler>(first, second);
        IOwnerWorkbookExporter workbookExporter = services
            .GetRequiredService<IOwnerWorkbookExporter>();
        IEmailArtifactStore artifactStore = services
            .GetRequiredService<IEmailArtifactStore>();
        IEmailRunZipBuilder zipBuilder = services
            .GetRequiredService<IEmailRunZipBuilder>();
        Assert.IsType<OpenXmlErsnOwnerWorkbookExporter>(workbookExporter);
        Assert.IsType<FileSystemEmailArtifactStore>(artifactStore);
        Assert.IsType<ZipApplicationWorkbookBuilder>(zipBuilder);
        Assert.Same(workbookExporter,
            services.GetRequiredService<IOwnerWorkbookExporter>());
        Assert.Same(artifactStore,
            services.GetRequiredService<IEmailArtifactStore>());
        Assert.Same(zipBuilder,
            services.GetRequiredService<IEmailRunZipBuilder>());
        EmailProcessingCapability capability = services
            .GetRequiredService<EmailProcessingCapability>();
        if (capability.IsEnabled)
        {
            EmailScheduleWorker schedule = services
                .GetRequiredService<EmailScheduleWorker>();
            EmailRunPreparationWorker preparation = services
                .GetRequiredService<EmailRunPreparationWorker>();
            EmailDeliveryWorker delivery = services
                .GetRequiredService<EmailDeliveryWorker>();
            Assert.Same(
                schedule,
                Assert.Single(services.GetServices<IHostedService>()
                    .OfType<EmailScheduleWorker>()));
            Assert.Same(
                preparation,
                Assert.Single(services.GetServices<IHostedService>()
                    .OfType<EmailRunPreparationWorker>()));
            Assert.Same(
                delivery,
                Assert.Single(services.GetServices<IHostedService>()
                    .OfType<EmailDeliveryWorker>()));
        }
        else
        {
            Assert.Null(services.GetService<EmailScheduleWorker>());
            Assert.Null(services.GetService<EmailRunPreparationWorker>());
            Assert.Null(services.GetService<EmailDeliveryWorker>());
            Assert.DoesNotContain(
                services.GetServices<IHostedService>(),
                service => service is EmailScheduleWorker or
                    EmailRunPreparationWorker or
                    EmailDeliveryWorker);
        }
    }

    private static void AssertScoped<TService>(
        IServiceScope first,
        IServiceScope second)
        where TService : class
    {
        TService firstInstance = first.ServiceProvider
            .GetRequiredService<TService>();
        Assert.Same(
            firstInstance,
            first.ServiceProvider.GetRequiredService<TService>());
        Assert.NotSame(
            firstInstance,
            second.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class TemporaryArtifactRoot : IDisposable
    {
        private readonly string _parent;

        public TemporaryArtifactRoot()
        {
            _parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RoleValidationServiceRegistrationTests"));
            Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                _parent,
                Guid.NewGuid().ToString("N")));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            string relative = System.IO.Path.GetRelativePath(_parent, Path);
            if (System.IO.Path.IsPathRooted(relative) ||
                relative == ".." ||
                relative.StartsWith(
                    ".." + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) ||
                (new DirectoryInfo(Path).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Refusing unsafe temporary-root cleanup.");
            }

            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class PoisonMasterOracleConnectionFactory :
        IMasterOracleConnectionFactory
    {
        private int _createConnectionCount;

        public int CreateConnectionCount =>
            Volatile.Read(ref _createConnectionCount);

        public DbConnection CreateConnection()
        {
            Interlocked.Increment(ref _createConnectionCount);
            throw new InvalidOperationException(
                "The Hybrid graph must not open the Oracle writer.");
        }
    }

    private sealed class ScanSignalLogger : ILogger<EmailScheduleWorker>
    {
        private readonly TaskCompletionSource _scanCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ScanCompleted => _scanCompleted.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel is LogLevel.Debug or LogLevel.Information)
            {
                _scanCompleted.TrySetResult();
            }
        }
    }
}
