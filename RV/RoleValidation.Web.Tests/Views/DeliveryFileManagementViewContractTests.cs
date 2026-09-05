using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.Email;

namespace RoleValidation.Web.Tests.Views;

public sealed class DeliveryFileManagementViewContractTests
{
    [Fact]
    public void ViewSources_Should_KeepArtifactsServerOwnedAndLinkFromRunDetail()
    {
        string deliveryFiles = Read("Views", "DeliveryFiles", "Index.cshtml");
        string runDetail = Read("Views", "EmailRuns", "Index.cshtml");

        foreach (string forbidden in new[]
                 {
                     "EmailArtifactMetadata",
                     "GetEmailRunZipArtifactAsync",
                     "GetEmailDeliveryWorkbookArtifactAsync",
                     "ReadRunZipAsync",
                     "ReadOwnerWorkbookAsync",
                     "ZipFileName",
                     "WorkbookFileName",
                     "StoragePath",
                     "OwnerEmail",
                     "Subject",
                     "Body"
                 })
        {
            Assert.DoesNotContain(forbidden, deliveryFiles);
        }

        Assert.Single(Regex.Matches(
            runDetail,
            "asp-controller=\"DeliveryFiles\"").Cast<
                System.Text.RegularExpressions.Match>());
        Assert.Contains("asp-route-id=\"@Model.Run.EmailRunId\"", runDetail);
        Assert.DoesNotContain(">Run detail</a>", Read(
            "Views",
            "Shared",
            "_ManagementNavigation.cshtml"));
        Assert.Contains("Model.RecentRuns", deliveryFiles);
        Assert.Contains("name=\"applicationId\"", deliveryFiles);
        Assert.Contains("asp-route-id=\"@run.EmailRunId\"", deliveryFiles);
    }

