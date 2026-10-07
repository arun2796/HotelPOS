using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Billing;

public interface IDiscountService
{
    Task<IReadOnlyList<DiscountDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<DiscountDto>> CreateAsync(SaveDiscountRequest request, CancellationToken cancellationToken = default);

    Task<Result<DiscountDto>> UpdateAsync(int id, SaveDiscountRequest request, CancellationToken cancellationToken = default);

    Task<Result<DiscountDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public interface IPaymentMethodService
{
    Task<IReadOnlyList<PaymentMethodDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<PaymentMethodDto>> CreateAsync(SavePaymentMethodRequest request, CancellationToken cancellationToken = default);

    Task<Result<PaymentMethodDto>> UpdateAsync(int id, SavePaymentMethodRequest request, CancellationToken cancellationToken = default);

    Task<Result<PaymentMethodDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class DiscountService : IDiscountService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;

    public DiscountService(IAppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DiscountDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        (await _db.Discounts.AsNoTracking()
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken))
        .Select(ToDto).ToList();

    public async Task<Result<DiscountDto>> CreateAsync(SaveDiscountRequest request, CancellationToken cancellationToken = default)
    {
        Discount discount;
        try
        {
            discount = new Discount(request.Name, request.Type, request.Value, request.RequiresApproval);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await NameTakenAsync(discount.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"A discount named '{discount.Name}' already exists.");
        }

        if (!request.IsActive)
        {
            discount.Deactivate();
        }

        _db.Discounts.Add(discount);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.DiscountCreated, nameof(Discount), discount.Id.ToString(CultureInfo.InvariantCulture), newValues: ToDto(discount));
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(discount);
    }

    public async Task<Result<DiscountDto>> UpdateAsync(int id, SaveDiscountRequest request, CancellationToken cancellationToken = default)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (discount is null)
        {
            return AppErrors.NotFound("Discount", id);
        }

        var before = ToDto(discount);
        try
        {
            discount.Update(request.Name, request.Type, request.Value, request.RequiresApproval);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await NameTakenAsync(discount.Name, id, cancellationToken))
        {
            return AppErrors.Duplicate($"A discount named '{discount.Name}' already exists.");
        }

        if (request.IsActive)
        {
            discount.Activate();
        }
        else
        {
            discount.Deactivate();
        }

        _audit.Record(AuditActions.DiscountUpdated, nameof(Discount), discount.Id.ToString(CultureInfo.InvariantCulture), before, ToDto(discount));
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(discount);
    }

    public async Task<Result<DiscountDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (discount is null)
        {
            return AppErrors.NotFound("Discount", id);
        }

        if (discount.IsActive)
        {
            discount.Deactivate();
            _audit.Record(AuditActions.DiscountUpdated, nameof(Discount), discount.Id.ToString(CultureInfo.InvariantCulture), newValues: new { discount.IsActive });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToDto(discount);
    }

    internal static DiscountDto ToDto(Discount d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Type = d.Type,
        Value = d.Value,
        RequiresApproval = d.RequiresApproval,
        IsActive = d.IsActive,
    };

    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var upper = name.ToUpperInvariant();
        return _db.Discounts.AnyAsync(d => d.Name.ToUpper() == upper && d.Id != exceptId, cancellationToken);
    }
}

public sealed class PaymentMethodService : IPaymentMethodService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;

    public PaymentMethodService(IAppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        (await _db.PaymentMethods.AsNoTracking()
            .Where(m => includeInactive || m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(cancellationToken))
        .Select(ToDto).ToList();

    public async Task<Result<PaymentMethodDto>> CreateAsync(SavePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        PaymentMethod method;
        try
        {
            method = new PaymentMethod(request.Name, request.Code, request.RequiresReference, request.SortOrder);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await _db.PaymentMethods.AnyAsync(m => m.Code == method.Code, cancellationToken))
        {
            return AppErrors.Duplicate($"Payment method code '{method.Code}' is already used.");
        }

        if (!request.IsActive)
        {
            method.Deactivate();
        }

        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.PaymentMethodCreated, nameof(PaymentMethod), method.Id.ToString(CultureInfo.InvariantCulture), newValues: ToDto(method));
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(method);
    }

    public async Task<Result<PaymentMethodDto>> UpdateAsync(int id, SavePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return AppErrors.NotFound("Payment method", id);
        }

        var before = ToDto(method);
        try
        {
            method.Update(request.Name, request.Code, request.RequiresReference, request.SortOrder);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await _db.PaymentMethods.AnyAsync(m => m.Code == method.Code && m.Id != id, cancellationToken))
        {
            return AppErrors.Duplicate($"Payment method code '{method.Code}' is already used.");
        }

        var error = await SetActiveAsync(method, request.IsActive, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        _audit.Record(AuditActions.PaymentMethodUpdated, nameof(PaymentMethod), method.Id.ToString(CultureInfo.InvariantCulture), before, ToDto(method));
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(method);
    }

    public async Task<Result<PaymentMethodDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return AppErrors.NotFound("Payment method", id);
        }

        var error = await SetActiveAsync(method, active: false, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        _audit.Record(AuditActions.PaymentMethodUpdated, nameof(PaymentMethod), method.Id.ToString(CultureInfo.InvariantCulture), newValues: new { method.IsActive });
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(method);
    }

    internal static PaymentMethodDto ToDto(PaymentMethod m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Code = m.Code,
        RequiresReference = m.RequiresReference,
        IsCash = m.IsCash,
        SortOrder = m.SortOrder,
        IsActive = m.IsActive,
    };

    private async Task<AppError?> SetActiveAsync(PaymentMethod method, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            method.Activate();
            return null;
        }

        if (method.IsActive && !await _db.PaymentMethods.AnyAsync(m => m.IsActive && m.Id != method.Id, cancellationToken))
        {
            return AppErrors.BusinessRule("At least one payment method must stay active.");
        }

        method.Deactivate();
        return null;
    }
}
