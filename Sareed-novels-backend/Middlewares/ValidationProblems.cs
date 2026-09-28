using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Sareed_novels_backend.Middlewares;

/// <summary>
/// The answer to a request the validators refuse (HTTP 400): ASP.NET's validation problem, whose
/// <c>errors: {Field: [messages]}</c> clients already read, with an Arabic title and the error middleware's
/// <c>code</c> (<see cref="Code"/>) and <c>message</c>. The validators' messages are Arabic; ASP.NET's own (a missing
/// field, a malformed JSON body, a value of the wrong type) are English and are replaced here with Arabic ones, keeping
/// each error under its field. <c>message</c> is the first validator's message, or else a general one.
/// </summary>
public static partial class ValidationProblems
{
    public const string Code = "ValidationFailed";
    public const string Title = "البيانات المرسلة غير صالحة.";

    /// <summary>A field the request needs but left out or sent empty ("The X field is required.").</summary>
    public const string RequiredMessage = "هذا الحقل مطلوب";

    /// <summary>A body that isn't readable JSON, or JSON whose values don't fit the fields.</summary>
    public const string UnreadableBodyMessage = "تعذّرت قراءة البيانات المرسلة. تأكد من صيغتها وحاول مرة أخرى.";

    /// <summary>A request without the body it needs.</summary>
    public const string MissingBodyMessage = "لم تصل أي بيانات مع الطلب";

    /// <summary>Any other value ASP.NET couldn't use, e.g. text where a number goes.</summary>
    public const string InvalidValueMessage = "القيمة المرسلة غير صالحة";

    public static IActionResult Respond(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, title: Title);

        string? firstArabic = null;
        var unreadable = false;
        foreach (var field in problem.Errors.Keys.ToList())
        {
            var errors = context.ModelState.TryGetValue(field, out var entry) ? entry.Errors : null;
            problem.Errors[field] = problem.Errors[field].Select((message, i) =>
            {
                if (Arabic().IsMatch(message))
                {
                    firstArabic ??= message;
                    return message;
                }
                var arabic = InArabic(field, message, errors is not null && i < errors.Count ? errors[i] : null);
                unreadable |= arabic == UnreadableBodyMessage;
                return arabic;
            }).ToArray();
        }

        problem.Extensions["code"] = Code;
        problem.Extensions["message"] = firstArabic ?? (unreadable ? UnreadableBodyMessage : Title);
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json", "application/problem+xml" } };
    }

    /// <summary>The Arabic for one of ASP.NET's own (English) model binding and validation messages.</summary>
    private static string InArabic(string field, string message, ModelError? error)
    {
        // System.Text.Json's errors come under "$" or "$.path", as a JsonException wrapped in an InputFormatterException.
        if (field.StartsWith('$') || error?.Exception is JsonException or InputFormatterException
            || message.Contains("JSON", StringComparison.OrdinalIgnoreCase))
        {
            return UnreadableBodyMessage;
        }
        if (message.Contains("request body is required", StringComparison.OrdinalIgnoreCase))
        {
            return MissingBodyMessage;
        }
        // "The X field is required.", "A value for the 'x' parameter or property was not provided."
        if (message.Contains("is required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("was not provided", StringComparison.OrdinalIgnoreCase))
        {
            return RequiredMessage;
        }
        return InvalidValueMessage;
    }

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex Arabic();
}
