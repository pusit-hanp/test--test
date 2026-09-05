using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.SourceMappings;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.ApplicationUsers;

namespace RoleValidation.Web.Tests.ApplicationUsers;

public sealed class ApplicationUsersViewContractTests
{
    [Fact]
    public async Task UserVisibleCopy_ShouldBeEnglish()
    {
        string html = WebUtility.HtmlDecode(
            await RenderApplicationUsersViewAsync(
                new ApplicationUserListViewModel
                {
                    SelectedApplicationId = 42,
                    SelectedApplicationName = "ERSN",
                    TotalCount = 0
                }));

        Assert.Contains(
            "Review employee status, role mappings, and data scope before exporting.",
            html);
        Assert.Contains(
            "Change the search, mapped-role filter, or status and try again.",
            html);
        Assert.DoesNotMatch(new Regex("[ก-๙]"), html);
    }

    [Theory]
    [InlineData("Admin", false)]
    [InlineData("Local_IT_Admin", true)]
    public async Task ApplicationUsers_Should_SelectNavigationForAuthenticatedRole(
        string role, bool hasManagementNavigation)
    {
        string html = WebUtility.HtmlDecode(await RenderApplicationUsersViewAsync(
            new ApplicationUserListViewModel
            {
                SelectedApplicationId = 42,
                SelectedApplicationName = "eRSN",
                Applications = [new ApplicationOptionViewModel(42, "eRSN")],
                AvailableMappedRoles =
                [
                    new ApplicationUserMappedRoleOption(1, "Application administrator"),
                    new ApplicationUserMappedRoleOption(2, "Access reviewer")
                ],
                Users =
                [
                    new ApplicationUserRowViewModel(
                        "TEST001", "test.user", "Test User", "test@example.invalid",
                        "Analyst", "IT", "REVIEWER", "Access reviewer", "Access reviewer",
                        3, true, EmployeeStatusType.Active, SourceRoleResolutionType.Resolved)
                ],
                TotalCount = 1175,
                TotalPages = 24,
                PageNumber = 12,
                PageSize = 50
            }, role));

        Assert.DoesNotContain("app-header-management-link", html);
        Assert.Contains("data-theme-choice=\"dark\"", html);
        Assert.Contains("Sign out", html);
        if (hasManagementNavigation)
        {
            Assert.Contains("class=\"management-navigation\"", html);
            Assert.Contains("class=\"management-topbar\"", html);
            Assert.Matches(new Regex(
                "<a(?=[^>]*href=\"/ApplicationUsers/Index\")(?=[^>]*aria-current=\"page\")[^>]*>\\s*Application users\\s*</a>"), html);
            Assert.DoesNotContain("class=\"app-header\"", html);
        }
        else
        {
            Assert.Contains("class=\"app-header\"", html);
            Assert.DoesNotContain("class=\"management-navigation\"", html);
            Assert.DoesNotContain("class=\"management-topbar\"", html);
        }

        string? fixtureDirectory = Environment.GetEnvironmentVariable("RV_UI_FIXTURE_DIRECTORY");
        if (!string.IsNullOrEmpty(fixtureDirectory))
        {
            Directory.CreateDirectory(fixtureDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(fixtureDirectory, $"application-users-{role}.html"), html);
        }
    }

    [Fact]
    public async Task CurrentScope_Should_LinkToSelectedApplicationConfiguration()
    {
        string html = WebUtility.HtmlDecode(
            await RenderApplicationUsersViewAsync(
                new ApplicationUserListViewModel
                {
                    SelectedApplicationId = 42,
                    SelectedApplicationName = "ERSN"
                }));

        Assert.Contains(
            "href=\"/Applications/Index#application-42\"",
            html);
        Assert.Contains("View application configuration", html);
    }

