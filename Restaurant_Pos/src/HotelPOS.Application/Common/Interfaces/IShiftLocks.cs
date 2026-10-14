namespace HotelPOS.Application.Common.Interfaces;

// PostgreSQL row locks on cash shifts, taken inside the command's transaction. A payment share-locks the terminal's open
// shift; closing a shift locks it for update, so the close waits for payments in flight and counts them, and a payment
// that waited for a close no longer finds the shift open.
public interface IShiftLocks
{
    Task<bool> LockForUpdateAsync(int shiftId, CancellationToken cancellationToken = default);

    Task<int?> LockOpenShiftOfDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
