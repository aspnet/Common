// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.VectorData.ProviderServices;

/// <summary>
/// Represents a property on a vector store record.
/// This is an internal support type meant for use by providers only and not by applications.
/// </summary>
[Experimental(DiagnosticIds.Experiments.VectorDataProviderServices, UrlFormat = DiagnosticIds.UrlFormat)]
public abstract class PropertyModel(string modelName, Type type)
{
    private Func<object, object?>? _getter;
    private Action<object, object?>? _setter;

    /// <summary>
    /// Gets or sets the model name of the property.
    /// </summary>
    /// <remarks>
    /// If the property corresponds to a .NET property, this name is the name of that property.
    /// </remarks>
    public string ModelName { get; set; } = modelName;

    /// <summary>
    /// Gets or sets the storage name of the property.
    /// </summary>
    /// <remarks>
    /// This is the name to which the property is mapped in the vector store.
    /// </remarks>
    public string StorageName
    {
        get => field ?? ModelName;
        set;
    }

    /// <summary>
    /// Gets or sets the CLR type of the property.
    /// </summary>
    public Type Type { get; set; } = type;

    /// <summary>
    /// Gets or sets the reflection <see cref="PropertyInfo"/> for the .NET property.
    /// </summary>
    /// <value>
    /// The reflection <see cref="PropertyInfo"/> for the .NET property.
    /// <see langword="null"/> when using dynamic mapping.
    /// </value>
    public PropertyInfo? PropertyInfo { get; set; }

    /// <summary>
    /// Gets or sets a dictionary of provider-specific annotations for this property.
    /// </summary>
    /// <remarks>
    /// This allows setting database-specific configuration options that aren't universal across all vector stores.
    /// </remarks>
    public Dictionary<string, object?>? ProviderAnnotations { get; set; }

    /// <summary>
    /// Gets a value indicating whether the property type is nullable.
    /// </summary>
    /// <remarks>
    /// For value types, this is <see langword="true"/> when the type is <see cref="Nullable{T}"/>.
    /// For reference types on .NET 6+, this uses NRT annotations via <c>NullabilityInfoContext</c>
    /// when a <see cref="PropertyInfo"/> is available (that is, during POCO mapping);
    /// otherwise, reference types are assumed nullable.
    /// </remarks>
    public bool IsNullable
    {
        get
        {
            // Value types: nullable only if Nullable<T>
            if (Type.IsValueType)
            {
                return Nullable.GetUnderlyingType(Type) is not null;
            }

            // Reference types: check NRT annotation via NullabilityInfoContext when available
#if NET
            if (PropertyInfo is { } propertyInfo)
            {
                var nullabilityInfo = new NullabilityInfoContext().Create(propertyInfo);
                return nullabilityInfo.ReadState != NullabilityState.NotNull;
            }
#endif

            // Dynamic mapping or old framework: assume nullable for reference types
            return true;
        }
    }

    /// <summary>
    /// Configures the property accessors using a CLR <see cref="System.Reflection.PropertyInfo"/> for POCO mapping.
    /// </summary>
    // TODO: Implement compiled delegates for better performance, https://github.com/microsoft/semantic-kernel/issues/11122
    // TODO: Implement source-generated accessors for NativeAOT, https://github.com/microsoft/semantic-kernel/issues/10256
    internal void ConfigurePocoAccessors(PropertyInfo propertyInfo)
    {
        PropertyInfo = propertyInfo;
        _getter = propertyInfo.GetValue;
        _setter = (record, value) =>
        {
            // If the value is null, no need to set the property (it's the CLR default)
            if (value is not null)
            {
                propertyInfo.SetValue(record, value);
            }
        };
    }

    /// <summary>
    /// Configures the property accessors for dynamic mapping using <see cref="Dictionary{TKey, TValue}"/>.
    /// </summary>
    internal void ConfigureDynamicAccessors()
    {
        var propertyType = Type;

        _getter = record =>
        {
            var dictionary = (Dictionary<string, object?>)record;
            _ = dictionary.TryGetValue(ModelName, out var value);

            if (value is not null && value.GetType() != (Nullable.GetUnderlyingType(propertyType) ?? propertyType))
            {
                throw new InvalidCastException($"Property '{ModelName}' has a value of type '{value.GetType().Name}', but its configured type is '{propertyType.Name}'.");
            }

            return value;
        };

        _setter = (record, value) => ((Dictionary<string, object?>)record)[ModelName] = value;
    }

    /// <summary>
    /// Reads the property from the given <paramref name="record"/>, returning the value as an <see cref="object"/>.
    /// </summary>
    /// <returns>The property value.</returns>
    public object? GetValueAsObject(object record)
    {
        Debug.Assert(_getter is not null, "Property accessors have not been configured.");
        return _getter!(record);
    }

    /// <summary>
    /// Writes the property from the given <paramref name="record"/>, accepting the value to write as an <see cref="object"/>.
    /// </summary>
    public void SetValueAsObject(object record, object? value)
    {
        Debug.Assert(_setter is not null, "Property accessors have not been configured.");
        _setter!(record, value);
    }

    /// <summary>
    /// Reads the property from the given <paramref name="record"/>.
    /// </summary>
    /// <typeparam name="T">The type of the property value.</typeparam>
    /// <returns>The property value.</returns>
    // TODO: actually implement the generic accessors to avoid boxing, and make use of them in providers
    public T GetValue<T>(object record)
        => (T)GetValueAsObject(record)!;

    /// <summary>
    /// Writes the property from the given <paramref name="record"/>.
    /// </summary>
    /// <typeparam name="T">The type of the property value.</typeparam>
    // TODO: actually implement the generic accessors to avoid boxing, and make use of them in providers
    public void SetValue<T>(object record, T value)
        => SetValueAsObject(record, value);
}
