using Domain.Constants;
using FluentValidation;

namespace Application.Chapters.Commands.UpdateChapter
{
    public class UpdateChapterValidator : AbstractValidator<UpdateChapterRequest>
    {
        public UpdateChapterValidator()
        {
            RuleFor(c => c.Title)
                .NotNull()
                .WithMessage("اكتب عنوان الفصل")
                .MaximumLength(50)
                .WithMessage("يجب ألا يتجاوز عنوان الفصل 50 حرفًا");

            RuleFor(c => c.Content)
                .NotNull()
                .WithMessage("اكتب نص الفصل")
                .MaximumLength(100000)
                .WithMessage("يجب ألا يتجاوز نص الفصل 100000 حرف");

            RuleFor(c => c.Status)
                .Must(status => ChapterStatuses.All.Contains(status))
                .When(c => c.Status != null)
                .WithMessage("حالة الفصل يجب أن تكون «مسودة» أو «منشور»");
        }
    }
}
