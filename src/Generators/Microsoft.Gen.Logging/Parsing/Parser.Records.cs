// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.Gen.Shared;

namespace Microsoft.Gen.Logging.Parsing
{
    internal partial class Parser
    {
        internal bool RecordHasSensitivePublicMembers(ITypeSymbol type, SymbolHolder symbols)
        {
            if (symbols.DataClassificationAttribute is null)
            {
                return false;
            }

            // Check full type hierarchy for sensitive members, including base types and transitive fields/properties
            var typesChain = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            _ = typesChain.Add(type); // Add itself
            return RecordHasSensitivePublicMembers(type, typesChain, symbols, _cancellationToken);
        }

        private readonly record struct MemberInfo(
            string Name,
            ITypeSymbol Type,
            IPropertySymbol? Property);

        private static bool RecordHasSensitivePublicMembers(ITypeSymbol type, HashSet<ITypeSymbol> typesChain, SymbolHolder symbols, CancellationToken token)
        {
            var overriddenMembers = new HashSet<string>();
            var namedType = type as INamedTypeSymbol;
            while (namedType != null && namedType.SpecialType != SpecialType.System_Object)
            {
                token.ThrowIfCancellationRequested();

                // Check if the type itself has any data classification attributes applied:
                var recordDataClassAttributes = GetDataClassificationAttributes(namedType, symbols);
                if (recordDataClassAttributes.Count > 0)
                {
                    return true;
                }

                var members = namedType.GetMembers();
                if (members.IsDefaultOrEmpty)
                {
                    namedType = namedType.BaseType;
                    continue;
                }

                foreach (var m in members)
                {
                    // Skip static, non-public members:
                    if (m.DeclaredAccessibility != Accessibility.Public ||
                        m.IsStatic)
                    {
                        continue;
                    }

                    MemberInfo? maybeMemberInfo =
                        m switch
                        {
                            IPropertySymbol property => new(property.Name, property.Type, property),
                            IFieldSymbol field => new(field.Name, field.Type, Property: null),
                            _ => null
                        };

                    if (maybeMemberInfo is null)
                    {
                        if (m is not IMethodSymbol { MethodKind: MethodKind.Constructor } ctorMethod)
                        {
                            continue;
                        }

                        // Check if constructor has any sensitive parameters:
                        foreach (var param in ctorMethod.Parameters)
                        {
                            // We don't check for "DataClassifierWrappers" here as they will be covered in field/property checks:
                            var parameterDataClassAttributes = GetDataClassificationAttributes(param, symbols);
                            if (parameterDataClassAttributes.Count > 0)
                            {
                                return true;
                            }
                        }

                        // No sensitive parameters in the ctor, continue with next member:
                        continue;
                    }

                    var memberInfo = maybeMemberInfo.Value;
                    if (memberInfo.Property is not null)
                    {
                        var property = memberInfo.Property;
                        var getMethod = property.GetMethod;

                        // We don't check if getter is "public", it doesn't matter: https://github.com/dotnet/runtime/issues/86700
                        if (getMethod is null)
                        {
                            continue;
                        }

                        // Property is already overridden in derived class, skip it:
                        if ((property.IsVirtual || property.IsOverride) &&
                            overriddenMembers.Contains(property.Name))
                        {
                            continue;
                        }

                        // Adding property that overrides some base class virtual property:
                        if (property.IsOverride)
                        {
                            _ = overriddenMembers.Add(property.Name);
                        }
                    }

                    // Skip members with delegate types:
                    var memberType = memberInfo.Type;
                    if (memberType.TypeKind == TypeKind.Delegate)
                    {
                        continue;
                    }

                    var extractedType = memberType;
                    if (memberType.IsNullableOfT())
                    {
                        // extract the T from a Nullable<T>
                        extractedType = ((INamedTypeSymbol)memberType).TypeArguments[0];
                    }

                    var propertyDataClassAttributes = GetDataClassificationAttributes(m, symbols);
                    if (propertyDataClassAttributes.Count > 0)
                    {
                        return true;
                    }

                    // Checking "extractedType" here:
                    if (typesChain.Contains(extractedType))
                    {
                        continue; // Move to the next member
                    }

                    if (extractedType.IsRecord && // Going inside record types only
                        extractedType.Kind == SymbolKind.NamedType &&
                        !extractedType.IsSpecialType(symbols))
                    {
                        // Too deep to finish inspecting: assume the record is sensitive rather than let unchecked data be logged.
                        if (typesChain.Count >= MaxTypeTraversalDepth)
                        {
                            return true;
                        }

                        _ = typesChain.Add(extractedType);
                        if (RecordHasSensitivePublicMembers(extractedType, typesChain, symbols, token))
                        {
                            return true;
                        }

                        _ = typesChain.Remove(extractedType);
                    }
                }

                // This is to support inheritance, i.e. to fetch public members of base class(-es) as well:
                namedType = namedType.BaseType;
            }

            return false;
        }
    }
}
