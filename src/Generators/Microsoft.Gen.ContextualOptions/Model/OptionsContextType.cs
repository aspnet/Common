// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Gen.ContextualOptions.Model;

// TODO: Equality
internal sealed class OptionsContextType
{
    public readonly ImmutableArray<string> OptionsContextProperties;
    public string Keyword;
    public string? Namespace;
    public string Name;

    public OptionsContextType(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax typeDeclarationSyntax,
        ImmutableArray<string> optionsContextProperties)
    {
        // NOTE: NEVER store INamedTypeSymbol in OptionsContextType.
        // This is better for source generator incrementality.
        Name = symbol.Name;
        Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToString();
        Keyword = typeDeclarationSyntax.Keyword.Text;
        OptionsContextProperties = optionsContextProperties;
    }
}
