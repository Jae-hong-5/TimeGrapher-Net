using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure MVVM means the view-models hold no UI-framework type: no type under
/// <c>TimeGrapher.App.ViewModels</c> may reference Avalonia (or ScottPlot). The wave removed the
/// last such reference (the Avalonia.Thickness review-slider margin moved to a View-layer
/// converter); these guards lock that boundary against regression. Two complementary checks:
/// a recursive source scan (catches references anywhere, including method bodies) and a reflection
/// scan of the compiled view-model types' surface (robust against aliased imports / qualified types
/// the text scan could phrase around).
/// </summary>
public sealed class ViewModelPurityTests
{
    private const string ViewModelsNamespace = "TimeGrapher.App.ViewModels";

    private static readonly string[] UiFrameworkRoots = { "Avalonia", "ScottPlot" };

    [Fact]
    public void ViewModelSourceDoesNotReferenceUiFrameworks()
    {
        string directory = LocateDirectory("src/TimeGrapher.App/ViewModels");

        // Guard against a vacuous pass: if the directory moves/empties or the scan
        // returns nothing, the per-file asserts below never run and the boundary
        // guard would silently pass without checking any view-model. Recursive so a
        // view-model added in a sub-folder cannot slip past the scan.
        string[] files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            // Check for actual references (an import or a qualified type), not the bare word, so a
            // doc comment mentioning the framework does not false-positive.
            Assert.DoesNotContain("using Avalonia", source);
            Assert.DoesNotContain("Avalonia.", source);
            Assert.DoesNotContain("using ScottPlot", source);
            Assert.DoesNotContain("ScottPlot.", source);
        }
    }

    [Fact]
    public void ViewModelTypesDoNotReferenceUiFrameworks()
    {
        Assembly appAssembly = typeof(MainWindowViewModel).Assembly;
        Type[] viewModelTypes = appAssembly
            .GetTypes()
            .Where(t => t.Namespace == ViewModelsNamespace)
            .ToArray();

        // Guard against a vacuous pass: the namespace must still hold types, and the
        // canonical view-model must be among them, otherwise the surface scan would
        // silently confirm nothing.
        Assert.NotEmpty(viewModelTypes);
        Assert.Contains(typeof(MainWindowViewModel), viewModelTypes);

        var violations = new List<string>();
        foreach (Type type in viewModelTypes)
        {
            foreach (Type referenced in SurfaceTypes(type))
            {
                if (IsUiFrameworkType(referenced))
                {
                    violations.Add($"{type.FullName} -> {referenced.FullName}");
                }
            }
        }

        string[] distinct = violations.Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.True(
            distinct.Length == 0,
            "ViewModels must not reference a UI-framework type:\n" + string.Join("\n", distinct));
    }

    // The view-model types' declared surface: base type, interfaces, fields, properties, the
    // signatures of constructors and methods, and the constraints of any generic type/method
    // parameters. DeclaredOnly keeps the scan to each view-model's own members (inherited object
    // members are System types).
    private static IEnumerable<Type> SurfaceTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType != null)
        {
            yield return type.BaseType;
        }

        foreach (Type iface in type.GetInterfaces())
        {
            yield return iface;
        }

        foreach (Type constraint in GenericParameterConstraints(type.GetGenericArguments()))
        {
            yield return constraint;
        }

        foreach (FieldInfo field in type.GetFields(all))
        {
            yield return field.FieldType;
        }

        foreach (PropertyInfo property in type.GetProperties(all))
        {
            yield return property.PropertyType;
        }

        foreach (ConstructorInfo ctor in type.GetConstructors(all))
        {
            foreach (ParameterInfo parameter in ctor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (MethodInfo method in type.GetMethods(all))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (Type constraint in GenericParameterConstraints(method.GetGenericArguments()))
            {
                yield return constraint;
            }
        }
    }

    private static IEnumerable<Type> GenericParameterConstraints(Type[] genericArguments)
    {
        foreach (Type argument in genericArguments)
        {
            if (!argument.IsGenericParameter)
            {
                continue;
            }

            // A constraint can name a framework type (e.g. where T : AvaloniaObject); the
            // arguments themselves are open parameters that Unwrap skips, so no recursion cycle.
            foreach (Type constraint in argument.GetGenericParameterConstraints())
            {
                yield return constraint;
            }
        }
    }

    private static bool IsUiFrameworkType(Type type)
    {
        foreach (Type part in Unwrap(type))
        {
            if (MatchesUiFramework(part.Namespace) || MatchesUiFramework(part.Assembly.GetName().Name))
            {
                return true;
            }
        }

        return false;
    }

    // Exact root or a dotted sub-namespace/assembly (Avalonia, Avalonia.Controls, ScottPlot,
    // ScottPlot.Avalonia), so an unrelated "AvaloniaSomething" cannot false-positive.
    private static bool MatchesUiFramework(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (string root in UiFrameworkRoots)
        {
            if (name == root || name.StartsWith(root + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Unwrap arrays/by-ref/pointer element types and generic arguments so a smuggled
    // List<Avalonia.X> or Avalonia.X[] is caught. Open generic parameters (T) carry no real
    // framework identity here (their constraints are scanned separately), so they are skipped.
    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.IsGenericParameter)
        {
            yield break;
        }

        if (type.HasElementType)
        {
            Type? element = type.GetElementType();
            if (element != null)
            {
                foreach (Type part in Unwrap(element))
                {
                    yield return part;
                }
            }

            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type part in Unwrap(argument))
                {
                    yield return part;
                }
            }
        }
    }

    private static string LocateDirectory(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(relativePath);
    }
}
