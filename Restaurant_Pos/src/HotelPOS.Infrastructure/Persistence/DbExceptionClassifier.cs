using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HotelPOS.Infrastructure.Persistence;

/// <summary>Recognises provider-specific database errors without leaking Npgsql into the API layer.</summary>
public static class DbExceptionClassifier
{
    // SQLSTATE 23505: unique_violation (unique index or primary key).
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
