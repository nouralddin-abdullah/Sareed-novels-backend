using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class GiftRepository(ApplicationDbContext dbContext) : IGiftRepository
{
    public async Task<Gift?> GetGiftById(Guid id)
    {
        return await dbContext.Gifts
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<(IEnumerable<Gift> gifts, int totalCount)> GetAllGifts(int pageNumber, int pageSize, bool includeInactive = false)
    {
        var query = dbContext.Gifts.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(g => g.IsActive);
        }

        var totalCount = await query.CountAsync();

        var gifts = await query
            .OrderBy(g => g.Cost)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (gifts, totalCount);
    }

    public async Task<Gift> CreateGift(Gift gift)
    {
        dbContext.Gifts.Add(gift);
        await dbContext.SaveChangesAsync();
        return gift;
    }

    public async Task<bool> UpdateGift(Gift gift)
    {
        gift.UpdatedAt = DateTime.UtcNow;
        dbContext.Gifts.Update(gift);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteGift(Guid id)
    {
        var gift = await GetGiftById(id);
        if (gift == null) return false;

        gift.IsActive = false;
        gift.UpdatedAt = DateTime.UtcNow;
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> GiftExists(Guid id)
    {
        return await dbContext.Gifts.AnyAsync(g => g.Id == id && g.IsActive);
    }
}
