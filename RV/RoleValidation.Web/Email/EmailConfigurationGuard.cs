using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Configuration;

namespace RoleValidation.Web.Email;

// Resource filters run before controller construction. This is important because
// email controllers/stores may resolve lazily validated EmailOptions in constructors.
public sealed class EmailConfigurationGuard(
    EmailProcessingCapability capability,
    IAuthorizationService authorization) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        if (!capability.HasConfigurationErrors)
        {
            await next();
            return;
        }

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) &&
            !HttpMethods.IsHead(context.HttpContext.Request.Method))
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status409Conflict,
                ContentType = "text/plain; charset=utf-8",
                Content = "EMAIL_CONFIGURATION_INVALID"
            };
            return;
        }

        AuthorizationResult access = await authorization.AuthorizeAsync(
            context.HttpContext.User,
            RoleValidationAuthorizationPolicies.LocalItAdministration);
        IReadOnlyList<string> keys = access.Succeeded
            ? capability.ConfigurationKeys
            : [];
        context.Result = new ViewResult
        {
            ViewName = "~/Views/Shared/EmailConfigurationUnavailable.cshtml",
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            ViewData = new ViewDataDictionary<IReadOnlyList<string>>(
                new EmptyModelMetadataProvider(),
                context.ModelState)
            {
                Model = keys
            }
        };
    }
}
