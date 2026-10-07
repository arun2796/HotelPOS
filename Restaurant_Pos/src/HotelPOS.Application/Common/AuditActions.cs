namespace HotelPOS.Application.Common;

public static class AuditActions
{
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string RefreshTokenReuse = "Auth.RefreshTokenReuse";
    public const string PasswordChanged = "User.PasswordChanged";

    public const string UserCreated = "User.Created";
    public const string UserUpdated = "User.Updated";
    public const string UserRoleChanged = "User.RoleChanged";
    public const string UserActivated = "User.Activated";
    public const string UserDeactivated = "User.Deactivated";
    public const string UserPasswordReset = "User.PasswordReset";

    public const string DeviceRegistered = "Device.Registered";

    public const string SettingsUpdated = "Settings.Updated";

    public const string SectionCreated = "Section.Created";
    public const string SectionUpdated = "Section.Updated";
    public const string SectionActivated = "Section.Activated";
    public const string SectionDeactivated = "Section.Deactivated";

    public const string TableCreated = "Table.Created";
    public const string TableUpdated = "Table.Updated";
    public const string TableActivated = "Table.Activated";
    public const string TableDeactivated = "Table.Deactivated";
    public const string TableOutOfService = "Table.OutOfService";
    public const string TableInService = "Table.InService";
    public const string TableReleasedByManager = "Table.ReleasedByManager";

    public const string CategoryCreated = "Category.Created";
    public const string CategoryUpdated = "Category.Updated";
    public const string CategoryActivated = "Category.Activated";
    public const string CategoryDeactivated = "Category.Deactivated";

    public const string StationCreated = "Station.Created";
    public const string StationUpdated = "Station.Updated";
    public const string StationActivated = "Station.Activated";
    public const string StationDeactivated = "Station.Deactivated";

    public const string TaxCreated = "Tax.Created";
    public const string TaxUpdated = "Tax.Updated";
    public const string TaxActivated = "Tax.Activated";
    public const string TaxDeactivated = "Tax.Deactivated";

    public const string ModifierGroupCreated = "ModifierGroup.Created";
    public const string ModifierGroupUpdated = "ModifierGroup.Updated";
    public const string ModifierGroupDeactivated = "ModifierGroup.Deactivated";
    public const string ModifierOptionAdded = "ModifierGroup.OptionAdded";
    public const string ModifierOptionUpdated = "ModifierGroup.OptionUpdated";
    public const string ModifierOptionDeactivated = "ModifierGroup.OptionDeactivated";

    public const string MenuItemCreated = "MenuItem.Created";
    public const string MenuItemUpdated = "MenuItem.Updated";
    public const string MenuItemPriceChanged = "MenuItem.PriceChanged";
    public const string MenuItemAvailabilityChanged = "MenuItem.AvailabilityChanged";
    public const string MenuItemImageChanged = "MenuItem.ImageChanged";
    public const string MenuItemActivated = "MenuItem.Activated";
    public const string MenuItemDeactivated = "MenuItem.Deactivated";

    public const string OrderSubmitted = "Order.Submitted";
    public const string OrderItemsAppended = "Order.ItemsAppended";
    public const string OrderUpdated = "Order.Updated";
    public const string OrderCancelled = "Order.Cancelled";
    public const string OrderServed = "Order.Served";
    public const string OrderItemCancelled = "Order.ItemCancelled";

    public const string BillRequested = "Bill.Requested";
    public const string BillClaimed = "Bill.Claimed";
    public const string BillReleased = "Bill.Released";
    public const string BillClaimOverridden = "Bill.ClaimOverridden";
    public const string BillDiscountApplied = "Bill.DiscountApplied";
    public const string BillDiscountCleared = "Bill.DiscountCleared";
    public const string BillCustomerUpdated = "Bill.CustomerUpdated";
    public const string BillFinalized = "Bill.Finalized";
    public const string BillSettled = "Bill.Settled";
    public const string BillClosed = "Bill.Closed";
    public const string BillReopened = "Bill.Reopened";
    public const string BillVoided = "Bill.Voided";
    public const string PaymentReceived = "Payment.Received";
    public const string PaymentRefunded = "Payment.Refunded";
    public const string ApprovalRejected = "Approval.Rejected";

    public const string DiscountCreated = "Discount.Created";
    public const string DiscountUpdated = "Discount.Updated";
    public const string PaymentMethodCreated = "PaymentMethod.Created";
    public const string PaymentMethodUpdated = "PaymentMethod.Updated";
}
