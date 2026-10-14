namespace HotelPOS.Application.Common.Cqrs;

// A request built around a Contracts DTO (the HTTP body or query string). The DTO's validators run in the
// pipeline for the request, so validation errors name the DTO's fields exactly as the API always has.
public interface IHasInput<out TInput>
{
    TInput Input { get; }
}
