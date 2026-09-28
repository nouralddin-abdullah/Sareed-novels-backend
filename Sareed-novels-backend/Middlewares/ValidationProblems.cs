using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Sareed_novels_backend.Middlewares;

/// <summary>
/// The answer to a request the validators refuse (HTTP 400): ASP.NET's validation problem, whose
/// <c>errors: {Field: [messages]}</c> clients already read, with an Arabic title and the error middleware's
/// <c>code</c> (<see cref="Code"/>) and <c>message</c>: the first Arabic error, or a general one when there is none
/// (ASP.NET's own model binding messages, such as a malformed JSON body, are English and meant for developers).
/// </summary>
public static partial class ValidationProblems
{
    public const string Code = "ValidationFailed";
    public const string Title = "البيانات المرسلة غير صالحة.";

    public static IActionResult Respond(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, title: Title);
        problem.Extensions["code"] = Code;
        problem.Extensions["message"] = problem.Errors.SelectMany(field => field.Value).FirstOrDefault(Arabic().IsMatch) ?? Title;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json", "application/problem+xml" } };
    }

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex Arabic();
}
