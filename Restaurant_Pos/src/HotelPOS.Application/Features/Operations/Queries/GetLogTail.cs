using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Operations.Queries;

// The end of today's API log, for a technician without remote desktop access. Empty when there is no log yet.
public sealed record GetLogTailQuery(LogTailQuery Input) : IQuery<LogTailDto>, IHasInput<LogTailQuery>, IRequirePermission
{
    public string Permission => Permissions.ServerManage;
}

public sealed class LogTailQueryValidator : AbstractValidator<LogTailQuery>
{
    public LogTailQueryValidator()
    {
        RuleFor(x => x.Lines).InclusiveBetween(1, LogTailQuery.MaxLines);
        RuleFor(x => x.Level).Must(l => l is null or "VRB" or "DBG" or "INF" or "WRN" or "ERR" or "FTL")
            .WithMessage("Level must be one of VRB, DBG, INF, WRN, ERR, FTL.");
    }
}

internal sealed class GetLogTailHandler : IQueryHandler<GetLogTailQuery, LogTailDto>
{
    private readonly ILogTailSource _logs;

    public GetLogTailHandler(ILogTailSource logs)
    {
        _logs = logs;
    }

    public async Task<Result<LogTailDto>> Handle(GetLogTailQuery request, CancellationToken cancellationToken) =>
        await _logs.ReadAsync(request.Input, cancellationToken) ?? new LogTailDto();
}
