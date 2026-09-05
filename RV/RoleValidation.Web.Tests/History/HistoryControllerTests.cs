using System.Reflection;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
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
using RoleValidation.Application.History;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.History;

namespace RoleValidation.Web.Tests.History;

public sealed class HistoryControllerTests
{
    [Fact]
    public void BangkokTimestamp_ShouldKeepExplicitAuditOffset()
    {
        string display = HistoryViewFormatting.FormatBangkok(
            DateTimeOffset.Parse("2026-08-30T04:04:33Z"));

        Assert.Equal("30 Aug 2026 11:04:33 UTC+07", display);
    }

    [Fact]
    public async Task ChangesView_ShouldLinkEncodedPayloadDetailOnDemand()
    {
        var model = new ChangeHistoryViewModel
        {
            Rows =
            [
                new ChangeHistoryRow(
                    81,
                    "ValidationRole",
                    "3001",
                    "Rename",
                    "C1008267",
                    DateTimeOffset.Parse("2026-08-30T04:04:33Z"))
            ],
            TotalCount = 1
        };

        string html = await RenderHistoryViewAsync("Changes", model);
        string visible = WebUtility.HtmlDecode(html);

        Assert.Contains("Changed at", html);
        Assert.Contains("Entity ID", html);
        Assert.Contains("Changed by", html);
        Assert.Contains("value=\"RoleOwnerAssignment\"", html);
        Assert.Contains("30 Aug 2026 11:04:33 UTC+07", visible);
        Assert.Contains("href=\"/History/ChangeDetail?id=81\"", html);
        Assert.DoesNotContain("data-management-drawer", html);
    }

    [Fact]
    public async Task ChangeDetailView_ShouldRenderEncodedPayloadOnlyForOneEvent()
    {
        var detail = new ChangeHistoryDetail(
            81,
            "ValidationRole",
            "3001",
            "Rename",
            "<script>bad()</script>",
            "Buyer-User",
            "C1008267",
            DateTimeOffset.Parse("2026-08-30T04:04:33Z"));

        string html = await RenderHistoryViewAsync("ChangeDetail", detail);

        Assert.Contains("Change event detail", html);
        Assert.Contains("&lt;script&gt;bad()&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>bad()</script>", html);
        Assert.Contains("Buyer-User", html);
        Assert.Contains("href=\"/History/Changes\"", html);
    }

    [Fact]
    public async Task ChangesViewPager_ShouldPreserveChangeActionQueryFilter()
    {
        var model = new ChangeHistoryViewModel
        {
            Action = "Rename",
            PageNumber = 2,
            TotalPages = 3,
            TotalCount = 150
        };

        string html = WebUtility.HtmlDecode(
            await RenderHistoryViewAsync("Changes", model));
        MatchCollection pagerLinks = Regex.Matches(
            html,
            "<a[^>]*href=\"([^\"]+)\"[^>]*>(Previous|Next)</a>");

        Assert.Equal(2, pagerLinks.Count);
        Assert.All(pagerLinks.Cast<System.Text.RegularExpressions.Match>(), match =>
        {
            string href = match.Groups[1].Value;
            Assert.Contains("changeAction=Rename", href);
            Assert.Contains("page=", href);
            Assert.DoesNotContain("action=Rename", href);
        });
        Assert.Contains(pagerLinks.Cast<System.Text.RegularExpressions.Match>(),
            match => match.Groups[1].Value.Contains("page=1"));
        Assert.Contains(pagerLinks.Cast<System.Text.RegularExpressions.Match>(),
            match => match.Groups[1].Value.Contains("page=3"));
    }

    [Fact]
    public async Task ChangesViewPager_ShouldRenderCenteredWindowAndPreserveAllFilters()
    {
        var model = new ChangeHistoryViewModel
        {
            Search = "3001",
            EntityType = "ValidationRole",
            Action = "Rename",
            PageNumber = 6,
            PageSize = 50,
            TotalPages = 10,
            TotalCount = 498
        };

        string html = WebUtility.HtmlDecode(
            await RenderHistoryViewAsync("Changes", model));
        string pager = ExtractHistoryPager(html);

        Assert.Equal(
            ["First=1", "Previous=5", "4=4", "5=5", "7=7", "8=8", "Next=7", "Last=10"],
            ReadHistoryPageLinks(pager));
        Assert.Matches(
            new Regex("<span[^>]*aria-current=\"page\"[^>]*>\\s*6\\s*</span>"),
            pager);
        Assert.All(
            Regex.Matches(pager, "<a[^>]*href=\"([^\"]+)\"")
                .Cast<System.Text.RegularExpressions.Match>(),
            match =>
            {
                string href = match.Groups[1].Value;
                Assert.Contains("search=3001", href);
                Assert.Contains("entityType=ValidationRole", href);
                Assert.Contains("changeAction=Rename", href);
            });
        Assert.Contains("Showing 251-300 of 498", ReadVisibleText(html));
    }

