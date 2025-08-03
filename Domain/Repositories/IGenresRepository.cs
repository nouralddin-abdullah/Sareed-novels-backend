using Domain.Entities;

namespace Domain.Repositories;

public interface IGenresRepository
{
    Task<IEnumerable<Genre>> GetAllGenres();
}
