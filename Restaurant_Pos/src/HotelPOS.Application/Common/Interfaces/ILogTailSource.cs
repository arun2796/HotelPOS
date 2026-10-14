using HotelPOS.Contracts.Admin;

namespace HotelPOS.Application.Common.Interfaces;

// Reads the API's own log files (implemented in the Api project, which knows where Serilog writes them).
public interface ILogTailSource
{
    Task<LogTailDto?> ReadAsync(LogTailQuery query, CancellationToken cancellationToken);
}
