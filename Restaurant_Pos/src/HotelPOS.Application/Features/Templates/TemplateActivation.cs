using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Printing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Templates;

// Makes one version the active one of its type (audited, announced to every terminal after the commit). The next
// print of that type uses it: documents carry the template in force, so no terminal needs to cache it.
internal sealed class TemplateActivation
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;

    public TemplateActivation(IAppDbContext db, ICurrentUser currentUser, IClock clock, IAuditTrail audit, IAfterCommit afterCommit, IRealtimeNotifier realtime)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _afterCommit = afterCommit;
        _realtime = realtime;
    }

    public async Task ActivateAsync(DocumentTemplate template, CancellationToken cancellationToken)
    {
        // Two activations of the same type at once: the unique "one active version per type" index lets one commit
        // and answers the other with a conflict (409), so a type never has two active versions.
        var current = await _db.DocumentTemplates
            .Where(t => t.DocumentType == template.DocumentType && t.IsActive)
            .ToListAsync(cancellationToken);
        var previous = current.FirstOrDefault(t => t.Id != template.Id);
        foreach (var other in current.Where(t => t.Id != template.Id))
        {
            other.Deactivate();
        }

        // Deactivate first: the "one active version per type" index is checked statement by statement.
        await _db.SaveChangesAsync(cancellationToken);
        template.Activate(_clock.UtcNow, _currentUser.UserId);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.TemplateActivated, nameof(DocumentTemplate), template.Id.ToString(CultureInfo.InvariantCulture),
            oldValues: previous is null ? null : new { previous.Version, previous.Name },
            newValues: new { template.DocumentType, template.Version, template.Name });
        var activated = new TemplateActivatedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = template.Id,
            DocumentType = template.DocumentType,
            Version = template.Version,
        };
        _afterCommit.Enqueue(() => _realtime.PublishAsync(HubEvents.TemplateActivated, activated, RealtimeAudience.All));
    }
}