    [Fact]
    public async Task LoginsViewPager_ShouldRenderCenteredWindowAndPreserveAllFilters()
    {
        var model = new LoginHistoryViewModel
        {
            SearchSubmitted = true,
            EmployeeNo = "C1008267",
            CorrelationId = "trace-91",
            Result = "DENIED",
            Rows = [CreateLoginHistoryRow()],
            PageNumber = 6,
            PageSize = 50,
            TotalPages = 10,
            TotalCount = 498
        };

        string html = WebUtility.HtmlDecode(
            await RenderHistoryViewAsync("Logins", model));
        string pager = ExtractHistoryPager(html);

        Assert.Equal(
            ["First=1", "Previous=5", "4=4", "5=5", "7=7", "8=8", "Next=7", "Last=10"],
            ReadHistoryPageLinks(pager));
        Assert.Matches(
            new Regex("<span[^>]*aria-current=\"page\"[^>]*>\\s*6\\s*</span>"),
            pager);
        Assert.All(
            Regex.Matches(pager, "<a[^>]*href=\"([^\"]+)\"")
                .Cast<System.Text.RegularExpressions.Match>(),
            match =>
            {
                string href = match.Groups[1].Value;
                Assert.Contains("search=true", href);
                Assert.Contains("employeeNo=C1008267", href);
                Assert.Contains("correlationId=trace-91", href);
                Assert.Contains("result=DENIED", href);
            });
        Assert.Contains("Showing 251-300 of 498", ReadVisibleText(html));
    }

    [Theory]
    [InlineData(1, 24, "1,2,3,4,5")]
    [InlineData(12, 24, "10,11,12,13,14")]
    [InlineData(24, 24, "20,21,22,23,24")]
    [InlineData(2, 4, "1,2,3,4")]
    [InlineData(1, 1, "1")]
    public async Task ChangesViewPager_ShouldClampFivePageWindow(
        int pageNumber,
        int totalPages,
        string expectedPages)
    {
        string html = WebUtility.HtmlDecode(await RenderHistoryViewAsync(
            "Changes",
            new ChangeHistoryViewModel
            {
                PageNumber = pageNumber,
                PageSize = 50,
                TotalPages = totalPages,
                TotalCount = totalPages * 50
            }));
        string pager = ExtractHistoryPager(html);

        Assert.Equal(
            expectedPages.Split(','),
            ReadVisiblePageNumbers(pager));
    }

    [Theory]
    [InlineData(1, 24, "1,2,3,4,5")]
    [InlineData(12, 24, "10,11,12,13,14")]
    [InlineData(24, 24, "20,21,22,23,24")]
    [InlineData(2, 4, "1,2,3,4")]
    [InlineData(1, 1, "1")]
    public async Task LoginsViewPager_ShouldClampFivePageWindow(
        int pageNumber,
        int totalPages,
        string expectedPages)
    {
        string html = WebUtility.HtmlDecode(await RenderHistoryViewAsync(
            "Logins",
            new LoginHistoryViewModel
            {
                SearchSubmitted = true,
                Rows = [CreateLoginHistoryRow()],
                PageNumber = pageNumber,
                PageSize = 50,
                TotalPages = totalPages,
                TotalCount = totalPages * 50
            }));
        string pager = ExtractHistoryPager(html);

        Assert.Equal(
            expectedPages.Split(','),
            ReadVisiblePageNumbers(pager));
    }

    [Fact]
    public async Task HistoryViews_ShouldClampPartialLastRowRange()
    {
        string changes = WebUtility.HtmlDecode(await RenderHistoryViewAsync(
            "Changes",
            new ChangeHistoryViewModel
            {
                PageNumber = 24,
                PageSize = 50,
                TotalPages = 24,
                TotalCount = 1175
            }));
        string logins = WebUtility.HtmlDecode(await RenderHistoryViewAsync(
            "Logins",
            new LoginHistoryViewModel
            {
                SearchSubmitted = true,
                Rows = [CreateLoginHistoryRow()],
                PageNumber = 24,
                PageSize = 50,
                TotalPages = 24,
                TotalCount = 1175
            }));

        Assert.Contains("Showing 1151-1175 of 1175", ReadVisibleText(changes));
        Assert.Contains("Showing 1151-1175 of 1175", ReadVisibleText(logins));
    }

