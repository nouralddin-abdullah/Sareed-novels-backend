using Domain.Entities;

namespace Domain.Repositories;

public interface ICharacterRepository
{
    Task<bool> CreateCharacter(Character character);
    Task<bool> UpdateCharacter(Character character);
    Task<bool> DeleteCharacter(Character character);
    Task<IEnumerable<Character>> GetCharacters(Guid novelId);
    Task<Character?> GetCharacter(Guid characterId);

}
