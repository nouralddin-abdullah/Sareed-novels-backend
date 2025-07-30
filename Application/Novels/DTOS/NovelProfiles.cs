using Application.Novels.Commands.CreateNovel;
using Application.Novels.Commands.UpdateNovel;
using AutoMapper;
using Domain.Entities;

namespace Application.Novels.DTOS;

public class NovelProfiles : Profile
{
    public NovelProfiles()
    {
        CreateMap<CreateNovelCommand, Novel>();
        CreateMap<UpdateNovelCommand, Novel>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));

    }
}
