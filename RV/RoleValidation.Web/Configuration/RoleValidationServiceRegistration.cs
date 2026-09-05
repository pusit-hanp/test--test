using Microsoft.Extensions.DependencyInjection.Extensions;
using RoleValidation.Application.Administration;
using RoleValidation.Application.Authentication;
using RoleValidation.Application.Authorization;
using RoleValidation.Application.Applications;
using RoleValidation.Application.Email;
using RoleValidation.Application.Exports;
using RoleValidation.Application.Employees;
using RoleValidation.Application.History;
using RoleValidation.Application.Roles;
using RoleValidation.Application.RoleOwners;
using RoleValidation.Application.Security;
using RoleValidation.Application.SourceMappings;
using RoleValidation.Application.Users;
using RoleValidation.Core.Features.Authorization;
using RoleValidation.Infrastructure.Authorization;
using RoleValidation.Infrastructure.Administration;
using RoleValidation.Infrastructure.Applications;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;
using RoleValidation.Infrastructure.Development;
using RoleValidation.Infrastructure.Email;
using RoleValidation.Infrastructure.Exports;
using RoleValidation.Infrastructure.Employees;
using RoleValidation.Infrastructure.History;
using RoleValidation.Infrastructure.Roles;
using RoleValidation.Infrastructure.RoleOwners;
using RoleValidation.Infrastructure.Security;
using RoleValidation.Infrastructure.SourceMappings;
using RoleValidation.Infrastructure.Temporary;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Configuration;

public sealed class EmailProcessingCapability
{
    public EmailProcessingCapability(bool isEnabled) =>
        IsEnabled = isEnabled;

    public bool IsEnabled { get; }
}

public static class RoleValidationServiceRegistration
{
    public static IServiceCollection AddRoleValidationData(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        EmailProcessingCapability capability =
            CaptureEmailProcessingCapability(configuration, environment);
        return AddRoleValidationData(
            services,
            configuration,
            environment,
            capability);
    }

    public static IServiceCollection AddRoleValidationData(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        EmailProcessingCapability emailProcessingCapability)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(emailProcessingCapability);

        services.AddSingleton(emailProcessingCapability);

        string dataSource = configuration["RoleValidation:DataSource"]
            ?? throw new InvalidOperationException(
                "Configuration value 'RoleValidation:DataSource' is not configured.");

        if (string.Equals(
                dataSource,
                "Temporary",
                StringComparison.OrdinalIgnoreCase))
        {
            RegisterTemporary(services, environment);
        }
        else if (string.Equals(
                     dataSource,
                     "Hybrid",
                     StringComparison.OrdinalIgnoreCase))
        {
            RegisterHybrid(services, configuration, environment);
        }
        else if (string.Equals(
                     dataSource,
                     "Oracle",
                     StringComparison.OrdinalIgnoreCase))
        {
            RegisterOracle(services, configuration);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported RoleValidation data source '{dataSource}'.");
        }

        if (!string.Equals(
                dataSource,
                "Temporary",
                StringComparison.OrdinalIgnoreCase))
        {
            RegisterEmailExecutionServices(
                services,
                configuration,
                dataSource,
                environment,
                emailProcessingCapability);
        }

        services.AddScoped<EmployeeIdentityResolver>();
        services.AddScoped<AuthenticationAccessEvaluator>();

