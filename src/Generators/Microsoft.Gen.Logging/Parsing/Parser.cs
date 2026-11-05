// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Gen.Logging.Model;
using Microsoft.Gen.Shared;

namespace Microsoft.Gen.Logging.Parsing;

internal sealed partial class Parser
{
    // Bounds how deep nested property types are walked, so that unbounded type graphs
    // (e.g. Node<T> with a Node<Node<T>> property) can't overflow the compiler's stack.
    private const int MaxTypeTraversalDepth = 32;

    // ITypeParameterSymbol.AllowsRefLikeType was added in Roslyn 4.9 (C# 13). Access via a compiled
    // delegate so the same source file compiles against all supported Roslyn versions, while
    // avoiding the per-call overhead of PropertyInfo.GetValue boxing.
    private static readonly Func<ITypeParameterSymbol, bool>? _getAllowsRefLikeType =
        (Func<ITypeParameterSymbol, bool>?)typeof(ITypeParameterSymbol)
            .GetProperty("AllowsRefLikeType")?.GetGetMethod()!
            .CreateDelegate(typeof(Func<ITypeParameterSymbol, bool>));

    private readonly CancellationToken _cancellationToken;
    private readonly Compilation _compilation;
    private readonly Action<Diagnostic> _reportDiagnostic;
    private bool _failedMethod;

    public Parser(Compilation compilation, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _cancellationToken = cancellationToken;
        _reportDiagnostic = reportDiagnostic;
    }

