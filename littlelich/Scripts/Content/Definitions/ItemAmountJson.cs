using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleLich.Content.Definitions;

/// <summary>
/// Reads the compact amount form used throughout the content (phase 11 schema): <c>{ "iron_bar": 3, "coal": 1 }</c>
/// into a list of <see cref="ItemAmount"/> (order kept). Counts must be whole numbers.
/// </summary>
public sealed class ItemAmountMapConverter : JsonConverter<List<ItemAmount>>
{
    public override List<ItemAmount> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("expected an object of id: count, e.g. { \"iron_bar\": 3 }");
        var list = new List<ItemAmount>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return list;
            var id = reader.GetString() ?? "";
            reader.Read();
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var n))
                throw new JsonException($"the count for '{id}' must be a whole number");
            list.Add(new ItemAmount { Id = id, Count = n });
        }
        throw new JsonException("unterminated object");
    }

    public override void Write(Utf8JsonWriter writer, List<ItemAmount> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var a in value) writer.WriteNumber(a.Id, a.Count);
        writer.WriteEndObject();
    }
}

/// <summary>Reads a single amount in the compact form <c>{ "iron_sword": 1 }</c> (exactly one entry).</summary>
public sealed class SingleItemAmountConverter : JsonConverter<ItemAmount>
{
    public override ItemAmount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new ItemAmountMapConverter().Read(ref reader, typeof(List<ItemAmount>), options);
        if (list.Count != 1) throw new JsonException("expected exactly one id: count, e.g. { \"iron_sword\": 1 }");
        return list[0];
    }

    public override void Write(Utf8JsonWriter writer, ItemAmount value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(value.Id, value.Count);
        writer.WriteEndObject();
    }
}
