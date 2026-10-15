namespace HotelPOS.Application.Common.Cqrs;

// A command whose effects belong in the audit trail. Its handler states each entry (action constant from
// AuditActions, entity, id, old and new values) on IAuditTrail; AuditBehavior writes them inside the command's
// transaction once the handler has succeeded. A command that is not declared audited may not record entries.
public interface IAuditedCommand
{
}

// The failure itself is the fact to keep (a wrong password counts towards the lockout and is audited): the
// transaction commits, and the audit entries are written, even when the handler returns a failure.
public interface ICommitOnFailure
{
}
