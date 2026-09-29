using FluentValidation;

namespace Application.ReadingLists.Commands.ReorderNovelsInList;

public class ReorderNovelsInListRequestValidator : AbstractValidator<ReorderNovelsInListRequest>
{
    public ReorderNovelsInListRequestValidator()
    {
        RuleFor(x => x.OrderedNovelIds)
            .NotNull()
            .WithMessage("أرسل روايات القائمة بترتيبها الجديد");
    }
}
