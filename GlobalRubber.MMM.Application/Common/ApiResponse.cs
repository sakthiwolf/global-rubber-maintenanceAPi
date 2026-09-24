using System.Text.Json.Serialization;

namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Standard, non-generic API response envelope used for endpoints that do not return a payload
/// (for example a successful command with no content, or a failure with no data to report).
/// Every controller action in this solution returns either <see cref="ApiResponse"/> or
/// <see cref="ApiResponse{T}"/> so the React frontend always receives the same shape.
/// </summary>
public class ApiResponse
{
    [JsonPropertyOrder(0)]
    public bool Success { get; init; }

    [JsonPropertyOrder(1)]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyOrder(3)]
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ApiResponse Ok(string message = "Request completed successfully.") =>
        new() { Success = true, Message = message };

    public static ApiResponse Fail(string message, IEnumerable<string>? errors = null) =>
        new()
        {
            Success = false,
            Message = message,
            Errors = errors?.ToArray() ?? Array.Empty<string>(),
        };

    public static ApiResponse Fail(string message, string error) =>
        Fail(message, new[] { error });
}

/// <summary>
/// Standard API response envelope for endpoints that return a payload. <see cref="Data"/> is
/// <c>null</c> whenever <see cref="ApiResponse.Success"/> is <c>false</c>.
/// </summary>
/// <typeparam name="T">The shape of the payload returned to the caller.</typeparam>
public sealed class ApiResponse<T> : ApiResponse
{
    [JsonPropertyOrder(2)]
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string message = "Request completed successfully.") =>
        new() { Success = true, Message = message, Data = data };

    public static new ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null) =>
        new()
        {
            Success = false,
            Message = message,
            Errors = errors?.ToArray() ?? Array.Empty<string>(),
            Data = default,
        };

    public static new ApiResponse<T> Fail(string message, string error) =>
        Fail(message, new[] { error });
}