    [Fact]
    public async Task HistoryPagers_ShouldRenderDisabledEdgesAsNonLinks()
    {
        string firstPage = ExtractHistoryPager(WebUtility.HtmlDecode(
            await RenderHistoryViewAsync(
                "Changes",
                new ChangeHistoryViewModel
                {
                    PageNumber = 1,
                    TotalPages = 10,
                    TotalCount = 500
                })));
        string lastPage = ExtractHistoryPager(WebUtility.HtmlDecode(
            await RenderHistoryViewAsync(
                "Logins",
                new LoginHistoryViewModel
                {
                    SearchSubmitted = true,
                    Rows = [CreateLoginHistoryRow()],
                    PageNumber = 10,
                    TotalPages = 10,
                    TotalCount = 500
                })));

        AssertDisabledHistorySpan(firstPage, "First");
        AssertDisabledHistorySpan(firstPage, "Previous");
        AssertDisabledHistorySpan(lastPage, "Next");
        AssertDisabledHistorySpan(lastPage, "Last");
    }

    [Fact]
    public async Task LoginsView_ShouldRenderAccessEvidenceAndTracingBoundary()
    {
        var model = new LoginHistoryViewModel
        {
            SearchSubmitted = true,
            EmployeeNo = "C1008267",
            Rows =
            [
                new LoginHistoryRow(
                    91,
                    "C1008267",
                    DateTimeOffset.Parse("2026-08-30T04:48:09Z"),
                    "DENIED",
                    "NOT_AUTHORIZED",
                    "trace-<91>")
            ],
            TotalCount = 1
        };

        string html = await RenderHistoryViewAsync("Logins", model);
        string visible = WebUtility.HtmlDecode(html);

        Assert.Contains("Login date", html);
        Assert.Contains("Employee number", html);
        Assert.Contains("Failure reason", html);
        Assert.Contains("Correlation ID", html);
        Assert.Contains("30 Aug 2026 11:48:09 UTC+07", visible);
        Assert.Contains("Tracing only", html);
        Assert.Contains("trace-&lt;91&gt;", html);
        Assert.DoesNotContain("trace-<91>", html);
        Assert.Contains("data-drawer-trigger", html);
        Assert.Contains("data-drawer-close", html);
    }

    [Fact]
    public async Task HistoryNavigation_ShouldRenderOnlyForLocalIt()
    {
        var model = new ChangeHistoryViewModel();

        string localIt = await RenderHistoryViewAsync(
            "Changes",
            model,
            localIt: true);
        string admin = await RenderHistoryViewAsync(
            "Changes",
            model,
            localIt: false);

        Assert.Matches(
            new Regex("<a[^>]*>\\s*Change history\\s*</a>"),
            localIt);
        Assert.Matches(
            new Regex("<a[^>]*>\\s*Login history\\s*</a>"),
            localIt);
        Assert.DoesNotMatch(
            new Regex("<a[^>]*>\\s*Change history\\s*</a>"),
            admin);
        Assert.DoesNotMatch(
            new Regex("<a[^>]*>\\s*Login history\\s*</a>"),
            admin);
    }

    [Fact]
    public async Task LoginsViewFirstLoad_ShouldShowLoadedRecentEvents()
    {
        string html = await RenderHistoryViewAsync(
            "Logins",
            new LoginHistoryViewModel
            {
                Rows = [CreateLoginHistoryRow()],
                TotalCount = 1
            });

        Assert.Contains("<table", html);
        Assert.Contains("trace-91", html);
    }

