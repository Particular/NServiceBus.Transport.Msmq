using System.IO;
using Newtonsoft.Json;

static class Serializer
{
    static readonly JsonSerializer JsonSerializer;

    static Serializer()
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        JsonSerializer = JsonSerializer.Create(settings);
    }

    public static void Serialize(Stream stream, object target)
    {
        using var stringWriter = new StreamWriter(stream);
        using var jsonWriter = new JsonTextWriter(stringWriter);
        JsonSerializer.Serialize(jsonWriter, target);
    }

    public static T Deserialize<T>(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(reader);
        return JsonSerializer.Deserialize<T>(jsonReader);
    }
}