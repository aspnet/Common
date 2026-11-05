// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace Microsoft.Extensions.LocalAnalyzers.Utilities;

internal static class SymbolExtensions
{
    /// <summary>
    /// Determines whether the symbol is externally visible outside this assembly.
    /// </summary>
    public static bool IsExternallyVisible(this ISymbol symbol)
    {
        while (symbol.Kind != SymbolKind.Namespace)
        {
            switch (symbol.DeclaredAccessibility)
            {
                // If we see anything private, then the symbol is private.
                case Accessibility.NotApplicable:
                case Accessibility.Private:
                    return false;

                // If we see anything internal, then knock it down from public to
                // internal.
                case Accessibility.Internal:
                case Accessibility.ProtectedAndInternal:
                    return false;
            }

            symbol = symbol.ContainingSymbol;
        }

        return true;
    }

    public static bool HasAttribute(this ISymbol sym, INamedTypeSymbol attribute)
    {
        foreach (var a in sym.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasAttribute(this ISymbol sym, string attributeName)
    {
        foreach (var a in sym.GetAttributes())
        {
            var attrType = a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attrType == attributeName)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsContaminated(this ISymbol symbol, INamedTypeSymbol? contaminationAttribute)
    {
        return (contaminationAttribute != null) && IsContaminated(symbol);

        bool IsContaminated(ISymbol symbol)
        {
            if (symbol.HasAttribute(contaminationAttribute))
            {
                // symbol is annotated
                return true;
            }

            if (symbol.ContainingAssembly != null
                && symbol.ContainingAssembly.HasAttribute(contaminationAttribute))
            {
                // symbol's assembly is annotated
                return true;
            }

            var container = symbol.ContainingType;
            while (container != null)
            {
                if (IsContaminated(container))
                {
                    // symbol's container is annotated
                    return true;
                }

                container = container.ContainingType;
            }

            if (symbol is INamedTypeSymbol type)
            {
                var baseType = type.BaseType;
                while (baseType != null)
                {
                    if (IsContaminated(baseType))
                    {
                        // symbol's base type is annotated
                        return true;
                    }

                    baseType = baseType.BaseType;
                }
            }

            return false;
        }
    }

    public static bool IsContaminated(this ISymbol symbol, string attributeName)
    {
        return IsContaminated(symbol);

        bool IsContaminated(ISymbol symbol)
        {
            if (symbol.HasAttribute(attributeName))
            {
                // symbol is annotated
                return true;
            }

            if (symbol.ContainingAssembly != null
                && symbol.ContainingAssembly.HasAttribute(attributeName))
            {
                // symbol's assembly is annotated
                return true;
            }

            var container = symbol.ContainingType;
            while (container != null)
            {
                if (IsContaminated(container))
                {
                    // symbol's container is annotated
                    return true;
                }

                container = container.ContainingType;
            }

            if (symbol is INamedTypeSymbol type)
            {
                var baseType = type.BaseType;
                while (baseType != null)
                {
                    if (IsContaminated(baseType))
                    {
                        // symbol's base type is annotated
                        return true;
                    }

                    baseType = baseType.BaseType;
                }
            }

            return false;
        }
    }

    internal static ITypeSymbol? GetFieldOrPropertyType(this ISymbol symbol)
    {
        if (symbol is IFieldSymbol fieldSymbol)
        {
            return fieldSymbol.Type;
        }
        else if (symbol is IPropertySymbol propertySymbol)
        {
            return propertySymbol.Type;
        }
        else
        {
            return null;
        }
    }
}