    [Fact]
    public void HistoryPages_ShouldBeLocalItOnlyGetEndpoints()
    {
        foreach (string actionName in new[]
                 {
                     nameof(HistoryController.Changes),
                     nameof(HistoryController.Logins),
                     "ChangeDetail"
                 })
        {
            MethodInfo? action = typeof(HistoryController)
                .GetMethod(actionName);
            Assert.NotNull(action);
            Assert.Equal(
                RoleValidationAuthorizationPolicies.LocalItAdministration,
                action!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
            Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
            Assert.Null(action.GetCustomAttribute<HttpPostAttribute>());
        }
    }

    [Fact]
    public void ChangesFilter_ShouldAvoidReservedActionRouteParameter()
    {
        MethodInfo method = typeof(HistoryController)
            .GetMethod(nameof(HistoryController.Changes))!;
        ParameterInfo? changeAction = method.GetParameters()
            .SingleOrDefault(parameter => parameter.Name == "changeAction");

        Assert.NotNull(changeAction);
        Assert.Null(changeAction.GetCustomAttribute<FromQueryAttribute>());
        Assert.DoesNotContain(
            method.GetParameters(),
            parameter => parameter.Name == "action");
    }

    [Fact]
    public async Task LoginsFirstLoad_ShouldReadRecentEventsWithoutSearchFlag()
    {
        var reader = new RecordingHistoryReader();
        var controller = new HistoryController(
            new LoadChangeHistoryHandler(reader),
            new LoadLoginHistoryHandler(reader));

        ViewResult view = Assert.IsType<ViewResult>(await controller.Logins(
            employeeNo: null,
            correlationId: null,
            result: null,
            search: false,
            page: 1,
            CancellationToken.None));
        var model = Assert.IsType<LoginHistoryViewModel>(view.Model);

        Assert.True(model.SearchSubmitted);
        Assert.Null(model.ErrorCode);
        Assert.Empty(model.Rows);
        Assert.Equal(1, model.PageNumber);
        Assert.Equal(1, reader.LoginReadCount);
    }

    [Fact]
    public async Task SubmittedLoginWithoutSearchKey_ShouldFilterResult()
    {
        var reader = new RecordingHistoryReader();
        var controller = new HistoryController(
            new LoadChangeHistoryHandler(reader),
            new LoadLoginHistoryHandler(reader));

        ViewResult view = Assert.IsType<ViewResult>(await controller.Logins(
            employeeNo: " ",
            correlationId: null,
            result: " DENIED ",
            search: true,
            page: 2,
            CancellationToken.None));
        var model = Assert.IsType<LoginHistoryViewModel>(view.Model);

        Assert.True(model.SearchSubmitted);
        Assert.Null(model.ErrorCode);
        Assert.Null(model.EmployeeNo);
        Assert.Equal("DENIED", model.Result);
        Assert.Equal(1, reader.LoginReadCount);
    }

    [Fact]
    public async Task Changes_ShouldOpenImmediatelyWithTrimmedServerFilters()
    {
        var reader = new RecordingHistoryReader();
        var controller = new HistoryController(
            new LoadChangeHistoryHandler(reader),
            new LoadLoginHistoryHandler(reader));

        ViewResult view = Assert.IsType<ViewResult>(await controller.Changes(
            search: " 3001 ",
            entityType: " ValidationRole ",
            changeAction: " Rename ",
            page: 0,
            CancellationToken.None));
        var model = Assert.IsType<ChangeHistoryViewModel>(view.Model);

        Assert.Equal(
            new ChangeHistoryQuery(
                "3001",
                "ValidationRole",
                "Rename",
                1,
                50),
            reader.LastChangeQuery);
        Assert.Single(model.Rows);
        Assert.Equal("3001", model.Search);
        Assert.Equal(1, model.PageNumber);
    }

    [Fact]
    public async Task ChangeDetail_ShouldReadExactlyOneRequestedEvent()
    {
        var reader = new RecordingHistoryReader();
        var controller = new HistoryController(
            new LoadChangeHistoryHandler(reader),
            new LoadLoginHistoryHandler(reader));
        MethodInfo? action = typeof(HistoryController).GetMethod(
            "ChangeDetail");
        Assert.NotNull(action);

        var pending = Assert.IsAssignableFrom<Task<IActionResult>>(
            action!.Invoke(controller, [81L, CancellationToken.None]));
        ViewResult view = Assert.IsType<ViewResult>(await pending);
        ChangeHistoryDetail detail = Assert.IsType<ChangeHistoryDetail>(
            view.Model);

        Assert.Equal(81, detail.ChangeHistoryId);
        Assert.Equal(81, reader.LastChangeDetailId);
    }

    private sealed class RecordingHistoryReader : IHistoryReader
    {
        public int LoginReadCount { get; private set; }

        public ChangeHistoryQuery? LastChangeQuery { get; private set; }

        public long? LastChangeDetailId { get; private set; }

        public Task<ChangeHistoryPage> ReadChangesAsync(
            ChangeHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastChangeQuery = query;
            return Task.FromResult(new ChangeHistoryPage(
                [new ChangeHistoryRow(
                    81,
                    "ValidationRole",
                    "3001",
                    "Rename",
                    "C1008267",
                    DateTimeOffset.Parse("2026-08-30T04:04:33Z"))],
                1,
                1,
                50));
        }

        public Task<ChangeHistoryDetail?> ReadChangeDetailAsync(
            long changeHistoryId,
            CancellationToken cancellationToken = default)
        {
            LastChangeDetailId = changeHistoryId;
            return Task.FromResult<ChangeHistoryDetail?>(new ChangeHistoryDetail(
                changeHistoryId,
                "ValidationRole",
                "3001",
                "Rename",
                "Buyer User",
                "Buyer-User",
                "C1008267",
                DateTimeOffset.Parse("2026-08-30T04:04:33Z")));
        }

        public Task<LoginHistoryPage> ReadLoginsAsync(
            LoginHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            LoginReadCount++;
            return Task.FromResult(new LoginHistoryPage(
                [],
                0,
                1,
                50));
        }
    }

    private static LoginHistoryRow CreateLoginHistoryRow() => new(
        91,
        "C1008267",
        DateTimeOffset.Parse("2026-08-30T04:48:09Z"),
        "DENIED",
        "NOT_AUTHORIZED",
        "trace-91");

    private static void AssertDisabledHistorySpan(
        string pager,
        string label)
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
        Assert.DoesNotMatch(
            new Regex($"<a[^>]*>\\s*{label}\\s*</a>"),
            pager);
    }

