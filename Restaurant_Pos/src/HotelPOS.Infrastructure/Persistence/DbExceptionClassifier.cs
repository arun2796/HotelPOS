using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

/// <summary>Recognises provider-specific database errors without leaking SqlClient into the API layer.</summary>
public static class DbExceptionClassifier
{
    // 2601: duplicate key in unique index, 2627: unique/primary key constraint violation.
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