    /// <summary>
    /// Gets the set of logging types containing methods to output.
    /// </summary>
    [SuppressMessage("Maintainability", "CA1505:Avoid unmaintainable code", Justification = "Fix this in a follow-up")]
    public IReadOnlyList<LoggingType> GetLogTypes(IEnumerable<TypeDeclarationSyntax> types)
    {
        Action<DiagnosticDescriptor, Location?, object?[]?> diagReport = Diag; // Keeping one instance of the delegate
        var symbols = SymbolLoader.LoadSymbols(_compilation, diagReport);
        if (symbols is null)
        {
            // nothing to do if required symbols aren't available
            return Array.Empty<LoggingType>();
        }

        var ids = new HashSet<int>();
        var eventNames = new HashSet<string>();
        var results = new List<LoggingType>();
        var parameterSymbols = new Dictionary<LoggingMethodParameter, IParameterSymbol>();

        // we enumerate by syntax tree, to minimize the need to instantiate semantic models (since they're expensive)
        foreach (var group in types.GroupBy(x => x.SyntaxTree))
        {
            SyntaxTree syntaxTree = group.Key;
            SemanticModel? sm = _compilation.GetSemanticModel(syntaxTree);

            foreach (var typeDec in group)
            {
                // stop if we're asked to
                _cancellationToken.ThrowIfCancellationRequested();

                LoggingType? lt = null;
                string nspace = string.Empty;
                string? loggerMember = null;
                bool loggerMemberNullable = false;
                ISymbol? secondLoggerMember = null;

                ids.Clear();

                foreach (MemberDeclarationSyntax member in typeDec.Members)
                {
                    var method = member as MethodDeclarationSyntax;
                    if (method is null)
                    {
                        // we only care about methods
                        continue;
                    }

                    var attrLoc = GetLoggerMessageAttribute(method, sm, symbols);
                    if (attrLoc is null)
                    {
                        // doesn't have the magic attribute we like, so ignore
                        continue;
                    }

                    var methodSymbol = sm.GetDeclaredSymbol(method, _cancellationToken)!;

                    _failedMethod = false;
                    var (lm, keepMethod) = ProcessMethod(method, methodSymbol, attrLoc);

                    parameterSymbols.Clear();
                    MethodParsingState parsingState = default;

                    foreach (var paramSymbol in methodSymbol.Parameters)
                    {
                        var lp = ProcessParameter(lm, paramSymbol, symbols, ref parsingState);
                        if (lp is null)
                        {
                            keepMethod = false;
                            continue;
                        }

                        parameterSymbols[lp] = paramSymbol;

                        var foundDataClassificationAttributesInProps = false;

                        var logPropertiesAttribute = ParserUtilities.GetSymbolAttributeAnnotationOrDefault(symbols.LogPropertiesAttribute, paramSymbol);
                        if (logPropertiesAttribute is not null)
                        {
                            if (!ProcessLogPropertiesForParameter(
                                logPropertiesAttribute,
                                lm,
                                lp,
                                paramSymbol,
                                symbols,
                                ref foundDataClassificationAttributesInProps))
                            {
                                lp.Properties.Clear();
                            }
                        }

                        var tagProviderAttribute = ParserUtilities.GetSymbolAttributeAnnotationOrDefault(symbols.TagProviderAttribute, paramSymbol);
                        if (tagProviderAttribute is not null)
                        {
                            if (!ProcessTagProviderForParameter(
                                tagProviderAttribute,
                                lp,
                                paramSymbol,
                                symbols))
                            {
                                lp.TagProvider = null;
                            }
                        }

                        if (lp.HasDataClassification && (lp.HasProperties || lp.HasTagProvider))
                        {
                            Diag(DiagDescriptors.CantUseDataClassificationWithLogPropertiesOrTagProvider, paramSymbol.GetLocation());
                            lp.ClassificationAttributeTypes.Clear();
                        }

                        if (lp.HasProperties && lp.HasTagProvider)
                        {
                            Diag(DiagDescriptors.CantMixAttributes, paramSymbol.GetLocation());
                            lp.Properties.Clear();
                            lp.TagProvider = null;
                        }

#pragma warning disable S1067 // Expressions should not be too complex
                        if (lp.IsNormalParameter
                            && (logPropertiesAttribute is null)
                            && (tagProviderAttribute is null)
                            && !lp.IsStringifiable
                            && paramSymbol.Type.Kind != SymbolKind.TypeParameter)
                        {
                            Diag(DiagDescriptors.DefaultToString, paramSymbol.GetLocation(), paramSymbol.Type, paramSymbol.Name);
                        }
#pragma warning restore S1067 // Expressions should not be too complex

                        bool forceAsTemplateParam = false;

                        bool parameterInTemplate = lm.Templates.Contains(lp.TagName, StringComparer.OrdinalIgnoreCase) ||
                            lm.Templates.Contains(lp.ParameterNameWithAtIfNeeded, StringComparer.OrdinalIgnoreCase) ||
                            lm.Templates.Contains($"@{lp.ParameterName}", StringComparer.OrdinalIgnoreCase);

                        var loggingProperties = logPropertiesAttribute != null || tagProviderAttribute != null;
                        if (lp.IsLogger && parameterInTemplate)
                        {
                            Diag(DiagDescriptors.ShouldntMentionLoggerInMessage, attrLoc, lp.ParameterName);
                            forceAsTemplateParam = true;
                        }
                        else if (lp.IsException && parameterInTemplate)
                        {
                            Diag(DiagDescriptors.ShouldntMentionExceptionInMessage, attrLoc, lp.ParameterName);
                            forceAsTemplateParam = true;
                        }
                        else if (lp.IsLogLevel && parameterInTemplate)
                        {
                            Diag(DiagDescriptors.ShouldntMentionLogLevelInMessage, attrLoc, lp.ParameterName);
                            forceAsTemplateParam = true;
                        }
                        else if (lp.IsNormalParameter && !parameterInTemplate && !loggingProperties && !string.IsNullOrEmpty(lm.Message))
                        {
                            Diag(DiagDescriptors.ParameterHasNoCorrespondingTemplate, paramSymbol.GetLocation(), lp.ParameterName);
                        }

                        var purelyStructuredLoggingParameter = loggingProperties && !parameterInTemplate;
                        if (lp.IsNormalParameter &&
                            !lp.HasDataClassification &&
                            !purelyStructuredLoggingParameter &&
                            paramSymbol.Type.IsRecord)
                        {
                            if (foundDataClassificationAttributesInProps ||
                                RecordHasSensitivePublicMembers(paramSymbol.Type, symbols))
                            {
                                Diag(DiagDescriptors.RecordTypeSensitiveArgumentIsInTemplate, paramSymbol.GetLocation(), lp.ParameterName, lm.Name);
                                keepMethod = false;
                            }
                        }

                        lm.Parameters.Add(lp);
                        if (lp.IsNormalParameter || forceAsTemplateParam)
                        {
                            if (parameterInTemplate)
                            {
                                lp.UsedAsTemplate = true;
                            }
                        }
                    }

                    if (keepMethod)
                    {
                        if (lm.IsStatic && !parsingState.FoundLogger)
                        {
                            Diag(DiagDescriptors.MissingLoggerParameter, method.ParameterList.GetLocation(), lm.Name);
                            keepMethod = false;
                        }
                        else if (!lm.IsStatic && parsingState.FoundLogger)
                        {
                            Diag(DiagDescriptors.LoggingMethodShouldBeStatic, method.Identifier.GetLocation());
                        }
                        else if (!lm.IsStatic && !parsingState.FoundLogger)
                        {
                            if (loggerMember is null)
                            {
                                (loggerMember, secondLoggerMember, loggerMemberNullable) = FindLoggerMember(sm, typeDec, symbols.ILoggerSymbol);
                            }

                            if (secondLoggerMember != null)
                            {
                                Diag(DiagDescriptors.MultipleLoggerMembers, secondLoggerMember.GetLocation(), typeDec.Identifier.Text);
                                keepMethod = false;
                            }
                            else if (loggerMember is null)
                            {
                                Diag(DiagDescriptors.MissingLoggerMember, method.Identifier.GetLocation(), typeDec.Identifier.Text);
                                keepMethod = false;
                            }
                            else
                            {
                                lm.LoggerMember = loggerMember;
                                lm.LoggerMemberNullable = loggerMemberNullable;
                            }
                        }

                        if (lm.Level is null && !parsingState.FoundLogLevel)
                        {
                            Diag(DiagDescriptors.MissingLogLevel, method.GetLocation());

                            lm.Level = 1;
                        }

                        if (keepMethod &&
                            string.IsNullOrWhiteSpace(lm.Message) &&
                            !lm.EventId.HasValue &&
                            lm.Parameters.TrueForAll(static x => x.IsLogger || x.IsLogLevel))
                        {
                            if (!_failedMethod)
                            {
                                Diag(DiagDescriptors.EmptyLoggingMethod, method.Identifier.GetLocation(), methodSymbol.Name);
                            }
                        }

                        foreach (var t in lm.Templates)
                        {
                            bool found = false;
                            foreach (LoggingMethodParameter p in lm.Parameters)
                            {
                                if (t.Equals(p.TagName, StringComparison.OrdinalIgnoreCase) ||
                                    t.Equals(p.ParameterNameWithAtIfNeeded, StringComparison.OrdinalIgnoreCase) ||
                                    (t[0] == '@' && t.Substring(1).Equals(p.ParameterNameWithAtIfNeeded, StringComparison.OrdinalIgnoreCase)))
                                {
                                    found = true;
                                    p.TagName = t;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                Diag(DiagDescriptors.TemplateHasNoCorrespondingParameter, attrLoc, t);
                            }
                        }

                        CheckTagNamesAreUnique(lm, parameterSymbols);
                    }

                    if (lt is null)
                    {
                        // determine the namespace the class is declared in, if any
                        SyntaxNode? potentialNamespaceParent = typeDec.Parent;
                        while (potentialNamespaceParent != null &&
                            potentialNamespaceParent is not NamespaceDeclarationSyntax &&
                            potentialNamespaceParent is not FileScopedNamespaceDeclarationSyntax)
                        {
                            potentialNamespaceParent = potentialNamespaceParent.Parent;
                        }

                        BaseNamespaceDeclarationSyntax? namespaceParent = potentialNamespaceParent as BaseNamespaceDeclarationSyntax;
                        if (namespaceParent != null)
                        {
                            nspace = namespaceParent.Name.ToString();
                            while (true)
                            {
                                namespaceParent = namespaceParent.Parent as NamespaceDeclarationSyntax;
                                if (namespaceParent is null)
                                {
                                    break;
                                }

                                nspace = $"{namespaceParent.Name}.{nspace}";
                            }
                        }
                    }

                    if (keepMethod)
                    {
                        if (lt is null)
                        {
                            lt = new LoggingType
                            {
                                Keyword = typeDec.Keyword.ValueText,
                                Namespace = nspace,
                                Name = typeDec.Identifier.ToString() + typeDec.TypeParameterList,
                                Parent = null,
                            };

                            lt.AllMembers.AddRange(methodSymbol.ContainingType.GetMembers().Select(x => x.Name));

                            LoggingType currentLoggerClass = lt;
                            var parentLoggerClass = typeDec.Parent as TypeDeclarationSyntax;
                            var parentType = methodSymbol.ContainingType.ContainingType;

                            static bool IsAllowedKind(SyntaxKind kind) =>
                                kind == SyntaxKind.ClassDeclaration ||
                                kind == SyntaxKind.StructDeclaration ||
                                kind == SyntaxKind.RecordDeclaration;

                            while (parentLoggerClass != null && IsAllowedKind(parentLoggerClass.Kind()))
                            {
                                currentLoggerClass.Parent = new LoggingType
                                {
                                    Keyword = parentLoggerClass.Keyword.ValueText,
                                    Namespace = nspace,
                                    Name = parentLoggerClass.Identifier.ToString() + parentLoggerClass.TypeParameterList,
                                    Parent = null,
                                };

                                currentLoggerClass = currentLoggerClass.Parent;
                                parentLoggerClass = parentLoggerClass.Parent as TypeDeclarationSyntax;
                                parentType = parentType.ContainingType;
                            }
                        }

                        lt.Methods.Add(lm);
                    }
                }

                if (lt != null)
                {
                    results.Add(lt);
                }
            }
        }

        return results;

        (LoggingMethod lm, bool keepMethod) ProcessMethod(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, Location attrLoc)
        {
            var attr = ParserUtilities.GetSymbolAttributeAnnotationOrDefault(symbols.LoggerMessageAttribute, methodSymbol)!;

            var (eventId, level, message, eventName, skipEnabledCheck) = AttributeProcessors.ExtractLoggerMessageAttributeValues(attr, symbols);

            var lm = new LoggingMethod
            {
                Name = methodSymbol.Name,
                Level = level,
                Message = message,
                EventId = eventId,
                EventName = eventName,
                SkipEnabledCheck = skipEnabledCheck,
                IsExtensionMethod = methodSymbol.IsExtensionMethod,
                IsStatic = methodSymbol.IsStatic,
                Modifiers = method.Modifiers.ToString(),
                HasXmlDocumentation = HasXmlDocumentation(method),
            };

            var keepMethod = true;

            if (!TemplateProcessor.ExtractTemplates(message, lm.Templates))
            {
                Diag(DiagDescriptors.MalformedFormatStrings, method.Identifier.GetLocation(), method.Identifier.ToString());
                keepMethod = false;
            }

            if (!methodSymbol.ReturnsVoid)
            {
                // logging methods must return void
                Diag(DiagDescriptors.LoggingMethodMustReturnVoid, method.ReturnType.GetLocation());
                keepMethod = false;
            }

            foreach (var tp in methodSymbol.TypeParameters)
            {
                if (_getAllowsRefLikeType?.Invoke(tp) == true)
                {
                    // 'allows ref struct' anti-constraint is not supported because the generated code stores
                    // parameters in fields and cannot hold ref struct type arguments.
                    Diag(DiagDescriptors.LoggingMethodHasAllowsRefStructConstraint, method.Identifier.GetLocation());
                    keepMethod = false;
                    break;
                }

                lm.TypeParameters.Add(new LoggingMethodTypeParameter
                {
                    Name = tp.Name,
                    Constraints = GetTypeParameterConstraints(tp),
                });
            }

            bool isPartial = methodSymbol.IsPartialDefinition;
            if (method.Body != null)
            {
                Diag(DiagDescriptors.LoggingMethodHasBody, method.Body.GetLocation());
                keepMethod = false;
            }
            else if (!isPartial)
            {
                Diag(DiagDescriptors.LoggingMethodMustBePartial, method.Identifier.GetLocation());
                keepMethod = false;
            }

            // ensure there are no duplicate ids.
            if (lm.EventId != null)
            {
                if (!ids.Add(lm.EventId.Value))
                {
                    Diag(DiagDescriptors.ShouldntReuseEventIds, attrLoc, lm.EventId.Value, methodSymbol.ContainingType.Name);
                }
            }

            // ensure there are no duplicate event names.
            if (lm.EventName != null)
            {
                if (!eventNames.Add(lm.EventName))
                {
                    Diag(DiagDescriptors.ShouldntReuseEventNames, attrLoc, lm.EventName, methodSymbol.ContainingType.Name);
                }
            }

            var msg = lm.Message;
#pragma warning disable S1067 // Expressions should not be too complex
            if (msg.StartsWith("INFORMATION:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("INFO:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase))
#pragma warning restore S1067 // Expressions should not be too complex
            {
                Diag(DiagDescriptors.RedundantQualifierInMessage, attrLoc, methodSymbol.Name);
            }

            return (lm, keepMethod);
        }
    }

    private static bool HasXmlDocumentation(MethodDeclarationSyntax method)
    {
        var triviaList = method.GetLeadingTrivia();
        foreach (var trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetTypeParameterConstraints(ITypeParameterSymbol typeParameter)
    {
        var constraints = new List<string>();

        if (typeParameter.HasReferenceTypeConstraint)
        {
            string classConstraint = typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class";
            constraints.Add(classConstraint);
        }
        else if (typeParameter.HasValueTypeConstraint)
        {
            // HasUnmanagedTypeConstraint also implies HasValueTypeConstraint
            constraints.Add(typeParameter.HasUnmanagedTypeConstraint ? "unmanaged" : "struct");
        }
        else if (typeParameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            if (constraintType is IErrorTypeSymbol)
            {
                continue;
            }

            constraints.Add(constraintType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)));
        }

        if (typeParameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }

        return constraints.Count > 0 ? string.Join(", ", constraints) : null;
    }

    // Returns all the classification attributes attached to a symbol.
    private static List<INamedTypeSymbol> GetDataClassificationAttributes(ISymbol symbol, SymbolHolder symbols)
        => symbol
            .GetAttributes()
            .Select(static attribute => attribute.AttributeClass)
            .Where(x => x is not null && symbols.DataClassificationAttribute is not null && ParserUtilities.IsBaseOrIdentity(x, symbols.DataClassificationAttribute, symbols.Compilation))
            .Where(x => !SymbolEqualityComparer.Default.Equals(x, symbols.NoDataClassificationAttribute))
            .Select(static x => x!)
            .ToList();

    private void CheckTagNamesAreUnique(LoggingMethod lm, Dictionary<LoggingMethodParameter, IParameterSymbol> parameterSymbols)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in lm.Parameters)
        {
            if (!parameter.IsNormalParameter)
            {
                continue;
            }

            if (parameter.HasProperties)
            {
                parameter.TraverseParameterPropertiesTransitively((chain, leaf) =>
                {
                    if (parameter.OmitReferenceName)
                    {
                        chain = chain.Skip(1);
                    }

                    var fullName = string.Join("_", chain.Concat(new[] { leaf }).Select(static x => x.TagName));
                    if (!names.Add(fullName))
                    {
                        Diag(DiagDescriptors.TagNameCollision, parameterSymbols[parameter].GetLocation(), parameter.ParameterName, fullName, lm.Name);
                    }
                });
            }
            else if (!names.Add(parameter.TagName))
            {
                Diag(DiagDescriptors.TagNameCollision, parameterSymbols[parameter].GetLocation(), parameter.ParameterName, parameter.TagName, lm.Name);
            }
        }
    }

    private LoggingMethodParameter? ProcessParameter(
        LoggingMethod lm,
        IParameterSymbol paramSymbol,
        SymbolHolder symbols,
        ref MethodParsingState parsingState)
    {
        var paramName = paramSymbol.Name;

        var needsAtSign = false;
        if (!paramSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            var paramSyntax = paramSymbol.DeclaringSyntaxReferences[0].GetSyntax(_cancellationToken) as ParameterSyntax;
            if (!string.IsNullOrEmpty(paramSyntax!.Identifier.Text))
            {
                needsAtSign = paramSyntax.Identifier.Text[0] == '@';
            }
        }

        if (string.IsNullOrWhiteSpace(paramName))
        {
            // semantic problem, just bail quietly
            return null;
        }

        var paramTypeSymbol = paramSymbol.Type;
        if (paramTypeSymbol is IErrorTypeSymbol)
        {
            // semantic problem, just bail quietly
            return null;
        }

        if (paramSymbol.IsParams)
        {
            Diag(DiagDescriptors.LoggingMethodParameterParams, paramSymbol.GetLocation(), paramName);
            return null;
        }

        string? qualifier = null;
        if (paramSymbol.RefKind == RefKind.In)
        {
            qualifier = "in";
        }
        else if (paramSymbol.RefKind == RefKind.RefReadOnlyParameter)
        {
            qualifier = "ref readonly";
        }
        else if (paramSymbol.RefKind != RefKind.None)
        {
            // Parameter has "ref", "out" modifier, no can do
            Diag(DiagDescriptors.LoggingMethodParameterRefKind, paramSymbol.GetLocation(), paramSymbol.ContainingSymbol.Name, paramName);
            return null;
        }

        if (paramSymbol.ScopedKind != ScopedKind.None)
        {
            Diag(DiagDescriptors.LoggingMethodParameterParams, paramSymbol.GetLocation(), paramName);
            return null;
        }

        string typeName = paramTypeSymbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

        var paramDataClassAttributes = new HashSet<string>(
            GetDataClassificationAttributes(paramSymbol, symbols)
            .Distinct(SymbolEqualityComparer.Default)
            .Select(x => x!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        var extractedType = paramTypeSymbol;
        if (paramTypeSymbol.IsNullableOfT())
        {
            // extract the T from a Nullable<T>
            extractedType = ((INamedTypeSymbol)paramTypeSymbol).TypeArguments[0];
        }

        var tagNameAttribute = ParserUtilities.GetSymbolAttributeAnnotationOrDefault(symbols.TagNameAttribute, paramSymbol);
        var tagName = tagNameAttribute != null
            ? AttributeProcessors.ExtractTagNameAttributeValues(tagNameAttribute)
            : lm.GetTemplatesForParameter(paramName).FirstOrDefault() ?? paramName;

        var lp = new LoggingMethodParameter
        {
            ParameterName = paramName,
            TagName = tagName,
            Type = typeName,
            Qualifier = qualifier,
            NeedsAtSign = needsAtSign,
            ClassificationAttributeTypes = paramDataClassAttributes,
            IsNullable = paramTypeSymbol.NullableAnnotation == NullableAnnotation.Annotated,
            IsReference = paramTypeSymbol.IsReferenceType,
            IsLogger = !parsingState.FoundLogger && ParserUtilities.IsBaseOrIdentity(paramTypeSymbol, symbols.ILoggerSymbol, _compilation),
            IsException = !parsingState.FoundException && ParserUtilities.IsBaseOrIdentity(paramTypeSymbol, symbols.ExceptionSymbol, _compilation),
            IsLogLevel = !parsingState.FoundLogLevel && SymbolEqualityComparer.Default.Equals(paramTypeSymbol, symbols.LogLevelSymbol),
            IsEnumerable = extractedType.IsEnumerable(symbols),
            ImplementsIConvertible = paramTypeSymbol.ImplementsIConvertible(symbols),
            ImplementsIFormattable = paramTypeSymbol.ImplementsIFormattable(symbols),
            ImplementsISpanFormattable = paramTypeSymbol.ImplementsISpanFormattable(symbols),
            HasCustomToString = paramTypeSymbol.HasCustomToString(),
        };

        parsingState.FoundLogger |= lp.IsLogger;
        parsingState.FoundException |= lp.IsException;
        parsingState.FoundLogLevel |= lp.IsLogLevel;

        return lp;
    }

    private Location? GetLoggerMessageAttribute(MethodDeclarationSyntax methodSyntax, SemanticModel sm, SymbolHolder symbols)
    {
        foreach (var mal in methodSyntax.AttributeLists)
        {
            foreach (var methodAttr in mal.Attributes)
            {
                var attrCtor = sm.GetSymbolInfo(methodAttr, _cancellationToken).Symbol;
                if (attrCtor != null && SymbolEqualityComparer.Default.Equals(attrCtor.ContainingType, symbols.LoggerMessageAttribute))
                {
                    return methodAttr.GetLocation();
                }
            }
        }

        return null;
    }

    private (string? member, ISymbol? secondMember, bool isNullable) FindLoggerMember(SemanticModel sm, TypeDeclarationSyntax classDec, ITypeSymbol loggerSymbol)
    {
        string? loggerMember = null;
        bool isNullable = false;

        INamedTypeSymbol? classType = sm.GetDeclaredSymbol(classDec, _cancellationToken);
        INamedTypeSymbol? currentClassType = classType;
        bool onMostDerivedType = true;

#pragma warning disable S125 // Sections of code should not be commented out
        /*
         We keep track of the names of all non-logger fields, since they prevent referring to logger
         primary constructor parameters with the same name. Example:
         partial class C(ILogger logger)
         {
             private readonly object logger = logger;
        
             [LoggerMessage(EventId = 0, Level = LogLevel.Debug, Message = ""M1"")]
             public partial void M1(); // The ILogger primary constructor parameter cannot be used here.
         }
        */
#pragma warning restore S125 // Sections of code should not be commented out

        HashSet<string> shadowedNames = new(StringComparer.Ordinal);

        while (currentClassType is { SpecialType: not SpecialType.System_Object })
        {
            foreach (ISymbol ms in currentClassType.GetMembers())
            {
                // we support both fields and properties
                ITypeSymbol? typeSymbol = ms switch
                {
                    IFieldSymbol fs => fs.Type,
                    IPropertySymbol ps => ps.Type,
                    _ => default,
                };

                if (typeSymbol is null)
                {
                    continue;
                }

                if (!onMostDerivedType && ms.DeclaredAccessibility == Accessibility.Private)
                {
                    continue;
                }

                if (!ms.CanBeReferencedByName)
                {
                    continue;
                }

                if (ParserUtilities.IsBaseOrIdentity(typeSymbol, loggerSymbol, _compilation))
                {
                    if (loggerMember is null)
                    {
                        loggerMember = ms.Name;
                        isNullable = typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
                    }
                    else if (!onMostDerivedType)
                    {
                        // We found a public logger member on a base class when a logger member was already found in the derived class.
                        // This prefers the existing one because it's more likely to be the one the user intended.
                        // The .NET runtime logging generator doesn't support ILogger properties, so this matches that behavior.
                        continue;
                    }
                    else
                    {
                        // This catches the case where there are multiple logger members on the same class.
                        return (member: null, secondMember: ms, isNullable);
                    }
                }
                else
                {
                    _ = shadowedNames.Add(ms.Name);
                }
            }

            onMostDerivedType = false;
            currentClassType = currentClassType.BaseType;
        }

        // We prioritize fields over primary constructor parameters and avoid warnings if both exist.
        if (loggerMember is not null)
        {
            return (member: loggerMember, secondMember: null, isNullable);
        }

        IEnumerable<IMethodSymbol> primaryConstructors = classType!.InstanceConstructors
            .Where(ic => ic.DeclaringSyntaxReferences
                .Any(ds => ds.GetSyntax() is ClassDeclarationSyntax));

        foreach (IMethodSymbol primaryConstructor in primaryConstructors)
        {
            foreach (IParameterSymbol parameter in primaryConstructor.Parameters)
            {
                if (ParserUtilities.IsBaseOrIdentity(parameter.Type, loggerSymbol, _compilation))
                {
                    if (shadowedNames.Contains(parameter.Name))
                    {
                        // Accessible fields always shadow primary constructor parameters,
                        // so we can't use the primary constructor parameter,
                        // even if the field is not a valid logger.
                        Diag(DiagDescriptors.PrimaryConstructorParameterLoggerHidden, parameter.GetLocation(), classDec.Identifier.Text);

                        continue;
                    }

                    if (loggerMember is null)
                    {
                        loggerMember = parameter.Name;
                        isNullable = parameter.Type.NullableAnnotation == NullableAnnotation.Annotated;
                    }
                    else
                    {
                        return (member: null, secondMember: parameter, isNullable);
                    }
                }
            }
        }

        return (member: loggerMember, secondMember: null, isNullable);
    }

    private void Diag(DiagnosticDescriptor desc, Location? location, params object?[]? messageArgs)
    {
        var d = Diagnostic.Create(desc, location, messageArgs);
        _reportDiagnostic(d);

        if (d.Severity == DiagnosticSeverity.Error)
        {
            _failedMethod = true;
        }
    }

    private record struct MethodParsingState(
        bool FoundLogger,
        bool FoundException,
        bool FoundLogLevel);
}
