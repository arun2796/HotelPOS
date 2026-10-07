namespace HotelPOS.Domain.Common;

/// <summary>Creation/modification stamps, filled in automatically when changes are saved.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    int? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    int? UpdatedBy { get; set; }
}

/// <summary>
/// Entity protected by an optimistic-concurrency token. Mapped to PostgreSQL's <c>xmin</c> system
/// column, which changes on every update of the row; never set it from application code.
/// </summary>
public interface IHasRowVersion
{
    uint RowVersion { get; set; }
}

/// <summary>Base class for entities with an int identity key.</summary>
public abstract class BaseEntity : IAuditable
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
