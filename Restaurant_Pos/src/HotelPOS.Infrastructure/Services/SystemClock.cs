using HotelPOS.Application.Common.Interfaces;

namespace HotelPOS.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
