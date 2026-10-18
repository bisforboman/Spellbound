using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Spellbound.Domain;
using Spellbound.Domain.Skirmish;

namespace Spellbound.Web.Data;

/// <summary>
/// JSON for domain objects stored in jsonb columns. The domain keeps its state behind private setters and in private
/// fields, which System.Text.Json skips, and it stays free of serialization attributes. So for domain classes with a
/// non-public parameterless constructor (the domain's "for deserialization" convention, e.g. <see cref="Mage"/>), every
/// instance field is stored instead of the public properties, and objects are created with that constructor: a field
/// missing from older JSON keeps its initializer. Read-only collection fields are filled in place, so a dictionary
/// keeps its comparer. Everything else (records such as <see cref="HallOfFame"/>) serializes the usual way.
/// Skirmish orders are a record hierarchy; they're stored with a "kind" field naming the order.
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
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoreFields, NameOrderKinds } },
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
            if (field.IsInitOnly && field.FieldType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(field.FieldType))
            {
                property.ObjectCreationHandling = JsonObjectCreationHandling.Populate; // fill the constructor's collection
            }
            else
            {
                property.Set = field.SetValue;
            }

            info.Properties.Add(property);
        }
    }

    /// <summary>A Skirmish order is stored with its kind, e.g. {"kind":"claim","regionId":3}, so a list of orders reads back.</summary>
    private static void NameOrderKinds(JsonTypeInfo info)
    {
        if (info.Type != typeof(SkirmishOrder))
        {
            return;
        }

        info.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "kind",
            DerivedTypes =
            {
                new JsonDerivedType(typeof(ClaimOrder), "claim"),
                new JsonDerivedType(typeof(BuildOrder), "build"),
                new JsonDerivedType(typeof(RecruitOrder), "recruit"),
                new JsonDerivedType(typeof(AttackOrder), "attack"),
                new JsonDerivedType(typeof(CastOrder), "cast"),
                new JsonDerivedType(typeof(HexOrder), "hex"),
            },
        };
    }

    /// <summary>"&lt;Land&gt;k__BackingField" and "_army" become "land" and "army".</summary>
    private static string JsonName(FieldInfo field)
    {
        var name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name.TrimStart('_');
        return JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
