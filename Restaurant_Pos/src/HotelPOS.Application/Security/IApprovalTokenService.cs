using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Auth;

namespace HotelPOS.Application.Security;

public interface IApprovalTokenService
{
    Task<Result<ApprovalTokenResponse>> IssueApprovalTokenAsync(ApprovalTokenRequest request, CancellationToken cancellationToken = default);

    Task<Result<int>> ValidateApprovalTokenAsync(string token, string expectedActionType, CancellationToken cancellationToken = default);
}

