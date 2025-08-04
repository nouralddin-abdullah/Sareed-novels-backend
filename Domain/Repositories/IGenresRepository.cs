using Domain.Entities;

namespace Domain.Repositories;

public interface IGenresRepository
{
    Task<IEnumerable<Genre>> GetAllGenres();
    Task<Genre?> GetBySlug(string slug);
}
