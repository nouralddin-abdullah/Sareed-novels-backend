using Domain.Entities;
using Domain.Moderation;
using FluentValidation;

namespace Application.Reports.Commands.CreateReport;

public class CreateReportCommandValidator : AbstractValidator<CreateReportCommand>
{
    public CreateReportCommandValidator()
    {
        RuleFor(r => r.TargetType)
            .Must(type => EnumNames.TryParse<ReportTargetType>(type, out _))
            .WithMessage("نوع المحتوى المُبلَّغ عنه غير صالح");

        RuleFor(r => r.TargetId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("معرّف المحتوى المُبلَّغ عنه غير صالح");

        RuleFor(r => r.Reason)
            .Must(reason => EnumNames.TryParse<ReportReason>(reason, out _))
            .WithMessage("سبب البلاغ غير صالح");

        RuleFor(r => r.Details)
            .MaximumLength(Report.DetailsMaxLength)
            .WithMessage($"يجب ألا تتجاوز تفاصيل البلاغ {Report.DetailsMaxLength} حرف");
    }
}
