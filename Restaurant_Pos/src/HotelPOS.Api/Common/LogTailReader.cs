using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Api.Common;

// Reads the end of today's API log for the admin Logs screen, so a technician does not need a remote desktop
// session to see why something failed.
public sealed class LogTailReader : ILogTailSource
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public LogTailReader(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<LogTailDto?> ReadAsync(LogTailQuery query, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(ServerPaths.LogDirectory(_configuration, _environment));
        var latest = directory.Exists
            ? directory.EnumerateFiles("api-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (latest is null)
        {
            return null;
        }

        // Serilog keeps the file open for writing; ReadWrite sharing lets us read it anyway.
        await using var stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var entries = new Queue<string>(query.Lines);
        string? currentEntry = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // An entry starts with its timestamp; exception stack lines belong to the entry above them.
            if (line.Length > 4 && char.IsDigit(line[0]) && line[4] == '-')
            {
                Add(entries, currentEntry, query);
                currentEntry = line;
            }
            else
            {
                currentEntry = currentEntry is null ? line : currentEntry + Environment.NewLine + line;
            }
        }

        Add(entries, currentEntry, query);
        return new LogTailDto { FileName = latest.Name, LastWriteUtc = latest.LastWriteTimeUtc, Lines = entries.ToList() };
    }

    private static void Add(Queue<string> entries, string? entry, LogTailQuery query)
    {
        if (entry is null || (!string.IsNullOrEmpty(query.Level) && !entry.Contains($"[{query.Level}]", StringComparison.Ordinal)))
        {
            return;
        }

        if (entries.Count == query.Lines)
        {
            entries.Dequeue();
        }

        entries.Enqueue(entry);
    }
}