    private static string ExtractHistoryPager(string html)
    {
        System.Text.RegularExpressions.Match pager = Regex.Match(
            html,
            "<nav[^>]*class=\"management-history-pager\"[^>]*>[\\s\\S]*?</nav>");
        Assert.True(pager.Success, "History pager was not rendered.");
        return pager.Value;
    }

    private static IReadOnlyList<string> ReadHistoryPageLinks(string pager)
    {
        return Regex.Matches(
                pager,
                "<a[^>]*href=\"[^\"]*[?&]page=(\\d+)[^\"]*\"[^>]*>\\s*([^<]+?)\\s*</a>")
            .Select(match =>
                $"{match.Groups[2].Value.Trim()}={match.Groups[1].Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> ReadVisiblePageNumbers(string pager)
    {
        return Regex.Matches(
                pager,
                ">\\s*(\\d+)\\s*</(?:a|span)>")
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static string ReadVisibleText(string html)
    {
        string withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(withoutTags, "\\s+", " ").Trim();
    }

    private static async Task<string> RenderHistoryViewAsync(
        string viewName,
        object model,
        bool localIt = true)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.ApplicationName)
            .Returns(typeof(HistoryController).Assembly.GetName().Name!);
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
        var diagnosticListener = new DiagnosticListener("HistoryViewTests");
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(HistoryController).Assembly);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    ClaimTypes.Role,
                    localIt ? "Local_IT_Admin" : "Admin")
            ], "Test"))
        };
        var routeData = new RouteData();
        routeData.Values["controller"] = "History";
        routeData.Values["action"] = viewName;
        var router = new Mock<IRouter>();
        router.Setup(item => item.GetVirtualPath(
                It.IsAny<VirtualPathContext>()))
            .Returns((VirtualPathContext context) =>
            {
                string controller =
                    context.Values["controller"]?.ToString() ?? "History";
                string action =
                    context.Values["action"]?.ToString() ?? viewName;
                string query = string.Join(
                    "&",
                    context.Values
                        .Where(value =>
                            value.Key is not "controller" and not "action"
                            && value.Value is not null)
                        .Select(value =>
                            $"{WebUtility.UrlEncode(value.Key)}="
                            + WebUtility.UrlEncode(value.Value!.ToString())));
                string path = $"/{controller}/{action}";
                if (query.Length > 0)
                {
                    path += $"?{query}";
                }

                return new VirtualPathData(router.Object, path);
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
            viewPath: $"/Views/History/{viewName}.cshtml",
            isMainPage: true);
        Assert.True(
            found.Success,
            $"View {viewName} was not found: {string.Join(", ", found.SearchedLocations)}");
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
