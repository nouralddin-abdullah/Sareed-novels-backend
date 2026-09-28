using Domain.Entities;

namespace Domain.Repositories;

public interface IUserDevicesRepository
{
    /// <summary>
    /// Registers <paramref name="device"/>'s token for its user: updates the row that already holds the token (moving
    /// it from another user if needed) or inserts one. Safe under concurrent calls with the same token.
    /// </summary>
    Task Upsert(UserDevice device);

    /// <summary>Removes the token if it belongs to <paramref name="userId"/>; does nothing otherwise.</summary>
    Task Remove(string userId, string token);

    /// <summary>
    /// Removes the token whoever registered it: for a phone that signed out without a valid session (an expired or
    /// revoked token, or offline). Knowing the token is the proof; it only stops pushes to that phone.
    /// </summary>
    Task RemoveToken(string token);
}
