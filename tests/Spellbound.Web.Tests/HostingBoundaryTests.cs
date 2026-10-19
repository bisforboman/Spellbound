using System.Reflection;
using System.Text.RegularExpressions;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>
/// Hosting/ is the reusable game host and must not depend on Spellbound's game code (see CLAUDE.md), so it can move to
/// its own project when a second game comes along. The one shared type is <see cref="Round"/>.
/// </summary>
public sealed class HostingBoundaryTests
{
    private const string HostingNamespace = "Spellbound.Web.Hosting";

    /// <summary>Platform code outside Hosting/ that Hosting may use: the template's Identity account pages.</summary>
    private const string AccountNamespace = "Spellbound.Web.Components.Account";

    private static readonly Assembly Web = typeof(GameHostSetup).Assembly;
    private static readonly Assembly Domain = typeof(Round).Assembly;

    [Fact]
    public void Hosting_types_only_use_the_game_through_Round()
    {
        var offenders = Web.GetTypes()
            .Where(t => t.Namespace?.StartsWith(HostingNamespace, StringComparison.Ordinal) == true)
            .SelectMany(t => ReferencedTypes(t).Select(used => (Owner: t, Used: used)))
            .Where(r => IsGameType(r.Used))
            .Select(r => $"{r.Owner.FullName} uses {r.Used.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Hosting_sources_do_not_name_game_types()
    {
        var gameTypeNames = Domain.GetExportedTypes().Where(t => t != typeof(Round)).Select(t => t.Name)
            .Concat(Web.GetTypes().Where(IsGameType).Select(t => t.Name))
            .Where(name => !name.Contains('<', StringComparison.Ordinal))
            .Distinct()
            .ToList();
        // A name used as a type: not a member (after a dot), a method (before a bracket, unless constructed with new)
        // or a named tuple element or argument (before a colon), so everyday words like Match and Report stay free.
        var names = string.Join('|', gameTypeNames.Select(Regex.Escape));
        var pattern = new Regex(
            $@"\bnew\s+({names})\b|(?<![.\w])({names})\b(?!\s*[(:])|Spellbound\.Web\.(Data|Components\.Game)\b");

        var offenders = Directory.EnumerateFiles(HostingSourceDirectory())
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => pattern.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>A type from Spellbound's game code: the domain (except Round) or the web app outside Hosting and accounts.</summary>
    private static bool IsGameType(Type type) =>
        (type.Assembly == Domain && type != typeof(Round))
        || (type.Assembly == Web
            && type.Namespace is { } ns
            && !ns.StartsWith(HostingNamespace, StringComparison.Ordinal)
            && !ns.StartsWith(AccountNamespace, StringComparison.Ordinal)
            && !IsGenerated(type));

    /// <summary>Compiler-generated helpers (records' contracts, anonymous types and the like) belong to no one.</summary>
    private static bool IsGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal) || type.Namespace == "Microsoft.CodeAnalysis";

    /// <summary>Types in a type's signature: base, interfaces, fields, properties, methods and constructors, with
    /// generic arguments. Nested compiler-generated types (lambdas, async methods) are included, so locals they
    /// capture count too.</summary>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var direct = new List<Type?> { type.BaseType };
        direct.AddRange(type.GetInterfaces());
        direct.AddRange(type.GetFields(All).Select(f => f.FieldType));
        direct.AddRange(type.GetProperties(All).Select(p => p.PropertyType));
        foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
        {
            direct.AddRange(method.GetParameters().Select(p => p.ParameterType));
            if (method is MethodInfo info)
            {
                direct.Add(info.ReturnType);
            }
        }

        foreach (var nested in type.GetNestedTypes(All))
        {
            direct.AddRange(ReferencedTypes(nested));
        }

        return direct.OfType<Type>().SelectMany(Unwrap);
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            return Unwrap(element);
        }

        return type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type) : [type];
    }

    private static string HostingSourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spellbound.slnx")))
            {
                return Path.Combine(dir.FullName, "src", "Spellbound.Web", "Hosting");
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root (Spellbound.slnx).");
    }
}
