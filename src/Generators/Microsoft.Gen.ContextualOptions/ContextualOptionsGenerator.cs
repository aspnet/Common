// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Gen.ContextualOptions.Model;

namespace Microsoft.Gen.ContextualOptions;

[Generator]
public class ContextualOptionsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<OptionsContextType> types = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Microsoft.Extensions.Options.Contextual.OptionsContextAttribute",
                (node, _) => node is TypeDeclarationSyntax,
                (context, _) => CreateOptionsContextType(context));

        IncrementalValueProvider<(Compilation, ImmutableArray<OptionsContextType>)> compilationAndTypes =
            context.CompilationProvider.Combine(types.Collect());

        context.RegisterSourceOutput(types.Collect(), static (spc, source) => HandleAnnotatedTypes(source, spc));
    }

    private static OptionsContextType CreateOptionsContextType(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var node = context.TargetNode;
        return Parser.GetContextualOptionTypes(symbol, (TypeDeclarationSyntax)node);
    }

    private static void HandleAnnotatedTypes(ImmutableArray<OptionsContextType> types, SourceProductionContext context)
    {
        if (types.Length > 0)
        {
            var emitter = new Emitter();
            context.AddSource($"ContextualOptions.g.cs", emitter.Emit(types.OrderBy(x => x.Namespace + "." + x.Name)));
        }
    }
}
