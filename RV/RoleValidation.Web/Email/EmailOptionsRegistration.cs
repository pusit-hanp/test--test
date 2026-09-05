using Microsoft.Extensions.Options;

namespace RoleValidation.Web.Email;

public static class EmailOptionsRegistration
{
    public static IServiceCollection AddRoleValidationEmailOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName));
        // Email is optional for the web app. Keep validation on resolution as a
        // backstop; capability/HTTP guards prevent invalid email work from starting.
        services.AddSingleton<IValidateOptions<EmailOptions>>(
            new EmailOptionsValidator(
                environment.EnvironmentName,
                configuration["RoleValidation:DataSource"]));
        services.AddSingleton(provider => provider
            .GetRequiredService<IOptions<EmailOptions>>()
            .Value);

        return services;
    }
}
