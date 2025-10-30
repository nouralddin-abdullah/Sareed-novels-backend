using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Characters.Commands.CreateCharacter;

public class CreateCharacterCommandHandler(INovelsRepository novelsRepository, ICharacterRepository characterRepository, IUserContext userContext, IMapper mapper, IFileUploadService fileUploadService) : IRequestHandler<CreateCharacterCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateCharacterCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        var character = mapper.Map<Character>(request);
        if (request.CharacterImageFile != null)
        {
            using var stream = request.CharacterImageFile.OpenReadStream();
            character.CharacterImageUrl = await fileUploadService.UploadCharacterImageAsync(
                stream,
                request.CharacterImageFile.ContentType,
                request.CharacterName
                );
        }
        character.NovelId = request.NovelId;
        var result = await characterRepository.CreateCharacter(character);
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "Character has been made."
            };
        }
        return new OperationResult
        {
            Success = false,
            Message = "Character has not been made."
        };

    }
}
