namespace HotelPOS.Application.Common.Cqrs;

// Checked by AuthorizationBehavior against the caller's permissions. The endpoint policy stays the outer gate;
// this keeps the rule true for every other caller of the request (tests, background work, later features).
public interface IRequirePermission
{
    string Permission { get; }
}
