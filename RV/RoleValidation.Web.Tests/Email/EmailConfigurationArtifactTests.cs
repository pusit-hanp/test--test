using System.Text.Json;

namespace RoleValidation.Web.Tests.Email;

public sealed class EmailConfigurationArtifactTests
{
    [Fact]
    public void BaseAppSettings_Should_NotContainEmailDefaults()
    {
        using JsonDocument document = ReadJsonArtifact("appsettings.json");

        Assert.False(
            document.RootElement.TryGetProperty("Email", out _));
    }

    [Fact]
    public void DevelopmentAppSettings_Should_ContainOnlyApprovedEmailShape()
    {
        using JsonDocument document =
            ReadJsonArtifact(Path.Combine(
                "ConfigurationArtifacts",
                "appsettings.Development.json"));
        JsonElement root = document.RootElement;
        JsonElement roleValidation = root.GetProperty("RoleValidation");
        JsonElement email = root.GetProperty("Email");
        JsonElement retry = email.GetProperty("PreSubmitRetry");

        Assert.Equal(
            ["DataSource"],
            SortedPropertyNames(roleValidation));
        Assert.Equal("Hybrid", roleValidation
            .GetProperty("DataSource")
            .GetString());
        Assert.Contains("PreSubmitRetry", SortedPropertyNames(email));
        Assert.Contains("PreparingStaleMinutes", SortedPropertyNames(email));
        Assert.Contains("RecipientMode", SortedPropertyNames(email));
        Assert.Contains("SafeRedirectEmployeeNo", SortedPropertyNames(email));
        Assert.Contains("TransportMode", SortedPropertyNames(email));
        AssertApprovedContent(email);
        Assert.Equal("Fake", email
            .GetProperty("TransportMode")
            .GetString());
        Assert.Equal("SafeRedirect", email
            .GetProperty("RecipientMode")
            .GetString());
        AssertValidConfiguredEmployeeNo(email
            .GetProperty("SafeRedirectEmployeeNo")
            .GetString());
        Assert.Equal(
            ["DelayMinutes", "MaxAttempts"],
            SortedPropertyNames(retry));
        Assert.Equal(3, retry
            .GetProperty("MaxAttempts")
            .GetInt32());
        Assert.Equal(
            [5, 15],
            retry.GetProperty("DelayMinutes")
                .EnumerateArray()
                .Select(item => item.GetInt32())
                .ToArray());

        Assert.DoesNotContain(
            EnumeratePropertyNames(email),
            IsForbiddenConfigurationKey);
    }

    [Theory]
    [InlineData("appsettings.QA.json", "SafeRedirect")]
    [InlineData("appsettings.Production.json", "RoleOwner")]
    public void OracleAppSettings_Should_KeepApiEmailSecretsExternal(
        string fileName,
        string expectedRecipientMode)
    {
        using JsonDocument document = ReadJsonArtifact(Path.Combine(
            "ConfigurationArtifacts",
            fileName));
        JsonElement root = document.RootElement;
        JsonElement email = root.GetProperty("Email");
        JsonElement apiEmail = email.GetProperty("ApiEmail");

        Assert.Equal("Oracle", root
            .GetProperty("RoleValidation")
            .GetProperty("DataSource")
            .GetString());
        Assert.Equal("ApiEmail", email
            .GetProperty("TransportMode")
            .GetString());
        Assert.Equal(expectedRecipientMode, email
            .GetProperty("RecipientMode")
            .GetString());
        AssertApprovedContent(email);
        AssertValidConfiguredEmployeeNo(email
            .GetProperty("SafeRedirectEmployeeNo")
            .GetString());
        Assert.Equal(string.Empty, email
            .GetProperty("ArtifactRootPath")
            .GetString());
        Assert.Equal(string.Empty, apiEmail
            .GetProperty("BaseUrl")
            .GetString());
        Assert.Equal(string.Empty, apiEmail
            .GetProperty("BearerToken")
            .GetString());
        Assert.Equal("/API/v2/EmailCenterRequest", apiEmail
            .GetProperty("Route")
            .GetString());
    }

    private static void AssertApprovedContent(JsonElement email)
    {
        Assert.Contains("Content", SortedPropertyNames(email));
        JsonElement content = email.GetProperty("Content");
        string? subject = content.GetProperty("SubjectTemplate").GetString();
        string? body = content.GetProperty("BodyTemplate").GetString();
        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("{ApplicationName}", subject);
        Assert.Contains("{ApplicationName}", body);
        Assert.Contains("{OwnerEmployeeNo}", body);
    }

    private static JsonDocument ReadJsonArtifact(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(
            File.Exists(path),
            $"Expected copied configuration artifact '{fileName}'.");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string[] SortedPropertyNames(JsonElement element)
    {
        return element
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumeratePropertyNames(
        JsonElement element)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            yield return property.Name;

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (string nestedName in
                    EnumeratePropertyNames(property.Value))
                {
                    yield return nestedName;
                }
            }
        }
    }

    private static bool IsForbiddenConfigurationKey(string key)
    {
        string[] forbiddenTerms =
        [
            "endpoint",
            "credential",
            "secret",
            "token",
            "url",
            "path",
            "root"
        ];

        return forbiddenTerms.Any(term =>
            key.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertValidConfiguredEmployeeNo(string? employeeNo)
    {
        string value = Assert.IsType<string>(employeeNo);
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.InRange(value.Length, 1, 8);
        Assert.Equal(value.Trim(), value);
        Assert.All(
            value,
            character => Assert.True(
                character is >= '0' and <= '9' or
                    >= 'A' and <= 'Z' or
                    >= 'a' and <= 'z'));
    }
}
