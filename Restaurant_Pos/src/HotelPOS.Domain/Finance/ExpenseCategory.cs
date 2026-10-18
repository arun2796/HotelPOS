using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Finance;

public sealed class ExpenseCategory : BaseEntity
{
    private ExpenseCategory()
    {
    }

    public ExpenseCategory(string name, string? description = null, int sortOrder = 0)
    {
        ValidateAndApply(name, description, sortOrder);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public void Update(string name, string? description, int sortOrder)
    {
        ValidateAndApply(name, description, sortOrder);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    private void ValidateAndApply(string name, string? description, int sortOrder)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            throw new DomainException("Category name is required.", ErrorCodes.ValidationError);
        }

        if (cleanName.Length > ExpenseLimits.CategoryNameMaxLength)
        {
            throw new DomainException($"Category name cannot exceed {ExpenseLimits.CategoryNameMaxLength} characters.", ErrorCodes.ValidationError);
        }

        var cleanDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (cleanDescription?.Length > ExpenseLimits.CategoryDescriptionMaxLength)
        {
            throw new DomainException($"Category description cannot exceed {ExpenseLimits.CategoryDescriptionMaxLength} characters.", ErrorCodes.ValidationError);
        }

        Name = cleanName;
        Description = cleanDescription;
        SortOrder = sortOrder;
    }
}

