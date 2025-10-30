using Application.Characters.Commands.CreateCharacter;
using AutoMapper;
using Domain.Entities;

namespace Application.Characters.DTOS;

public class CharacterProfiles : Profile
{
    public CharacterProfiles()
    {
        CreateMap<CreateCharacterCommand, Character>();
    }
}