    [Fact]
    public async Task DirectMenu_Should_RenderRecentDownloadableRunsForAdmin()
    {
        string html = WebUtility.HtmlDecode(await RenderAsync(
            new DeliveryFileManagementViewModel
            {
                Applications =
                [
                    new DeliveryFileApplicationOptionViewModel
                    {
                        ApplicationId = 41,
                        ApplicationCode = "ERSN",
                        ApplicationName = "eRSN"
                    }
                ],
                SelectedApplicationId = 41,
                RecentRuns =
                [
                    new DeliveryFileRunRowViewModel
                    {
                        EmailRunId = 701,
                        TriggerIdentifier = "RUN_NOW",
                        StatusIdentifier = "COMPLETED",
                        CreatedAt = new DateTimeOffset(
                            2026, 8, 30, 2, 59, 0, TimeSpan.Zero),
                        TotalCount = 2,
                        HasZipRecord = true
                    }
                ]
            },
            "Admin"));
        string visibleText = ReadVisibleText(html);

        string? fixtureDirectory = Environment.GetEnvironmentVariable("RV_UI_FIXTURE_DIRECTORY");
        if (!string.IsNullOrEmpty(fixtureDirectory))
        {
            Directory.CreateDirectory(fixtureDirectory);
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "delivery-files-Admin.html"), html);
        }

        Assert.Contains(
            "<h1 id=\"page-heading\" tabindex=\"-1\">Delivery files</h1>",
            html);
        Assert.Contains("Recent Runs", visibleText);
        Assert.Contains("eRSN", visibleText);
        Assert.Contains("Run 701", visibleText);
        Assert.Contains("COMPLETED", visibleText);
        Assert.Contains("Download ZIP", visibleText);
        Assert.Contains("Open files", visibleText);
        Assert.Contains("name=\"applicationId\"", html);
        Assert.Contains("href=\"/DeliveryFiles/Index\"", html);
        Assert.Matches(
            "<a[^>]*aria-current=\"page\"[^>]*>\\s*Delivery files\\s*</a>",
            html);
        Assert.DoesNotContain("Annual delivery", visibleText);
        Assert.DoesNotContain("Preview workbook", visibleText);
        Assert.DoesNotContain("sample", visibleText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunPage_Should_RenderSafeRecordedStateDrawersAndDownloads()
    {
        string html = WebUtility.HtmlDecode(await RenderAsync(
            Model(),
            "Local_IT_Admin"));
        string visibleText = ReadVisibleText(html);

        Assert.Contains("Run #701", visibleText);
        Assert.Contains("ORACLE", visibleText);
        Assert.Contains("API_EMAIL", visibleText);
        Assert.Contains("SAFE_REDIRECT", visibleText);
        Assert.Contains("REVIEW_REQUIRED", visibleText);
        Assert.DoesNotContain("ReviewRequired", visibleText);
        Assert.Contains("Application / Admin package", visibleText);
        Assert.Contains("Download only · never emailed", visibleText);
        Assert.Contains("Recorded", visibleText);
        Assert.Contains("Not recorded", visibleText);
        Assert.Contains("Intended Owner", visibleText);
        Assert.Contains("Effective recipient", visibleText);
        Assert.Contains("C2000001", visibleText);
        Assert.Contains("C2001234", visibleText);
        Assert.Contains("UNKNOWN", visibleText);
        Assert.Contains("FAILED", visibleText);

        Assert.Contains("href=\"#delivery-file-preview-zip\"", html);
        Assert.Contains("href=\"#delivery-file-preview-801\"", html);
        Assert.Contains("id=\"delivery-file-preview-zip\"", html);
        Assert.Contains("id=\"delivery-file-preview-801\"", html);
        Assert.True(
            Regex.Matches(
                html,
                "id=\"delivery-file-preview-(zip|801)\"[^>]*tabindex=\"-1\"")
                .Count == 2,
            "Every metadata drawer needs a focusable container.");
        Assert.Contains("aria-labelledby=\"delivery-file-preview-zip-title\"", html);
        Assert.Contains("aria-labelledby=\"delivery-file-preview-801-title\"", html);
        Assert.True(
            Regex.Matches(html, "data-drawer-close").Count >= 2,
            "Every drawer needs a visible Close control.");

        Assert.Contains("href=\"/DeliveryFiles/DownloadZip\"", html);
        Assert.Contains("href=\"/DeliveryFiles/DownloadWorkbook\"", html);
        System.Text.RegularExpressions.Match unrecordedRow = Regex.Match(
            html,
            "<tr[^>]*id=\"delivery-file-row-802\"(?<row>.*?)</tr>",
            RegexOptions.Singleline);
        Assert.True(unrecordedRow.Success, "Unrecorded workbook row was missing.");
        Assert.DoesNotContain("Download workbook", unrecordedRow.Groups["row"].Value);

        Assert.Contains("href=\"/EmailSchedules/Index\"", html);
        Assert.DoesNotContain("Annual delivery <small>Unavailable</small>", html);
        Assert.Single(Regex.Matches(
            html,
            "<form",
            RegexOptions.IgnoreCase).Cast<
                System.Text.RegularExpressions.Match>());
        Assert.Contains("action=\"/Authentication/Logout\"", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("private-run.zip", html);
        Assert.DoesNotContain("private-owner.xlsx", html);
        Assert.DoesNotContain("StoragePath", html);
        Assert.DoesNotContain("owner@example", html);
        Assert.DoesNotContain("WorkbookFileName", html);
        Assert.DoesNotContain("ZipFileName", html);
        Assert.DoesNotContain("sheet name", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zip entry", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object", html, StringComparison.OrdinalIgnoreCase);
    }

    private static DeliveryFileManagementViewModel Model() => new()
    {
        Run = new DeliveryFileRunSnapshotViewModel
        {
            EmailRunId = 701,
            DataSource = "ORACLE",
            TransportMode = "API_EMAIL",
            RecipientPolicy = "SAFE_REDIRECT",
            StatusIdentifier = "REVIEW_REQUIRED"
        },
        HasZipRecord = true,
        Deliveries =
        [
            new EmailDeliveryFileRowViewModel
            {
                EmailDeliveryId = 801,
                IntendedOwnerEmployeeNo = "C2000001",
                EffectiveEmployeeNo = "C2001234",
                StatusIdentifier = "UNKNOWN",
                AttemptCount = 3,
                HasWorkbookRecord = true
            },
            new EmailDeliveryFileRowViewModel
            {
                EmailDeliveryId = 802,
                IntendedOwnerEmployeeNo = "C2000002",
                EffectiveEmployeeNo = "C2001234",
                StatusIdentifier = "FAILED",
                AttemptCount = 2,
                HasWorkbookRecord = false
            }
        ]
    };

    private static async Task<string> RenderAsync(
        DeliveryFileManagementViewModel model,
        string role)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.ApplicationName)
            .Returns(typeof(DeliveryFilesController).Assembly.GetName().Name!);
        environment.SetupGet(item => item.EnvironmentName)
            .Returns(Environments.Development);
        environment.SetupGet(item => item.ContentRootPath)
            .Returns(AppContext.BaseDirectory);
        environment.SetupGet(item => item.ContentRootFileProvider)
            .Returns(new NullFileProvider());
        environment.SetupGet(item => item.WebRootPath)
            .Returns(AppContext.BaseDirectory);
        environment.SetupGet(item => item.WebRootFileProvider)
            .Returns(new NullFileProvider());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(environment.Object);
        var diagnosticListener = new DiagnosticListener(
            "DeliveryFileViewTests");
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(DeliveryFilesController).Assembly);
        services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "viewer"),
            new Claim(ClaimTypes.Role, role)
        };
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                RoleValidationAuthenticationDefaults.CookieScheme,
                ClaimTypes.Name,
                ClaimTypes.Role))
        };
        var routeData = new RouteData();
        routeData.Values["controller"] = "DeliveryFiles";
        routeData.Values["action"] = "Index";
        var router = new Mock<IRouter>();
        router.Setup(item => item.GetVirtualPath(
                It.IsAny<VirtualPathContext>()))
            .Returns((VirtualPathContext context) =>
            {
                string controller =
                    context.Values["controller"]?.ToString() ?? "DeliveryFiles";
                string action = context.Values["action"]?.ToString() ?? "Index";
                return new VirtualPathData(
                    router.Object,
                    $"/{controller}/{action}");
            });
        routeData.Routers.Add(router.Object);
        var actionContext = new ActionContext(
            httpContext,
            routeData,
            new ActionDescriptor());
        IRazorViewEngine viewEngine =
            scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
        ViewEngineResult found = viewEngine.GetView(
            executingFilePath: null,
            viewPath: "/Views/DeliveryFiles/Index.cshtml",
            isMainPage: true);
        Assert.True(
            found.Success,
            $"Delivery-files view was not found: {string.Join(", ", found.SearchedLocations)}");
        IModelMetadataProvider metadata =
            scope.ServiceProvider.GetRequiredService<IModelMetadataProvider>();
        var viewData = new ViewDataDictionary(
            metadata,
            new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());
        using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            found.View,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        await found.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static string ReadVisibleText(string html)
    {
        string withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(withoutTags, "\\s+", " ").Trim();
    }

    private static string Read(params string[] pathParts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, .. pathParts]));
}
