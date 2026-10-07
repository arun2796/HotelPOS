namespace HotelPOS.Domain.Common;

/// <summary>Creation/modification stamps, filled in automatically when changes are saved.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    int? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    int? UpdatedBy { get; set; }
}

/// <summary>Entity protected by an optimistic-concurrency token (SQL Server rowversion).</summary>
public interface IHasRowVersion
{
    byte[] RowVersion { get; set; }
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
