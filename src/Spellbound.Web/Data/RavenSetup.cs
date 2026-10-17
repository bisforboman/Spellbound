using System.Reflection;
using Raven.Client.Documents.Conventions;

namespace Spellbound.Web.Data;

public static class RavenSetup
{
    public static DocumentConventions Conventions() => new() { FindIdentityProperty = IsDocumentKey };

    /// <summary>
    /// Only string <c>Id</c> properties are document keys. Mage keeps its Guid Id as plain data
    /// and is stored under <see cref="MageKey"/>.
    /// </summary>
    public static bool IsDocumentKey(MemberInfo member) =>
        member is PropertyInfo { Name: "Id" } property && property.PropertyType == typeof(string);

    /// <summary>One mage per player: the owner's id is the key, so a second mage can't be stored.</summary>
    public static string MageKey(string ownerId) => $"mages/{ownerId}";

    public static string NewsKey(string ownerId) => $"news/{ownerId}";
}
