using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using HotelPOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Support;

public enum ProbeOutcome
{
    Succeed,
    Fail,
    Throw,
}

// What another terminal saw when the probe's event reached it.
public sealed record ProbeEvent(int SectionId, bool SectionVisibleToOtherConnections);

// Creates a section (state), records an audit entry and queues an event, then ends as asked.
public sealed record ProbeCommand(CreateSectionRequest Input, ProbeOutcome Outcome = ProbeOutcome.Succeed)
    : ICommand<int>, IHasInput<CreateSectionRequest>, IRequirePermission, IAuditedCommand
{
    public const string EventName = "ProbeHappened";

    public string Permission => Permissions.TablesManage;
}

public sealed record KeepOnFailureProbe(CreateSectionRequest Input) : ICommand<int>, IAuditedCommand, ICommitOnFailure;

public sealed record UndeclaredAuditProbe(CreateSectionRequest Input) : ICommand<int>;

public static class PipelineProbes
{
    public static void Register(IServiceCollection services)
    {
        services.AddTransient<IRequestHandler<ProbeCommand, Result<int>>, ProbeHandler>();
        services.AddTransient<IRequestHandler<KeepOnFailureProbe, Result<int>>, KeepOnFailureProbeHandler>();
        services.AddTransient<IRequestHandler<UndeclaredAuditProbe, Result<int>>, UndeclaredAuditProbeHandler>();
        services.AddInputValidators(typeof(PipelineProbes).Assembly);
    }

    internal static async Task<Section> AddSectionAsync(IAppDbContext db, IAuditTrail audit, CreateSectionRequest input, CancellationToken cancellationToken)
    {
        var section = new Section(input.Name, input.SortOrder);
        db.Sections.Add(section);
        await db.SaveChangesAsync(cancellationToken);
        audit.Add(AuditActions.SectionCreated, nameof(Section), section.Id.ToString(), newValues: new { section.Name });
        return section;
    }
}

internal sealed class ProbeHandler : ICommandHandler<ProbeCommand, int>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;
    private readonly IServiceScopeFactory _scopes;

    public ProbeHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, IRealtimeNotifier realtime, IServiceScopeFactory scopes)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _realtime = realtime;
        _scopes = scopes;
    }

    public async Task<Result<int>> Handle(ProbeCommand request, CancellationToken cancellationToken)
    {
        var section = await PipelineProbes.AddSectionAsync(_db, _audit, request.Input, cancellationToken);
        _afterCommit.Enqueue(async () =>
        {
            var visible = await VisibleToOtherConnectionsAsync(section.Id);
            await _realtime.PublishAsync(ProbeCommand.EventName, new ProbeEvent(section.Id, visible), RealtimeAudience.All);
        });

        return request.Outcome switch
        {
            ProbeOutcome.Fail => AppErrors.BusinessRule("The probe was told to fail."),
            ProbeOutcome.Throw => throw new InvalidOperationException("The probe was told to throw."),
            _ => section.Id,
        };
    }

    private async Task<bool> VisibleToOtherConnectionsAsync(int sectionId)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Sections.AnyAsync(s => s.Id == sectionId);
    }
}

internal sealed class KeepOnFailureProbeHandler : ICommandHandler<KeepOnFailureProbe, int>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public KeepOnFailureProbeHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<int>> Handle(KeepOnFailureProbe request, CancellationToken cancellationToken)
    {
        await PipelineProbes.AddSectionAsync(_db, _audit, request.Input, cancellationToken);
        return AppErrors.InvalidCredentials();
    }
}

internal sealed class UndeclaredAuditProbeHandler : ICommandHandler<UndeclaredAuditProbe, int>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public UndeclaredAuditProbeHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<int>> Handle(UndeclaredAuditProbe request, CancellationToken cancellationToken) =>
        (await PipelineProbes.AddSectionAsync(_db, _audit, request.Input, cancellationToken)).Id;
}
