using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CharacterRepository(ApplicationDbContext dbContext) : ICharacterRepository
{
    public async Task<bool> CreateCharacter(Character character)
    {
        await dbContext.AddAsync(character);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<bool> DeleteCharacter(Character character)
    {
        dbContext.Remove(character);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<Character?> GetCharacter(Guid characterId)
    {
        return await dbContext.Characters.Where(c => c.Id == characterId).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Character>> GetCharacters(Guid novelId)
    {
        return await dbContext.Characters.Where(c => c.NovelId == novelId).ToListAsync();
    }

    public async Task<bool> UpdateCharacter(Character character)
    {
        dbContext.Update(character);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }
}
