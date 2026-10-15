namespace HotelPOS.Application.Common.Cqrs;

// Which business days a command changes. Day null means the current business day; a bill adds the day it was settled
// in (a refund or reopen changes that day's figures as well as today's).
public sealed record BusinessDayScope(int? BillId = null, DateOnly? Day = null)
{
    public static BusinessDayScope Today { get; } = new();
}

// A command that changes bills, payments or cash of a business day. BusinessDayLockBehavior refuses it with DAY_CLOSED
// when one of its days is closed, and holds a shared lock on those days until the command commits, so Day End's close
// either waits for it or it waits for Day End and then sees the day closed.
public interface IBusinessDayGuarded
{
    BusinessDayScope BusinessDayScope { get; }
}
