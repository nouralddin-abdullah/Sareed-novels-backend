using Microsoft.AspNetCore.Http;

namespace Application.Characters.Commands.CreateCharacter;

public class CreateCharacterRequest
{
    public string CharacterName { get; set; } = default!;
    public string CharacterDescription { get; set; } = default!;
    public int CharacterAge { get; set; } = default!;
    public IFormFile CharacterImageFile { get; set; } = default!;
}
