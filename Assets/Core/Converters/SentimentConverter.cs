using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class SentimentConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Sentiment) || objectType == typeof(Sentiment[]);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (objectType == typeof(Sentiment[]))
        {
            var sentiments = new List<Sentiment>();
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                sentiments.Add(ReadJsonString(reader.Value as string));
            return sentiments.ToArray();
        }
        else
            return ReadJsonString(reader.Value as string);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is Sentiment[] sentiments)
        {
            writer.WriteStartArray();
            foreach (var sentiment in sentiments)
                WriteJsonString(writer, sentiment);
            writer.WriteEndArray();
        }
        else
            WriteJsonString(writer, value as Sentiment);
    }

    private Sentiment ReadJsonString(string name)
    {
        if (name == null)
            name = "Neutral";

        return Convert(name);
    }

    private void WriteJsonString(JsonWriter writer, Sentiment sentiment)
    {
        string name = "Neutral";

        if (sentiment != null)
            name = sentiment.Name;

        writer.WriteValue(name);
    }

    public static Sentiment Convert(string name, ChatManagerContext context = null)
    {
        if (context == null)
            context = ChatManagerContext.Current;
        return context.SentimentsSearch[name];
    }
}