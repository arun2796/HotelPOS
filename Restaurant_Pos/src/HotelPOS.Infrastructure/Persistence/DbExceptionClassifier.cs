using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HotelPOS.Infrastructure.Persistence;

public static class DbExceptionClassifier
{
    // SQLSTATE 23505: unique_violation (unique index or primary key).
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
