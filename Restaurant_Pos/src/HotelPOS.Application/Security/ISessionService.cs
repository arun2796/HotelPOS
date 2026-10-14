using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Application.Security;

public interface ISessionService
{
    Task<Result<IReadOnlyList<UserSessionDto>>> GetUserSessionsAsync(int userId, CancellationToken cancellationToken = default);

    Task<Result> LogoutAllAsync(int userId, CancellationToken cancellationToken = default);

    Task<Result> UnlockUserAsync(int userId, CancellationToken cancellationToken = default);
}

