using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;

namespace Kavita_Missing_Chapter_Checker
{
    public class JsonDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                if (decimal.TryParse(reader.GetString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }
                throw new JsonException($"Unable to convert \"{reader.GetString()}\" to decimal.");
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDecimal();
            }
            throw new JsonException($"Unexpected token type {reader.TokenType} when parsing decimal.");
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
}
