using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// JSON for domain objects stored in jsonb columns. The domain keeps its state behind private setters and in private
/// fields, which System.Text.Json skips, and it stays free of serialization attributes. So for domain classes with a
/// non-public parameterless constructor (the domain's "for deserialization" convention, e.g. <see cref="Mage"/>), every
/// instance field is stored instead of the public properties, and objects are created with that constructor: a field
/// missing from older JSON keeps its initializer. Read-only collection fields are filled in place, so a dictionary
/// keeps its comparer. Everything else (records such as <see cref="HallOfFame"/>) serializes the usual way.
/// </summary>
public static class DomainJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Stored {typeof(T).Name} was null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoreFields } },
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    private static void StoreFields(JsonTypeInfo info)
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (info.Kind != JsonTypeInfoKind.Object
            || info.Type.Assembly != typeof(Mage).Assembly
            || info.Type.GetConstructor(Instance, Type.EmptyTypes) is not { IsPublic: false } constructor)
        {
            return;
        }

        info.Properties.Clear();
        info.CreateObject = () => constructor.Invoke(null);
        foreach (var field in info.Type.GetFields(Instance))
        {
            var property = info.CreateJsonPropertyInfo(field.FieldType, JsonName(field));
            property.Get = field.GetValue;
            if (field.FieldType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(field.FieldType))
            {
                // Fill the collection the constructor made, so it keeps its comparer (the army's is case-insensitive).
                property.ObjectCreationHandling = JsonObjectCreationHandling.Populate;
                if (!field.IsInitOnly)
                {
                    property.Set = field.SetValue; // for a field the constructor leaves null
                }
            }
            else
            {
                property.Set = field.SetValue;
            }

            info.Properties.Add(property);
        }
    }

    /// <summary>"&lt;Land&gt;k__BackingField" and "_army" become "land" and "army".</summary>
    private static string JsonName(FieldInfo field)
    {
        var name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name.TrimStart('_');
        return JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
