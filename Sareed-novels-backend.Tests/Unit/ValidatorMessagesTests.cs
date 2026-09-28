using System.Text.RegularExpressions;
using FluentValidation;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Every rule of every validator answers in Arabic. A WithMessage covers only the rule right before it, so a chain
/// like NotEmpty().Length(3, 20).WithMessage(...) used to answer FluentValidation's English for NotEmpty.
/// </summary>
public class ValidatorMessagesTests
{
    public static TheoryData<Type> Validators() =>
        new(typeof(Application.Extensions.ServiceCollectionExtensions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IValidator).IsAssignableFrom(t)));

    [Theory]
    [MemberData(nameof(Validators))]
    public void Every_rule_has_an_arabic_message(Type validatorType)
    {
        var validator = (IValidator)Activator.CreateInstance(validatorType)!;

        var components = validator.CreateDescriptor().Rules.SelectMany(rule => rule.Components
            .Select(component => (Rule: $"{rule.PropertyName}: {component.Validator.Name}", Message: component.GetUnformattedErrorMessage())))
            .ToList();

        Assert.NotEmpty(components);
        Assert.All(components, c => Assert.True(Regex.IsMatch(c.Message, @"\p{IsArabic}"), $"{validatorType.Name} {c.Rule}: {c.Message}"));
    }

    [Fact]
    public void A_rule_without_its_own_message_is_seen_as_english()
    {
        var validator = new InlineValidator<string> { v => v.RuleFor(s => s).NotEmpty().Length(3, 20).WithMessage("اختر اسمًا") };

        var messages = validator.CreateDescriptor().Rules.SelectMany(r => r.Components).Select(c => c.GetUnformattedErrorMessage()).ToList();

        Assert.DoesNotMatch(@"\p{IsArabic}", messages[0]);
        Assert.Equal("اختر اسمًا", messages[1]);
    }
}
