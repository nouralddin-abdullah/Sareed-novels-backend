using Domain.Entities;

namespace Domain.Repositories;

public interface IGiftRepository
{
    Task<Gift?> GetGiftById(Guid id);
    Task<(IEnumerable<Gift> gifts, int totalCount)> GetAllGifts(int pageNumber, int pageSize, bool includeInactive = false);
    Task<Gift> CreateGift(Gift gift);
    Task<bool> UpdateGift(Gift gift);
    Task<bool> DeleteGift(Guid id); // Soft delete
    Task<bool> GiftExists(Guid id);
}
