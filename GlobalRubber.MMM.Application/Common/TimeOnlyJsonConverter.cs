using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Supports parsing both "HH:mm:ss" and "HH:mm" (as emitted by HTML &lt;input type="time"&gt;)
/// as well as standard 12/24 hour time formats into TimeOnly.
/// </summary>
public sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["HH:mm:ss", "HH:mm", "h:mm tt", "hh:mm tt", "H:mm", "H:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Time string cannot be empty.");
        }

        if (TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            || TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time))
        {
            return time;
        }

        throw new JsonException($"Unable to parse \"{value}\" as TimeOnly.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Nullable variant of TimeOnlyJsonConverter.
/// </summary>
public sealed class NullableTimeOnlyJsonConverter : JsonConverter<TimeOnly?>
{
    private static readonly string[] Formats = ["HH:mm:ss", "HH:mm", "h:mm tt", "hh:mm tt", "H:mm", "H:mm:ss"];

    public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            || TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time))
        {
            return time;
        }

        throw new JsonException($"Unable to parse \"{value}\" as TimeOnly.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