    [Fact]
    public void Index_Should_ExposeAuditedFilterAndPreserveItForExportAndPaging()
    {
        string view = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Views",
            "ApplicationUsers",
            "Index.cshtml"));

        Assert.Contains("name=\"isAudited\"", view);
        Assert.Contains("name=\"auditedSelectionMode\"", view);
        Assert.Contains("name=\"employeeStatusSelectionMode\"", view);
        Assert.Contains("name=\"resolutionSelectionMode\"", view);
        Assert.Equal(
            2,
            Regex.Matches(view, "data-multi-select-filter").Count);
        Assert.Contains("ValueName = \"employeeStatus\"", view);
        Assert.Contains("ValueName = \"isAudited\"", view);
        Assert.Contains("ValueName = \"resolutionType\"", view);
        Assert.DoesNotContain("<select name=\"employeeStatus\"", view);
        Assert.DoesNotContain("<select name=\"isAudited\"", view);
        Assert.DoesNotContain("<select name=\"resolutionType\"", view);
        Assert.Equal(
            2,
            Regex.Matches(view, "name=\"isAudited\"").Count);
        Assert.Contains("Employee number", view);
        Assert.Contains("Is audited", view);
        Assert.Contains("@Display(user.EmployeeNo)", view);
        Assert.DoesNotContain("select-column", view);
    }

    [Fact]
    public void Model_Should_DefaultAuditedFilterToTrue()
    {
        var model = new ApplicationUserListViewModel();

        Assert.True(model.IsAuditedFilter);
    }

    [Fact]
    public void Model_Should_ToggleCurrentSortAndStartNewSortAscending()
    {
        var model = new ApplicationUserListViewModel
        {
            SortBy = ApplicationUserSortField.FullName,
            SortDirection = ApplicationUserSortDirection.Ascending
        };

        Assert.Equal(
            ApplicationUserSortDirection.Descending,
            model.GetNextSortDirection(ApplicationUserSortField.FullName));
        Assert.Equal(
            ApplicationUserSortDirection.Ascending,
            model.GetNextSortDirection(ApplicationUserSortField.Department));
        Assert.Equal(
            "ascending",
            model.GetAriaSortValue(ApplicationUserSortField.FullName));
        Assert.Equal(
            "none",
            model.GetAriaSortValue(ApplicationUserSortField.Department));
    }

    [Fact]
    public void Index_Should_UseNativeSortButtonsAndCheckboxGroupSemantics()
    {
        string view = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Views",
            "ApplicationUsers",
            "Index.cshtml"));
        string script = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "js",
            "application-users.js"));

        Assert.Equal(
            11,
            Regex.Matches(
                view,
                "<th[^>]*scope=\"col\"[^>]*aria-sort=[^>]*>\\s*<button[^>]*data-sortable",
                RegexOptions.Singleline).Count);
        Assert.Contains("button[data-sortable]", script);
        Assert.DoesNotContain("header.tabIndex", script);
        Assert.DoesNotContain("setAttribute(\"role\", \"button\")", script);
        Assert.DoesNotContain("role=\"listbox\"", view);
        Assert.DoesNotContain("aria-multiselectable", view);
        Assert.DoesNotContain("aria-selected", script);
        Assert.Contains("role=\"group\"", view);
        Assert.Contains("aria-labelledby", view);
    }

    [Fact]
    public void ApplicationSelection_Should_ReloadWithFreshMappedRoles()
    {
        string view = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Views",
            "ApplicationUsers",
            "Index.cshtml"));
        string script = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "js",
            "application-users.js"));

        Assert.Contains("data-application-select", view);
        Assert.Contains("[data-application-select]", script);
        Assert.Contains("applicationSelect.addEventListener(\"change\"", script);
        Assert.Contains("url.search = \"\"", script);
        Assert.Contains("url.searchParams.set(\"applicationId\"", script);
        Assert.Contains("window.location.assign(url)", script);
        Assert.DoesNotContain("loadForm.requestSubmit()", script);
    }

    [Fact]
    public async Task IndexPager_ShouldRenderCenteredWindowAndPreserveEveryFilter()
    {
        var model = new ApplicationUserListViewModel
        {
            SelectedApplicationId = 42,
            SelectedApplicationName = "ERSN",
            Search = "buyer & audit",
            SelectedMappedRoleIds = [3001, 3002],
            MappedRoleSelectionMode = MappedRoleSelectionMode.Selected,
            SelectedEmployeeStatuses =
                [EmployeeStatusType.Active, EmployeeStatusType.Inactive],
            EmployeeStatusSelectionMode = FilterSelectionMode.Selected,
            SelectedAuditedStatuses = [true, false],
            AuditedSelectionMode = FilterSelectionMode.Selected,
            SelectedResolutionTypes =
                [SourceRoleResolutionType.Resolved],
            ResolutionSelectionMode = FilterSelectionMode.Selected,
            SortBy = ApplicationUserSortField.Department,
            SortDirection = ApplicationUserSortDirection.Descending,
            PageNumber = 6,
            PageSize = 100,
            TotalPages = 10,
            TotalCount = 950
        };

        string html = WebUtility.HtmlDecode(
            await RenderApplicationUsersViewAsync(model));
        string pager = ExtractPager(html, "form", "pager");

        Assert.Contains("data-load-form", pager);
        Assert.Equal(
            ["First=1", "Previous=5", "4=4", "5=5", "7=7", "8=8", "Next=7", "Last=10"],
            ReadSubmitPages(pager));
        Assert.Matches(
            new Regex("<span[^>]*aria-current=\"page\"[^>]*>\\s*6\\s*</span>"),
            pager);
        Assert.Contains("name=\"applicationId\" value=\"42\"", pager);
        Assert.Contains("name=\"search\" value=\"buyer & audit\"", pager);
        Assert.Contains("name=\"pageSize\" value=\"100\"", pager);
        Assert.Contains("name=\"sortBy\" value=\"Department\"", pager);
        AssertHiddenValue(pager, "sortDirection", "Descending");
        AssertHiddenValue(pager, "employeeStatusSelectionMode", "Selected");
        AssertHiddenValue(pager, "auditedSelectionMode", "Selected");
        AssertHiddenValue(pager, "resolutionSelectionMode", "Selected");
        AssertHiddenValue(pager, "mappedRoleSelectionMode", "Selected");
        Assert.Equal(
            ["3001", "3002"],
            ReadInputValues(pager, "mappedRoleIds"));
        Assert.Equal(
            ["Active", "Inactive"],
            ReadInputValues(pager, "employeeStatus"));
        Assert.Equal(
            ["true", "false"],
            ReadInputValues(pager, "isAudited"));
        Assert.Equal(
            ["Resolved"],
            ReadInputValues(pager, "resolutionType"));
        Assert.DoesNotContain("<a", pager);
        System.Text.RegularExpressions.Match pageSize = Regex.Match(
            html,
            "<select[^>]*name=\"pageSize\"[^>]*>([\\s\\S]*?)</select>");
        Assert.True(pageSize.Success, "Page-size selector was not rendered.");
        Assert.Equal(
            ["25", "50", "100", "200"],
            Regex.Matches(pageSize.Groups[1].Value, "<option[^>]*value=\"(\\d+)\"")
                .Select(match => match.Groups[1].Value)
                .ToArray());
        Assert.Contains("Showing 501-600 of 950", ReadVisibleText(html));
    }

    [Theory]
    [InlineData(1, "First", "Previous")]
    [InlineData(10, "Next", "Last")]
    public async Task IndexPager_ShouldRenderDisabledEdgesAsNonLinks(
        int pageNumber,
        string firstDisabledLabel,
        string secondDisabledLabel)
    {
        string html = WebUtility.HtmlDecode(await RenderApplicationUsersViewAsync(
            new ApplicationUserListViewModel
            {
                SelectedApplicationId = 42,
                PageNumber = pageNumber,
                PageSize = 25,
                TotalPages = 10,
                TotalCount = 250
            }));
        string pager = ExtractPager(html, "form", "pager");

        AssertDisabledSpan(pager, firstDisabledLabel);
        AssertDisabledSpan(pager, secondDisabledLabel);
        Assert.DoesNotMatch(
            new Regex($"<(a|button)[^>]*>\\s*{firstDisabledLabel}\\s*</(a|button)>"),
            pager);
        Assert.DoesNotMatch(
            new Regex($"<(a|button)[^>]*>\\s*{secondDisabledLabel}\\s*</(a|button)>"),
            pager);
    }

    [Theory]
    [InlineData(1, 24, "1,2,3,4,5")]
    [InlineData(12, 24, "10,11,12,13,14")]
    [InlineData(24, 24, "20,21,22,23,24")]
    [InlineData(2, 4, "1,2,3,4")]
    public async Task IndexPager_ShouldClampFivePageWindowAtLargeAndSmallScales(
        int pageNumber,
        int totalPages,
        string expectedPages)
    {
        string html = WebUtility.HtmlDecode(await RenderApplicationUsersViewAsync(
            new ApplicationUserListViewModel
            {
                SelectedApplicationId = 42,
                PageNumber = pageNumber,
                PageSize = 50,
                TotalPages = totalPages,
                TotalCount = totalPages * 50
            }));
        string pager = ExtractPager(html, "form", "pager");

        Assert.Equal(
            expectedPages.Split(','),
            ReadVisiblePageNumbers(pager));
    }

    [Fact]
    public async Task IndexPager_ShouldStayHiddenForOnePageAndClampPartialLastRange()
    {
        string onePage = WebUtility.HtmlDecode(
            await RenderApplicationUsersViewAsync(
                new ApplicationUserListViewModel
                {
                    SelectedApplicationId = 42,
                    PageNumber = 1,
                    PageSize = 25,
                    TotalPages = 1,
                    TotalCount = 7
                }));
        string partialLastPage = WebUtility.HtmlDecode(
            await RenderApplicationUsersViewAsync(
                new ApplicationUserListViewModel
                {
                    SelectedApplicationId = 42,
                    PageNumber = 24,
                    PageSize = 50,
                    TotalPages = 24,
                    TotalCount = 1175
                }));

        Assert.DoesNotContain("class=\"pager\"", onePage);
        Assert.Contains("Showing 1-7 of 7", ReadVisibleText(onePage));
        Assert.Contains(
            "Showing 1151-1175 of 1175",
            ReadVisibleText(partialLastPage));
    }

    [Fact]
    public void ApplicationPagerStyles_ShouldWrapAsRowsOnDesktopAndMobile()
    {
        string siteCss = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "css",
            "site.css"));

        Assert.Matches(
            new Regex("\\.pager\\s*\\{[^}]*flex-wrap:\\s*wrap", RegexOptions.Singleline),
            siteCss);
        Assert.Matches(
            new Regex("\\.pager[^}]*\\[aria-current=\"page\"\\]", RegexOptions.Singleline),
            siteCss);
    }

    [Fact]
    public void HistoryPagerStyles_ShouldWrapAsRowsOnDesktopAndMobile()
    {
        string managementCss = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "css",
            "phase2-management.css"));

        Assert.Matches(
            new Regex("\\.management-history-pager\\s*\\{[^}]*flex-wrap:\\s*wrap", RegexOptions.Singleline),
            managementCss);
        Assert.Matches(
            new Regex("\\.management-history-pager[^}]*\\[aria-current=\"page\"\\]", RegexOptions.Singleline),
            managementCss);
        Assert.DoesNotMatch(
            new Regex(
                "\\.management-history-pager\\s*\\{[^}]*flex-direction:\\s*column",
                RegexOptions.Singleline),
            managementCss);
    }

    [Fact]
    public void MobilePagerRules_ShouldExplicitlyStayWrappingRows()
    {
        string siteCss = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "css",
            "site.css"));
        string managementCss = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "css",
            "phase2-management.css"));

        string applicationPagerRule = ExtractCssRule(
            ExtractMediaBlock(siteCss, 540),
            ".pager");
        string historyPagerRule = ExtractCssRule(
            ExtractMediaBlock(managementCss, 560),
            ".management-history-pager");

        Assert.Contains("flex-direction: row;", applicationPagerRule);
        Assert.Contains("flex-wrap: wrap;", applicationPagerRule);
        Assert.Contains("flex-direction: row;", historyPagerRule);
        Assert.Contains("flex-wrap: wrap;", historyPagerRule);
    }

    private static string ExtractMediaBlock(string css, int maxWidth)
    {
        string marker = $"@media (max-width: {maxWidth}px)";
        int start = css.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{marker} was not found.");
        int end = css.IndexOf("@media", start + marker.Length, StringComparison.Ordinal);
        return end >= 0 ? css[start..end] : css[start..];
    }

    private static string ExtractCssRule(string css, string selector)
    {
        System.Text.RegularExpressions.Match rule = Regex.Match(
            css,
            $"{Regex.Escape(selector)}\\s*\\{{([^}}]*)\\}}");
        Assert.True(rule.Success, $"{selector} rule was not found.");
        return rule.Groups[1].Value;
    }

    private static void AssertHiddenValue(
        string pager,
        string name,
        string value)
    {
        Assert.Matches(
            new Regex($"<input[^>]*name=\"{name}\"[^>]*value=\"{value}\"[^>]*/>"),
            pager);
    }

    private static IReadOnlyList<string> ReadInputValues(
        string html,
        string name)
    {
        return Regex.Matches(
                html,
                $"<input[^>]*name=\"{name}\"[^>]*value=\"([^\"]*)\"[^>]*/>")
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static void AssertDisabledSpan(string pager, string label)
    {
        System.Text.RegularExpressions.Match disabled = Regex.Match(
            pager,
            $"<span(?<before>[^>]*)aria-disabled=\"true\"(?<after>[^>]*)>\\s*{label}\\s*</span>");
        Assert.True(disabled.Success, $"Disabled {label} span was not rendered.");
        string openingTag =
            $"<span{disabled.Groups["before"].Value}"
            + "aria-disabled=\"true\""
            + $"{disabled.Groups["after"].Value}>";
        Assert.DoesNotMatch(
            new Regex("href\\s*=", RegexOptions.IgnoreCase),
            openingTag);
    }

    private static IReadOnlyList<string> ReadSubmitPages(string pager)
    {
        return Regex.Matches(
                pager,
                "<button[^>]*type=\"submit\"[^>]*name=\"page\"[^>]*value=\"(\\d+)\"[^>]*>\\s*([^<]+?)\\s*</button>")
            .Select(match =>
                $"{match.Groups[2].Value.Trim()}={match.Groups[1].Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> ReadVisiblePageNumbers(string pager)
    {
        return Regex.Matches(
                pager,
                ">\\s*(\\d+)\\s*</(?:button|span)>")
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static string ReadVisibleText(string html)
    {
        string withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(withoutTags, "\\s+", " ").Trim();
    }

    private static string ExtractPager(
        string html,
        string element,
        string className)
    {
        System.Text.RegularExpressions.Match pager = Regex.Match(
            html,
            $"<{element}[^>]*class=\"[^\"]*{className}[^\"]*\"[^>]*>[\\s\\S]*?</{element}>");
        Assert.True(pager.Success, $"{className} was not rendered.");
        return pager.Value;
    }

    private static async Task<string> RenderApplicationUsersViewAsync(
        ApplicationUserListViewModel model,
        string role = "Admin")
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.ApplicationName)
            .Returns(typeof(ApplicationUsersController).Assembly.GetName().Name!);
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
        var diagnosticListener = new DiagnosticListener("ApplicationUsersViewTests");
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(ApplicationUsersController).Assembly);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "C1008267"),
                new Claim(ClaimTypes.Role, role)
            ], "Test"));
        var routeData = new RouteData();
        routeData.Values["controller"] = "ApplicationUsers";
        routeData.Values["action"] = "Index";
        var router = new Mock<IRouter>();
        router.Setup(item => item.GetVirtualPath(
                It.IsAny<VirtualPathContext>()))
            .Returns((VirtualPathContext context) =>
            {
                string controller =
                    context.Values["controller"]?.ToString() ?? "ApplicationUsers";
                string action = context.Values["action"]?.ToString() ?? "Index";
                return new VirtualPathData(router.Object, $"/{controller}/{action}");
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
            viewPath: "/Views/ApplicationUsers/Index.cshtml",
            isMainPage: true);
        Assert.True(
            found.Success,
            $"Application users view was not found: {string.Join(", ", found.SearchedLocations)}");
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
}
