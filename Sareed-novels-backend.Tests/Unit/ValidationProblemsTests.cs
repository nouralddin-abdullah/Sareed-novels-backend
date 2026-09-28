using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>A refused request body answers in Arabic, also where the message is ASP.NET's own.</summary>
public class ValidationProblemsTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();

    private static ValidationProblemDetails Respond(ModelStateDictionary modelState)
    {
        var context = new ActionContext(new DefaultHttpContext { RequestServices = Services }, new RouteData(), new ActionDescriptor(), modelState);
        var result = Assert.IsType<BadRequestObjectResult>(ValidationProblems.Respond(context));
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(ValidationProblems.Code, problem.Extensions["code"]);
        Assert.Equal(ValidationProblems.Title, problem.Title);
        Assert.All(problem.Errors.Values.SelectMany(m => m), m => Assert.Matches(@"\p{IsArabic}", m));
        return problem;
    }

    [Fact]
    public void A_validators_arabic_message_is_kept_and_answers_first()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Count", "The value 'abc' is not valid for Count.");
        modelState.AddModelError("Name", "اسم القائمة مطلوب");

        var problem = Respond(modelState);

        Assert.Equal("اسم القائمة مطلوب", problem.Extensions["message"]);
        Assert.Equal(["اسم القائمة مطلوب"], problem.Errors["Name"]);
        Assert.Equal([ValidationProblems.InvalidValueMessage], problem.Errors["Count"]);
    }

    [Fact]
    public void A_malformed_json_body_says_it_could_not_be_read()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.count", new InputFormatterException("The JSON value could not be converted to System.Int32. Path: $.count", new JsonException()), new EmptyModelMetadataProvider().GetMetadataForType(typeof(int)));
        modelState.AddModelError("command", "The command field is required.");

        var problem = Respond(modelState);

        Assert.Equal(ValidationProblems.UnreadableBodyMessage, problem.Extensions["message"]);
        Assert.Equal([ValidationProblems.UnreadableBodyMessage], problem.Errors["$.count"]);
        Assert.Equal([ValidationProblems.RequiredMessage], problem.Errors["command"]);
    }

    [Fact]
    public void Missing_fields_and_bodies_get_arabic_messages_under_their_keys()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Token", "The Token field is required.");
        modelState.AddModelError("pageNumber", "A value for the 'pageNumber' parameter or property was not provided.");
        modelState.AddModelError("", "A non-empty request body is required.");

        var problem = Respond(modelState);

        Assert.Equal(ValidationProblems.Title, problem.Extensions["message"]);
        Assert.Equal([ValidationProblems.RequiredMessage], problem.Errors["Token"]);
        Assert.Equal([ValidationProblems.RequiredMessage], problem.Errors["pageNumber"]);
        Assert.Equal([ValidationProblems.MissingBodyMessage], problem.Errors[""]);
    }
}