        return services;
    }

    public static EmailProcessingCapability CaptureEmailProcessingCapability(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        EmailOptions options = configuration
            .GetSection(EmailOptions.SectionName)
            .Get<EmailOptions>() ?? new EmailOptions();
        bool hasValidContent = EmailOptionsValidator.TryValidateContent(
            options.Content,
            out _);
        string? dataSource = configuration["RoleValidation:DataSource"];
        string? transportMode = configuration["Email:TransportMode"];
        string? recipientMode = configuration["Email:RecipientMode"];
        bool isDevelopmentFake =
            environment.EnvironmentName == Environments.Development &&
            dataSource is "Hybrid" or "Oracle" &&
            transportMode == "Fake" &&
            recipientMode == "SafeRedirect";
        bool isApprovedApiEmailTuple =
            transportMode == "ApiEmail" &&
            ((environment.EnvironmentName == "QA" &&
                    dataSource == "Oracle" &&
                    recipientMode == "SafeRedirect") ||
                (environment.EnvironmentName == Environments.Production &&
                    dataSource == "Oracle" &&
                    recipientMode == "RoleOwner"));
        bool hasCompleteApiEmailConfiguration = false;
        if (isApprovedApiEmailTuple)
        {
            hasCompleteApiEmailConfiguration =
                EmailOptionsValidator.TryCreateApiEmailSettings(
                    options,
                    environment.EnvironmentName,
                    out _,
                    out _);
        }

        bool isEnabled = hasValidContent &&
            (isDevelopmentFake ||
                (isApprovedApiEmailTuple && hasCompleteApiEmailConfiguration));
        return new EmailProcessingCapability(isEnabled);
    }

    private static void RegisterTemporary(
        IServiceCollection services,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Temporary RoleValidation data is allowed only in Development.");
        }

        services.AddSingleton<TemporaryFirstSliceData>();
        services.AddSingleton<IApplicationReader>(services =>
            services.GetRequiredService<TemporaryFirstSliceData>());
        services.AddSingleton<IApplicationUserReader>(services =>
            services.GetRequiredService<TemporaryFirstSliceData>());
        services.AddSingleton<IEmployeeReader>(services =>
            services.GetRequiredService<TemporaryFirstSliceData>());
        services.AddSingleton<ISourceRoleMappingReader>(services =>
            services.GetRequiredService<TemporaryFirstSliceData>());
        services.AddSingleton<IValidationRoleReader>(services =>
            services.GetRequiredService<TemporaryFirstSliceData>());
    }

    private static void RegisterHybrid(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Hybrid RoleValidation data is allowed only in Development.");
        }

        RegisterConnectionFactoriesAndProviders(services, configuration);

        services.AddSingleton<DevelopmentRoleVData>();
        services.AddSingleton<IApplicationReader>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddScoped<OracleApplicationUserReader>();
        services.AddScoped<IApplicationUserReader,
            HybridApplicationUserReader>();
        services.AddScoped<OracleEmployeeReader>();
        services.AddScoped<IEmployeeReader>(services =>
            services.GetRequiredService<OracleEmployeeReader>());
        services.AddScoped<IEmployeeSearchReader>(services =>
            services.GetRequiredService<OracleEmployeeReader>());
        services.AddSingleton<IAuthorizedUserReader>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<ILoginAccessRecorder>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<IHistoryReader>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<IRoleValidationAdministrationStore>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<IRoleOwnerReader>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<ISourceRoleMappingReader>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<IValidationRoleReader>(services =>
            services.GetRequiredService<DevelopmentRoleVData>());
        services.AddSingleton<IEmailExecutionStore>(services =>
            new DevelopmentEmailExecutionStore(
                services.GetRequiredService<DevelopmentRoleVData>(),
                services.GetRequiredService<EmailOptions>()
                    .SafeRedirectEmployeeNo,
                services.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IEmailManagementReader>(services =>
            (IEmailManagementReader)services
                .GetRequiredService<IEmailExecutionStore>());
        services.AddScoped<ApplicationAdministrationHandler>();
        services.AddScoped<ValidationRoleAdministrationHandler>();
        services.AddScoped<RoleOwnerAdministrationHandler>();
        services.AddScoped<SourceMappingAdministrationHandler>();
        services.AddScoped<AuthorizedUserAdministrationHandler>();
        services.AddScoped<LoadChangeHistoryHandler>();
        services.AddScoped<LoadLoginHistoryHandler>();
    }

    private static void RegisterOracle(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterConnectionFactoriesAndProviders(services, configuration);

        services.AddScoped<IApplicationReader, OracleApplicationReader>();
        services.AddScoped<IApplicationUserReader,
            OracleApplicationUserReader>();
        services.AddScoped<OracleEmployeeReader>();
        services.AddScoped<IEmployeeReader>(services =>
            services.GetRequiredService<OracleEmployeeReader>());
        services.AddScoped<IEmployeeSearchReader>(services =>
            services.GetRequiredService<OracleEmployeeReader>());
        services.AddScoped<IAuthorizedUserReader,
            OracleAuthorizedUserReader>();
        services.AddScoped<ILoginAccessRecorder,
            OracleLoginAccessRecorder>();
        services.AddScoped<IHistoryReader, OracleHistoryReader>();
        services.AddScoped<IRoleValidationAdministrationStore,
            OracleRoleValidationAdministrationStore>();
        services.AddScoped<IRoleOwnerReader, OracleRoleOwnerReader>();
        services.AddScoped<ISourceRoleMappingReader,
            OracleSourceRoleMappingReader>();
        services.AddScoped<IValidationRoleReader,
            OracleValidationRoleReader>();
        services.AddScoped<IEmailExecutionStore>(services =>
            new OracleEmailExecutionStore(
                services.GetRequiredService<IMasterOracleConnectionFactory>(),
                services.GetRequiredService<EmailOptions>()
                    .SafeRedirectEmployeeNo,
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<EmailConfigurationSnapshot>()));
        services.AddScoped<IEmailManagementReader>(services =>
            (IEmailManagementReader)services
                .GetRequiredService<IEmailExecutionStore>());
        services.AddScoped<ApplicationAdministrationHandler>();
        services.AddScoped<ValidationRoleAdministrationHandler>();
        services.AddScoped<RoleOwnerAdministrationHandler>();
        services.AddScoped<SourceMappingAdministrationHandler>();
        services.AddScoped<AuthorizedUserAdministrationHandler>();
        services.AddScoped<LoadChangeHistoryHandler>();
        services.AddScoped<LoadLoginHistoryHandler>();
    }

    private static void RegisterEmailExecutionServices(
        IServiceCollection services,
        IConfiguration configuration,
        string dataSource,
        IHostEnvironment environment,
        EmailProcessingCapability emailProcessingCapability)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<EmailScheduleAdministrationHandler>();
        services.AddScoped<CreateScheduledRunHandler>();
        services.AddScoped<CreateRunNowHandler>();
        services.AddScoped<ResolveUnknownDeliveryHandler>();
        services.AddScoped<IOwnerScopeBuilder, ErsnOwnerScopeBuilder>();
        services.AddSingleton<IOwnerWorkbookExporter,
            OpenXmlErsnOwnerWorkbookExporter>();
        services.AddSingleton<IEmailArtifactStore>(provider =>
            new FileSystemEmailArtifactStore(ResolveArtifactRoot(
                dataSource,
                provider.GetRequiredService<EmailOptions>(),
                environment)));
        services.AddSingleton<IEmailRunZipBuilder, ZipApplicationWorkbookBuilder>();
        services.AddScoped<PrepareEmailRunHandler>();
        services.AddScoped<PrepareEmailDeliveryArtifactHandler>();
        services.AddScoped<BuildEmailRunZipHandler>();
        services.AddSingleton(provider =>
        {
            EmailOptions options = provider.GetRequiredService<EmailOptions>();
            return new EmailConfigurationSnapshot(
                dataSource,
                ToSnapshotTransportMode(options.TransportMode),
                ToSnapshotRecipientMode(options.RecipientMode));
        });

        if (!emailProcessingCapability.IsEnabled)
        {
            return;
        }

        services.AddScoped<EmailRecipientPolicy>(services =>
            new EmailRecipientPolicy(
                services.GetRequiredService<IEmployeeReader>(),
                services.GetRequiredService<EmailOptions>()
                    .SafeRedirectEmployeeNo));
        services.AddScoped<IEmailContentSource, ConfiguredEmailContentSource>();
        string transportMode = configuration["Email:TransportMode"] ?? string.Empty;
        if (transportMode == "Fake")
        {
            services.AddSingleton<FakeEmailTransport>();
            services.AddSingleton<IEmailTransport>(provider =>
                provider.GetRequiredService<FakeEmailTransport>());
        }
        else if (transportMode == "ApiEmail")
        {
            EmailOptions options = configuration
                .GetSection(EmailOptions.SectionName)
                .Get<EmailOptions>() ?? new EmailOptions();
            if (!EmailOptionsValidator.TryCreateApiEmailSettings(
                    options,
                    environment.EnvironmentName,
                    out ApiEmailTransportSettings? settings,
                    out string? failure))
            {
                throw new InvalidOperationException(failure);
            }

            services.AddSingleton(settings!);
            services.AddHttpClient<ApiEmailTransport>(client =>
                client.Timeout = TimeSpan.FromSeconds(
                    options.ApiEmail.TimeoutSeconds))
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false
                    });
            services.AddScoped<IEmailTransport>(provider =>
                provider.GetRequiredService<ApiEmailTransport>());
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported email transport '{transportMode}'.");
        }

        services.AddScoped<ProcessEmailDeliveryHandler>();
        services.AddSingleton<EmailScheduleWorker>();
        services.AddSingleton<EmailRunPreparationWorker>();
        services.AddSingleton<EmailDeliveryWorker>();
    }

    private static string ResolveArtifactRoot(
        string dataSource,
        EmailOptions options,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(environment.ContentRootPath))
            {
                throw new InvalidOperationException(
                    "Content root path is required for Development email artifacts.");
            }

            return Path.GetFullPath(Path.Combine(
                environment.ContentRootPath,
                "App_Data",
                "RoleValidationEmail"));
        }

        return options.ArtifactRootPath;
    }

    private static string ToSnapshotTransportMode(string value) =>
        value switch
        {
            "Fake" => "FAKE",
            "ApiEmail" => "API_EMAIL",
            _ => value
        };

    private static string ToSnapshotRecipientMode(string value) =>
        value switch
        {
            "SafeRedirect" => "SAFE_REDIRECT",
            "RoleOwner" => "ROLE_OWNER",
            _ => value
        };

    private static void RegisterConnectionFactoriesAndProviders(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ITextEncryptionService? textEncryption =
            CreateTextEncryptionService(configuration);
        bool encryptedConfiguration = configuration.GetValue<bool>(
            "Security:TextEncryption:EncryptedConfiguration");

        if (textEncryption is not null)
        {
            services.AddSingleton(textEncryption);
        }

        if (encryptedConfiguration && textEncryption is null)
        {
            throw new InvalidOperationException(
                "Configuration value 'Security:TextEncryption:Passphrase' " +
                "is required when encrypted configuration is enabled.");
        }

        string master = GetRequiredConnectionString(
            configuration,
            "Master",
            encryptedConfiguration,
            textEncryption);
        string material = GetRequiredConnectionString(
            configuration,
            "Material",
            encryptedConfiguration,
            textEncryption);
        string appSim = GetRequiredConnectionString(
            configuration,
            "AppSim",
            encryptedConfiguration,
            textEncryption);

        services.AddSingleton<IMasterOracleConnectionFactory>(
            new MasterOracleConnectionFactory(master));
        services.AddSingleton<IMaterialOracleConnectionFactory>(
            new MaterialOracleConnectionFactory(material));
        services.AddSingleton<IAppSimOracleConnectionFactory>(
            new AppSimOracleConnectionFactory(appSim));

        services.AddScoped<ILegacyApplicationUserProvider,
            ErsnApplicationUserProvider>();
        services.AddScoped<ILegacyApplicationUserProvider,
            PullListApplicationUserProvider>();
        services.AddScoped<ILegacyApplicationUserProvider,
            IdmApplicationUserProvider>();
        services.AddScoped<ILegacyApplicationUserProvider,
            MfmApplicationUserProvider>();
        services.AddScoped<ILegacyApplicationUserProvider,
            EvaApplicationUserProvider>();
        services.AddScoped<ILegacyApplicationUserProvider,
            IcProgrammingApplicationUserProvider>();
        services.AddScoped<ILegacyApplicationUserProvider,
            OpenMarketApplicationUserProvider>();
    }

    private static string GetRequiredConnectionString(
        IConfiguration configuration,
        string name,
        bool encryptedConfiguration,
        ITextEncryptionService? textEncryption)
    {
        string? value = configuration.GetConnectionString(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Connection string '{name}' is not configured.");
        }

        return encryptedConfiguration
            ? textEncryption!.Decrypt(value)
            : value;
    }

    private static ITextEncryptionService? CreateTextEncryptionService(
        IConfiguration configuration)
    {
        string? passphrase =
            configuration["Security:TextEncryption:Passphrase"];

        return string.IsNullOrWhiteSpace(passphrase)
            ? null
            : new AesTextEncryptionService(passphrase);
    }
}
