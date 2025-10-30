using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Characters.Commands.CreateCharacter;

public class CreateCharacterCommand(Guid novelId, string characterName, string characterDescription, int characterAge, IFormFile characterImageFile) : IRequest<OperationResult>
{
    public Guid NovelId { get; set; } = novelId;
    public string CharacterName { get; set; } = characterName;
    public string CharacterDescription { get; set; } = characterDescription;
    public int CharacterAge { get; set; } = characterAge;
    public IFormFile CharacterImageFile { get; set; } = characterImageFile;
}
