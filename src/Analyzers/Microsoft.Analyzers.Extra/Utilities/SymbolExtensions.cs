// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace Microsoft.Extensions.ExtraAnalyzers.Utilities;

internal static class SymbolExtensions
{
    /// <summary>
    /// Determines whether the current instance is an ancestor type of the parameter.
    /// </summary>
    /// <param name="potentialAncestor">The potential ancestor being inspected.</param>
    /// <param name="potentialDescendant">The type to test.</param>
    /// <returns><see langword="true"/> if <paramref name="potentialDescendant"/> derives directly or indirectly from <paramref name="potentialAncestor"/>.</returns>
    public static bool IsAncestorOf(this ITypeSymbol potentialAncestor, ITypeSymbol potentialDescendant)
    {
        ITypeSymbol? t = potentialDescendant;
        while (true)
        {
            t = t.BaseType;
            if (t == null)
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(t, potentialAncestor))
            {
                return true;
            }
        }
    }

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

    public static bool ImplementsPublicInterface(this IMethodSymbol method)
    {
        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            if (iface.IsExternallyVisible())
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var impl = method.ContainingType.FindImplementationForInterfaceMember(member);
                    if (SymbolEqualityComparer.Default.Equals(impl, method))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
