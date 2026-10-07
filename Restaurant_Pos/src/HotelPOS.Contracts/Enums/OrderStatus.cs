namespace HotelPOS.Contracts.Enums;

public enum OrderStatus
{
    Draft = 1,
    Submitted = 2,
    Accepted = 3,
    Preparing = 4,
    Ready = 5,
    Served = 6,
    BillRequested = 7,
    Billed = 8,
    Paid = 9,
    Completed = 10,
    Cancelled = 11,
}
