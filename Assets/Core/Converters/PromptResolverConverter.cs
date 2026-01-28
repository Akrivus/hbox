using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class PromptResolverConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(PromptResolver) || objectType == typeof(PromptResolver[]);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (objectType == typeof(PromptResolver[]))
        {
            var prompts = new List<PromptResolver>();
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                prompts.Add(ReadJsonString(reader.Value as string));
            return prompts.ToArray();
        }
        else
            return ReadJsonString(reader.Value as string);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is PromptResolver[] prompts)
        {
            writer.WriteStartArray();
            foreach (var prompt in prompts)
                WriteJsonString(writer, prompt);
            writer.WriteEndArray();
        }
        else
            WriteJsonString(writer, value as PromptResolver);
    }

    private PromptResolver ReadJsonString(string path)
    {
        if (path == null)
            return new PromptResolver(ChatManagerContext.Current, path);

        return Convert(path);
    }

    private void WriteJsonString(JsonWriter writer, PromptResolver prompt)
    {
        string path = null;

        if (prompt != null)
            path = prompt.Path;

        writer.WriteValue(path);
    }

    public static PromptResolver Convert(string path, ChatManagerContext context = null)
    {
        if (context == null)
            context = ChatManagerContext.Current;
        return new PromptResolver(context, path);
    }
}