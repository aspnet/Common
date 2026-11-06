// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Gen.ContextualOptions.Model;

namespace Microsoft.Gen.ContextualOptions;

internal static class Parser
{
    public static OptionsContextType GetContextualOptionTypes(INamedTypeSymbol symbol, TypeDeclarationSyntax typeDeclarationSyntax)
        => new OptionsContextType(symbol, typeDeclarationSyntax, GetContextProperties(symbol));

    // TODO: Separate analyzer for diagnostics.
    //private static OptionsContextType CheckInstantiable(OptionsContextType type)
    //{
    //    if (type.Symbol.IsStatic)
    //    {
    //        type.Diagnostics.AddRange(
    //            type.Definitions
    //                .SelectMany(def => def.Modifiers)
    //                .Where(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))
    //                .Select(modifier => Diagnostic.Create(DiagDescriptors.ContextCannotBeStatic, modifier.GetLocation(), type.Name)));
    //    }

    //    return type;
    //}

    // TODO: Separate analyzer for diagnostics.
    //private static OptionsContextType CheckRefLikeType(OptionsContextType type)
    //{
    //    if (type.Symbol.IsRefLikeType)
    //    {
    //        type.Diagnostics.AddRange(
    //            type.Definitions
    //                .SelectMany(def => def.Modifiers)
    //                .Where(modifier => modifier.IsKind(SyntaxKind.RefKeyword))
    //                .Select(modifier => Diagnostic.Create(DiagDescriptors.ContextCannotBeRefLike, modifier.GetLocation(), type.Name)));
    //    }

    //    return type;
    //}

    // TODO: Separate analyzer for diagnostics.
    //private static OptionsContextType CheckPartial(OptionsContextType type)
    //{
    //    if (!type.Definitions.Any(def => def.Modifiers.Any(static token => token.IsKind(SyntaxKind.PartialKeyword))))
    //    {
    //        type.Diagnostics.AddRange(
    //            type.Definitions.Select(def => Diagnostic.Create(DiagDescriptors.ContextMustBePartial, def.Identifier.GetLocation(), type.Name)));
    //    }

    //    return type;
    //}

    // TODO: Separate analyzer for diagnostics.
    //private static OptionsContextType CheckHasProperties(OptionsContextType type)
    //{
    //    if (type.OptionsContextProperties.IsEmpty)
    //    {
    //        type.Diagnostics.AddRange(
    //            type.Definitions.Select(def => Diagnostic.Create(DiagDescriptors.ContextDoesNotHaveValidProperties, def.Identifier.GetLocation(), type.Name)));
    //    }

    //    return type;
    //}

    private static ImmutableArray<string> GetContextProperties(INamedTypeSymbol symbol)
    {
        return symbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(prop => !prop.IsStatic)
            .Where(prop => !prop.IsWriteOnly)
            .Where(prop => !prop.Type.IsRefLikeType)
            .Where(prop => prop.Type.TypeKind != TypeKind.Pointer)
            .Where(prop => prop.Type.TypeKind != TypeKind.FunctionPointer)
            .Where(prop => prop.Parameters.IsEmpty)
            .Where(prop => prop.ExplicitInterfaceImplementations.IsEmpty)
            .Where(GetterIsPublic)
            .Select(prop => prop.Name)
            .ToImmutableArray();

        static bool GetterIsPublic(IPropertySymbol prop) =>
            prop.GetMethod!.DeclaredAccessibility == Accessibility.Public;
    }
}
