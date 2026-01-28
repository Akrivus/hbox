using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class ActorConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Actor) || objectType == typeof(Actor[]);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (objectType == typeof(Actor[]))
        {
            var actors = new List<Actor>();
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                actors.Add(ReadJsonString(reader.Value as string));
            return actors.ToArray();
        }
        else
            return ReadJsonString(reader.Value as string);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is Actor[] actors)
        {
            writer.WriteStartArray();
            foreach (var actor in actors)
                WriteJsonString(writer, actor);
            writer.WriteEndArray();
        }
        else
            WriteJsonString(writer, value as Actor);
    }

    private Actor ReadJsonString(string name)
    {
        if (name == null)
            return null;

        return Convert(name);
    }

    private void WriteJsonString(JsonWriter writer, Actor actor)
    {
        string name = null;

        if (actor != null)
            name = actor.Name;

        writer.WriteValue(name);
    }

    public static Actor Convert(string name, ChatManagerContext context = null)
    {
        if (context == null)
            context = ChatManagerContext.Current;
        return context.ActorsSearch[name];
    }
}