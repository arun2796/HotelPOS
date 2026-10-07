using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Auth;

namespace HotelPOS.Application.Auth;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoginResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default);

    Task<Result<CurrentUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
