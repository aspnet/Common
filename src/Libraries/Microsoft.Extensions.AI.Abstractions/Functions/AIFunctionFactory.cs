// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
#if !NET
using System.Linq;
#endif
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Collections;
using Microsoft.Shared.Diagnostics;

#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields

namespace Microsoft.Extensions.AI;

/// <summary>Provides factory methods for creating commonly-used implementations of <see cref="AIFunction"/>.</summary>
/// <remarks>
/// <para>
/// The <see cref="AIFunctionFactory"/> class creates <see cref="AIFunction"/> instances that wrap .NET methods
/// (specified as <see cref="Delegate"/> or <see cref="MethodInfo"/>). As part of this process, JSON schemas are
/// automatically derived for both the function's input parameters (exposed via <see cref="AIFunctionDeclaration.JsonSchema"/>)
/// and, by default, the function's return type (exposed via <see cref="AIFunctionDeclaration.ReturnJsonSchema"/>).
/// These schemas are produced using the <see cref="AIFunctionFactoryOptions.SerializerOptions"/> and
/// <see cref="AIFunctionFactoryOptions.JsonSchemaCreateOptions"/>, and enable AI services to understand and
/// interact with the function. Return value serialization and schema derivation behavior can be customized
/// via <see cref="AIFunctionFactoryOptions.MarshalResult"/> and <see cref="AIFunctionFactoryOptions.ExcludeResultSchema"/>,
/// respectively.
/// </para>
/// </remarks>
/// <related type="Article" href="https://learn.microsoft.com/dotnet/ai/quickstarts/use-function-calling">Invoke .NET functions using an AI model.</related>
public static partial class AIFunctionFactory
{
    // NOTE:
    // Unlike most library code, AIFunctionFactory uses ConfigureAwait(true) rather than ConfigureAwait(false). This is to
    // enable AIFunctionFactory to be used with methods that might be context-aware, such as those employing a UI framework.

    /// <summary>Holds the default options instance used when creating function.</summary>
    private static readonly AIFunctionFactoryOptions _defaultOptions = new();

    /// <summary>Creates an <see cref="AIFunction"/> instance for a method, specified via a delegate.</summary>
    /// <param name="method">The method to be represented via the created <see cref="AIFunction"/>.</param>
    /// <param name="options">Metadata to use to override defaults inferred from <paramref name="method"/>.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <remarks>
    /// <para>
    /// By default, any parameters to <paramref name="method"/> are sourced from the <see cref="AIFunctionArguments"/>'s dictionary
    /// of key/value pairs and are represented in the JSON schema for the function, as exposed in the returned <see cref="AIFunction"/>'s
    /// <see cref="AIFunctionDeclaration.JsonSchema"/>. There are a few exceptions to this:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="CancellationToken"/> parameters are automatically bound to the <see cref="CancellationToken"/> passed into
    ///     the invocation via <see cref="AIFunction.InvokeAsync"/>'s <see cref="CancellationToken"/> parameter. The parameter is
    ///     not included in the generated JSON schema. The behavior of <see cref="CancellationToken"/> parameters can't be overridden.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="IServiceProvider"/> parameters are bound from the <see cref="AIFunctionArguments.Services"/> property
    ///     and are not included in the JSON schema. If the parameter is optional, such that a default value is provided,
    ///     <see cref="AIFunctionArguments.Services"/> is allowed to be <see langword="null"/>; otherwise, <see cref="AIFunctionArguments.Services"/>
    ///     must be non-<see langword="null"/>, or else the invocation will fail with an exception due to the required nature of the parameter.
    ///     The handling of <see cref="IServiceProvider"/> parameters can be overridden via <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/>.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="AIFunctionArguments"/> parameters are bound directly to <see cref="AIFunctionArguments"/> instance
    ///     passed into <see cref="AIFunction.InvokeAsync"/> and are not included in the JSON schema. If the <see cref="AIFunctionArguments"/>
    ///     instance passed to <see cref="AIFunction.InvokeAsync"/> is <see langword="null"/>, the <see cref="AIFunction"/> implementation
    ///     manufactures an empty instance, such that parameters of type <see cref="AIFunctionArguments"/> can always be satisfied, whether
    ///     optional or not. The handling of <see cref="AIFunctionArguments"/> parameters can be overridden via
    ///     <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/>.
    ///   </item>
    /// </list>
    /// All other parameter types are, by default, bound from the <see cref="AIFunctionArguments"/> dictionary passed into <see cref="AIFunction.InvokeAsync"/>
    /// and are included in the generated JSON schema. This can be overridden by the <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/> provided
    /// via the <paramref name="options"/> parameter; for every parameter, the delegate is enabled to choose if the parameter should be included in the
    /// generated schema and how its value should be bound, including handling of optionality (by default, required parameters that are not included in the
    /// <see cref="AIFunctionArguments"/> dictionary will result in an exception being thrown). Loosely-typed additional context information can be passed
    /// into <see cref="AIFunction.InvokeAsync"/> via the <see cref="AIFunctionArguments"/>'s <see cref="AIFunctionArguments.Context"/> dictionary; the default
    /// binding ignores this collection, but a custom binding supplied via <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/> can choose to
    /// source arguments from this data.
    /// </para>
    /// <para>
    /// The default marshaling of parameters from the <see cref="AIFunctionArguments"/> dictionary permits values to be passed into the <paramref name="method"/>'s
    /// invocation directly if the object is already of a compatible type. Otherwise, if the argument is a <see cref="JsonElement"/>, <see cref="JsonDocument"/>,
    /// or <see cref="JsonNode"/>, it is deserialized into the parameter type, utilizing <see cref="AIFunctionFactoryOptions.SerializerOptions"/> if provided,
    /// or else using <see cref="AIJsonUtilities.DefaultOptions"/>. If the argument is anything else, it is round-tripped through JSON, serializing the object as JSON
    /// and then deserializing it to the expected type.
    /// </para>
    /// <para>
    /// In general, the data supplied via an <see cref="AIFunctionArguments"/>'s dictionary is supplied from an AI service and should be considered
    /// unvalidated and untrusted. To provide validated and trusted data to the invocation of <paramref name="method"/>, consider having <paramref name="method"/>
    /// point to an instance method on an instance configured to hold the appropriate state. An <see cref="IServiceProvider"/> parameter can also be
    /// used to resolve services from a dependency injection container.
    /// </para>
    /// <para>
    /// By default, return values are serialized to <see cref="JsonElement"/> using <paramref name="options"/>'s
    /// <see cref="AIFunctionFactoryOptions.SerializerOptions"/> if provided, or else using <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// However, return values whose declared type is <see cref="AIContent"/>, a derived type of <see cref="AIContent"/>, or
    /// any type assignable from <see cref="IEnumerable{AIContent}"/> (e.g. <c>AIContent[]</c>, <c>List&lt;AIContent&gt;</c>) are
    /// special-cased and are not serialized: the created function returns the original instance(s) directly to enable
    /// callers (such as an <c>IChatClient</c>) to perform type tests and implement specialized handling. If
    /// <see cref="AIFunctionFactoryOptions.MarshalResult"/> is supplied, that delegate governs the behavior instead.
    /// </para>
    /// <para>
    /// In addition to the parameter schema, a JSON schema is also derived from the method's return type and exposed via the
    /// returned <see cref="AIFunction"/>'s <see cref="AIFunctionDeclaration.ReturnJsonSchema"/>. For methods returning
    /// <see cref="void"/>, <see cref="Task"/>, or <see cref="ValueTask"/>, no return schema is produced (the property is <see langword="null"/>).
    /// For methods returning <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>, the schema is derived from the
    /// unwrapped result type. Return schema generation can be excluded via <see cref="AIFunctionFactoryOptions.ExcludeResultSchema"/>,
    /// and its generation is governed by <paramref name="options"/>'s <see cref="AIFunctionFactoryOptions.JsonSchemaCreateOptions"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">A parameter to <paramref name="method"/> is not serializable.</exception>
    public static AIFunction Create(Delegate method, AIFunctionFactoryOptions? options)
    {
        _ = Throw.IfNull(method);

        return ReflectionAIFunction.Build(method.Method, method.Target, options ?? _defaultOptions);
    }

    /// <summary>Creates an <see cref="AIFunction"/> instance for a method, specified via a delegate.</summary>
    /// <param name="method">The method to be represented via the created <see cref="AIFunction"/>.</param>
    /// <param name="name">
    /// The name to use for the <see cref="AIFunction"/>. If <see langword="null"/>, the name will be derived from
    /// any <see cref="AIFunctionNameAttribute"/> on <paramref name="method"/>, then from any <see cref="DisplayNameAttribute"/>
    /// on <paramref name="method"/>, if available, or else from the name of <paramref name="method"/>.
    /// </param>
    /// <param name="description">
    /// The description to use for the <see cref="AIFunction"/>. If <see langword="null"/>, a description will be derived from
    /// any <see cref="DescriptionAttribute"/> on <paramref name="method"/>, if available.
    /// </param>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> used to marshal function parameters and any return value.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <remarks>
    /// <para>
    /// Any parameters to <paramref name="method"/> are sourced from the <see cref="AIFunctionArguments"/>'s dictionary
    /// of key/value pairs and are represented in the JSON schema for the function, as exposed in the returned <see cref="AIFunction"/>'s
    /// <see cref="AIFunctionDeclaration.JsonSchema"/>. There are a few exceptions to this:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="CancellationToken"/> parameters are automatically bound to the <see cref="CancellationToken"/> passed into
    ///     the invocation via <see cref="AIFunction.InvokeAsync"/>'s <see cref="CancellationToken"/> parameter. The parameter is
    ///     not included in the generated JSON schema.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="IServiceProvider"/> parameters are bound from the <see cref="AIFunctionArguments.Services"/> property
    ///     and are not included in the JSON schema. If the parameter is optional, such that a default value is provided,
    ///     <see cref="AIFunctionArguments.Services"/> is allowed to be <see langword="null"/>; otherwise, <see cref="AIFunctionArguments.Services"/>
    ///     must be non-<see langword="null"/>, or else the invocation will fail with an exception due to the required nature of the parameter.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="AIFunctionArguments"/> parameters are bound directly to <see cref="AIFunctionArguments"/> instance
    ///     passed into <see cref="AIFunction.InvokeAsync"/> and are not included in the JSON schema. If the <see cref="AIFunctionArguments"/>
    ///     instance passed to <see cref="AIFunction.InvokeAsync"/> is <see langword="null"/>, the <see cref="AIFunction"/> implementation
    ///     manufactures an empty instance, such that parameters of type <see cref="AIFunctionArguments"/> can always be satisfied, whether
    ///     optional or not.
    ///   </item>
    /// </list>
    /// All other parameter types are bound from the <see cref="AIFunctionArguments"/> dictionary passed into <see cref="AIFunction.InvokeAsync"/>
    /// and are included in the generated JSON schema.
    /// </para>
    /// <para>
    /// The marshaling of parameters from the <see cref="AIFunctionArguments"/> dictionary permits values to be passed into the <paramref name="method"/>'s
    /// invocation directly if the object is already of a compatible type. Otherwise, if the argument is a <see cref="JsonElement"/>, <see cref="JsonDocument"/>,
    /// or <see cref="JsonNode"/>, it is deserialized into the parameter type, utilizing <paramref name="serializerOptions"/> if provided, or else
    /// <see cref="AIJsonUtilities.DefaultOptions"/>. If the argument is anything else, it is round-tripped through JSON, serializing the object as JSON
    /// and then deserializing it to the expected type.
    /// </para>
    /// <para>
    /// In general, the data supplied via an <see cref="AIFunctionArguments"/>'s dictionary is supplied from an AI service and should be considered
    /// unvalidated and untrusted. To provide validated and trusted data to the invocation of <paramref name="method"/>, consider having <paramref name="method"/>
    /// point to an instance method on an instance configured to hold the appropriate state. An <see cref="IServiceProvider"/> parameter can also be
    /// used to resolve services from a dependency injection container.
    /// </para>
    /// <para>
    /// Return values are serialized to <see cref="JsonElement"/> using <paramref name="serializerOptions"/> if provided,
    /// or else using <see cref="AIJsonUtilities.DefaultOptions"/>. However, return values whose declared type is <see cref="AIContent"/>, a
    /// derived type of <see cref="AIContent"/>, or any type assignable from <see cref="IEnumerable{AIContent}"/> are not serialized;
    /// they are returned as-is to facilitate specialized handling.
    /// </para>
    /// <para>
    /// A JSON schema is also derived from the method's return type and exposed via <see cref="AIFunctionDeclaration.ReturnJsonSchema"/>.
    /// For methods returning <see cref="void"/>, <see cref="Task"/>, or <see cref="ValueTask"/>, no return schema is produced.
    /// For methods returning <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>, the schema is derived from the unwrapped result type.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">A parameter to <paramref name="method"/> is not serializable.</exception>
    public static AIFunction Create(Delegate method, string? name = null, string? description = null, JsonSerializerOptions? serializerOptions = null)
    {
        _ = Throw.IfNull(method);

        AIFunctionFactoryOptions createOptions = serializerOptions is null && name is null && description is null
            ? _defaultOptions
            : new()
            {
                Name = name,
                Description = description,
                SerializerOptions = serializerOptions,
            };

        return ReflectionAIFunction.Build(method.Method, method.Target, createOptions);
    }

    /// <summary>
    /// Creates an <see cref="AIFunction"/> instance for a method, specified via an <see cref="MethodInfo"/> instance
    /// and an optional target object if the method is an instance method.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="AIFunction"/>.</param>
    /// <param name="target">
    /// The target object for the <paramref name="method"/> if it represents an instance method.
    /// This should be <see langword="null"/> if and only if <paramref name="method"/> is a static method.
    /// </param>
    /// <param name="options">Metadata to use to override defaults inferred from <paramref name="method"/>.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <remarks>
    /// <para>
    /// By default, any parameters to <paramref name="method"/> are sourced from the <see cref="AIFunctionArguments"/>'s dictionary
    /// of key/value pairs and are represented in the JSON schema for the function, as exposed in the returned <see cref="AIFunction"/>'s
    /// <see cref="AIFunctionDeclaration.JsonSchema"/>. There are a few exceptions to this:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="CancellationToken"/> parameters are automatically bound to the <see cref="CancellationToken"/> passed into
    ///     the invocation via <see cref="AIFunction.InvokeAsync"/>'s <see cref="CancellationToken"/> parameter. The parameter is
    ///     not included in the generated JSON schema. The behavior of <see cref="CancellationToken"/> parameters can't be overridden.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="IServiceProvider"/> parameters are bound from the <see cref="AIFunctionArguments.Services"/> property
    ///     and are not included in the JSON schema. If the parameter is optional, such that a default value is provided,
    ///     <see cref="AIFunctionArguments.Services"/> is allowed to be <see langword="null"/>; otherwise, <see cref="AIFunctionArguments.Services"/>
    ///     must be non-<see langword="null"/>, or else the invocation will fail with an exception due to the required nature of the parameter.
    ///     The handling of <see cref="IServiceProvider"/> parameters can be overridden via <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/>.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="AIFunctionArguments"/> parameters are bound directly to <see cref="AIFunctionArguments"/> instance
    ///     passed into <see cref="AIFunction.InvokeAsync"/> and are not included in the JSON schema. If the <see cref="AIFunctionArguments"/>
    ///     instance passed to <see cref="AIFunction.InvokeAsync"/> is <see langword="null"/>, the <see cref="AIFunction"/> implementation
    ///     manufactures an empty instance, such that parameters of type <see cref="AIFunctionArguments"/> can always be satisfied, whether
    ///     optional or not. The handling of <see cref="AIFunctionArguments"/> parameters can be overridden via
    ///       <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/>.
    ///   </item>
    /// </list>
    /// All other parameter types are, by default, bound from the <see cref="AIFunctionArguments"/> dictionary passed into <see cref="AIFunction.InvokeAsync"/>
    /// and are included in the generated JSON schema. This can be overridden by the <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/> provided
    /// via the <paramref name="options"/> parameter; for every parameter, the delegate is enabled to choose if the parameter should be included in the
    /// generated schema and how its value should be bound, including handling of optionality (by default, required parameters that are not included in the
    /// <see cref="AIFunctionArguments"/> dictionary will result in an exception being thrown). Loosely typed additional context information can be passed
    /// into <see cref="AIFunction.InvokeAsync"/> via the <see cref="AIFunctionArguments"/>'s <see cref="AIFunctionArguments.Context"/> dictionary; the default
    /// binding ignores this collection, but a custom binding supplied via <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/> can choose to
    /// source arguments from this data.
    /// </para>
    /// <para>
    /// The default marshaling of parameters from the <see cref="AIFunctionArguments"/> dictionary permits values to be passed into the <paramref name="method"/>'s
    /// invocation directly if the object is already of a compatible type. Otherwise, if the argument is a <see cref="JsonElement"/>, <see cref="JsonDocument"/>,
    /// or <see cref="JsonNode"/>, it is deserialized into the parameter type, utilizing <see cref="AIFunctionFactoryOptions.SerializerOptions"/> if provided,
    /// or else using <see cref="AIJsonUtilities.DefaultOptions"/>. If the argument is anything else, it is round-tripped through JSON, serializing the object as JSON
    /// and then deserializing it to the expected type.
    /// </para>
    /// <para>
    /// In general, the data supplied via an <see cref="AIFunctionArguments"/>'s dictionary is supplied from an AI service and should be considered
    /// unvalidated and untrusted. To provide validated and trusted data to the invocation of <paramref name="method"/>, consider having <paramref name="method"/>
    /// point to an instance method on an instance configured to hold the appropriate state. An <see cref="IServiceProvider"/> parameter can also be
    /// used to resolve services from a dependency injection container.
    /// </para>
    /// <para>
    /// By default, return values are serialized to <see cref="JsonElement"/> using <paramref name="options"/>'s
    /// <see cref="AIFunctionFactoryOptions.SerializerOptions"/> if provided, or else using <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// However, return values whose declared type is <see cref="AIContent"/>, a derived type of <see cref="AIContent"/>, or
    /// any type assignable from <see cref="IEnumerable{AIContent}"/> are not serialized and are instead returned directly.
    /// Handling of return values can be overridden via <see cref="AIFunctionFactoryOptions.MarshalResult"/>.
    /// </para>
    /// <para>
    /// In addition to the parameter schema, a JSON schema is also derived from the method's return type and exposed via the
    /// returned <see cref="AIFunction"/>'s <see cref="AIFunctionDeclaration.ReturnJsonSchema"/>. For methods returning
    /// <see cref="void"/>, <see cref="Task"/>, or <see cref="ValueTask"/>, no return schema is produced (the property is <see langword="null"/>).
    /// For methods returning <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>, the schema is derived from the
    /// unwrapped result type. Return schema generation can be excluded via <see cref="AIFunctionFactoryOptions.ExcludeResultSchema"/>,
    /// and its generation is governed by <paramref name="options"/>'s <see cref="AIFunctionFactoryOptions.JsonSchemaCreateOptions"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> represents an instance method but <paramref name="target"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> represents an open generic method.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> contains a parameter without a parameter name.</exception>
    /// <exception cref="JsonException">A parameter to <paramref name="method"/> or its return type is not serializable.</exception>
    public static AIFunction Create(MethodInfo method, object? target, AIFunctionFactoryOptions? options)
    {
        _ = Throw.IfNull(method);

        return ReflectionAIFunction.Build(method, target, options ?? _defaultOptions);
    }

    /// <summary>
    /// Creates an <see cref="AIFunction"/> instance for a method, specified via an <see cref="MethodInfo"/> instance
    /// and an optional target object if the method is an instance method.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="AIFunction"/>.</param>
    /// <param name="target">
    /// The target object for the <paramref name="method"/> if it represents an instance method.
    /// This should be <see langword="null"/> if and only if <paramref name="method"/> is a static method.
    /// </param>
    /// <param name="name">
    /// The name to use for the <see cref="AIFunction"/>. If <see langword="null"/>, the name will be derived from
    /// any <see cref="AIFunctionNameAttribute"/> on <paramref name="method"/>, then from any <see cref="DisplayNameAttribute"/>
    /// on <paramref name="method"/>, if available, or else from the name of <paramref name="method"/>.
    /// </param>
    /// <param name="description">
    /// The description to use for the <see cref="AIFunction"/>. If <see langword="null"/>, a description will be derived from
    /// any <see cref="DescriptionAttribute"/> on <paramref name="method"/>, if available.
    /// </param>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> used to marshal function parameters and return value.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <remarks>
    /// <para>
    /// Any parameters to <paramref name="method"/> are sourced from the <see cref="AIFunctionArguments"/>'s dictionary
    /// of key/value pairs and are represented in the JSON schema for the function, as exposed in the returned <see cref="AIFunction"/>'s
    /// <see cref="AIFunctionDeclaration.JsonSchema"/>. There are a few exceptions to this:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="CancellationToken"/> parameters are automatically bound to the <see cref="CancellationToken"/> passed into
    ///     the invocation via <see cref="AIFunction.InvokeAsync"/>'s <see cref="CancellationToken"/> parameter. The parameter is
    ///     not included in the generated JSON schema.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="IServiceProvider"/> parameters are bound from the <see cref="AIFunctionArguments.Services"/> property
    ///     and are not included in the JSON schema. If the parameter is optional, such that a default value is provided,
    ///     <see cref="AIFunctionArguments.Services"/> is allowed to be <see langword="null"/>; otherwise, <see cref="AIFunctionArguments.Services"/>
    ///     must be non-<see langword="null"/>, or else the invocation will fail with an exception due to the required nature of the parameter.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="AIFunctionArguments"/> parameters are bound directly to <see cref="AIFunctionArguments"/> instance
    ///     passed into <see cref="AIFunction.InvokeAsync"/> and are not included in the JSON schema. If the <see cref="AIFunctionArguments"/>
    ///     instance passed to <see cref="AIFunction.InvokeAsync"/> is <see langword="null"/>, the <see cref="AIFunction"/> implementation
    ///     manufactures an empty instance, such that parameters of type <see cref="AIFunctionArguments"/> can always be satisfied, whether
    ///     optional or not.
    ///   </item>
    /// </list>
    /// All other parameter types are bound from the <see cref="AIFunctionArguments"/> dictionary passed into <see cref="AIFunction.InvokeAsync"/>
    /// and are included in the generated JSON schema.
    /// </para>
    /// <para>
    /// The marshaling of parameters from the <see cref="AIFunctionArguments"/> dictionary permits values to be passed into the <paramref name="method"/>'s
    /// invocation directly if the object is already of a compatible type. Otherwise, if the argument is a <see cref="JsonElement"/>, <see cref="JsonDocument"/>,
    /// or <see cref="JsonNode"/>, it is deserialized into the parameter type, utilizing <paramref name="serializerOptions"/> if provided, or else
    /// <see cref="AIJsonUtilities.DefaultOptions"/>. If the argument is anything else, it is round-tripped through JSON, serializing the object as JSON
    /// and then deserializing it to the expected type.
    /// </para>
    /// <para>
    /// In general, the data supplied via an <see cref="AIFunctionArguments"/>'s dictionary is supplied from an AI service and should be considered
    /// unvalidated and untrusted. To provide validated and trusted data to the invocation of <paramref name="method"/>, consider having <paramref name="method"/>
    /// point to an instance method on an instance configured to hold the appropriate state. An <see cref="IServiceProvider"/> parameter can also be
    /// used to resolve services from a dependency injection container.
    /// </para>
    /// <para>
    /// Return values are serialized to <see cref="JsonElement"/> using <paramref name="serializerOptions"/> if provided,
    /// or else using <see cref="AIJsonUtilities.DefaultOptions"/>. However, return values whose declared type is <see cref="AIContent"/>, a
    /// derived type of <see cref="AIContent"/>, or any type assignable from <see cref="IEnumerable{AIContent}"/> are returned
    /// without serialization to enable specialized handling.
    /// </para>
    /// <para>
    /// A JSON schema is also derived from the method's return type and exposed via <see cref="AIFunctionDeclaration.ReturnJsonSchema"/>.
    /// For methods returning <see cref="void"/>, <see cref="Task"/>, or <see cref="ValueTask"/>, no return schema is produced.
    /// For methods returning <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>, the schema is derived from the unwrapped result type.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> represents an instance method but <paramref name="target"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> represents an open generic method.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> contains a parameter without a parameter name.</exception>
    /// <exception cref="JsonException">A parameter to <paramref name="method"/> or its return type is not serializable.</exception>
    public static AIFunction Create(MethodInfo method, object? target, string? name = null, string? description = null, JsonSerializerOptions? serializerOptions = null)
    {
        _ = Throw.IfNull(method);

        AIFunctionFactoryOptions createOptions = serializerOptions is null && name is null && description is null
            ? _defaultOptions
            : new()
            {
                Name = name,
                Description = description,
                SerializerOptions = serializerOptions,
            };

        return ReflectionAIFunction.Build(method, target, createOptions);
    }

    /// <summary>
    /// Creates an <see cref="AIFunction"/> instance for a method, specified via a <see cref="MethodInfo"/> for
    /// an instance method and a <see cref="Func{AIFunctionArguments,Object}"/> for constructing an instance of
    /// the receiver object each time the <see cref="AIFunction"/> is invoked.
    /// </summary>
    /// <param name="method">The instance method to be represented via the created <see cref="AIFunction"/>.</param>
    /// <param name="createInstanceFunc">
    /// Callback used on each function invocation to create an instance of the type on which the instance method <paramref name="method"/>
    /// will be invoked. If the returned instance is <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>, it will be disposed of
    /// after <paramref name="method"/> completes its invocation.
    /// </param>
    /// <param name="options">Metadata to use to override defaults inferred from <paramref name="method"/>.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <remarks>
    /// <para>
    /// Return values are serialized to <see cref="JsonElement"/> using <paramref name="options"/>'s
    /// <see cref="AIFunctionFactoryOptions.SerializerOptions"/>. Arguments that are not already of the expected type are
    /// marshaled to the expected type via JSON and using <paramref name="options"/>'s
    /// <see cref="AIFunctionFactoryOptions.SerializerOptions"/>. If the argument is a <see cref="JsonElement"/>,
    /// <see cref="JsonDocument"/>, or <see cref="JsonNode"/>, it is deserialized directly. If the argument is anything else unknown,
    /// it is round-tripped through JSON, serializing the object as JSON and then deserializing it to the expected type.
    /// </para>
    /// <para>
    /// By default, any parameters to <paramref name="method"/> are sourced from the <see cref="AIFunctionArguments"/>'s dictionary
    /// of key/value pairs and are represented in the JSON schema for the function, as exposed in the returned <see cref="AIFunction"/>'s
    /// <see cref="AIFunctionDeclaration.JsonSchema"/>. There are a few exceptions to this:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="CancellationToken"/> parameters are automatically bound to the <see cref="CancellationToken"/> passed into
    ///     the invocation via <see cref="AIFunction.InvokeAsync"/>'s <see cref="CancellationToken"/> parameter. The parameter is
    ///     not included in the generated JSON schema. The behavior of <see cref="CancellationToken"/> parameters can't be overridden.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="IServiceProvider"/> parameters are bound from the <see cref="AIFunctionArguments.Services"/> property
    ///     and are not included in the JSON schema. If the parameter is optional, such that a default value is provided,
    ///     <see cref="AIFunctionArguments.Services"/> is allowed to be <see langword="null"/>; otherwise, <see cref="AIFunctionArguments.Services"/>
    ///     must be non-<see langword="null"/>, or else the invocation will fail with an exception due to the required nature of the parameter.
    ///     The handling of <see cref="IServiceProvider"/> parameters can be overridden via <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/>.
    ///   </item>
    ///   <item>
    ///     By default, <see cref="AIFunctionArguments"/> parameters are bound directly to <see cref="AIFunctionArguments"/> instance
    ///     passed into <see cref="AIFunction.InvokeAsync"/> and are not included in the JSON schema. If the <see cref="AIFunctionArguments"/>
    ///     instance passed to <see cref="AIFunction.InvokeAsync"/> is <see langword="null"/>, the <see cref="AIFunction"/> implementation
    ///     manufactures an empty instance, such that parameters of type <see cref="AIFunctionArguments"/> can always be satisfied, whether
    ///     optional or not. The handling of <see cref="AIFunctionArguments"/> parameters can be overridden via
    ///     <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/>.
    ///   </item>
    /// </list>
    /// All other parameter types are, by default, bound from the <see cref="AIFunctionArguments"/> dictionary passed into <see cref="AIFunction.InvokeAsync"/>
    /// and are included in the generated JSON schema. This can be overridden by the <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/> provided
    /// via the <paramref name="options"/> parameter; for every parameter, the delegate is enabled to choose if the parameter should be included in the
    /// generated schema and how its value should be bound, including handling of optionality (by default, required parameters that are not included in the
    /// <see cref="AIFunctionArguments"/> dictionary will result in an exception being thrown). Loosely-typed additional context information can be passed
    /// into <see cref="AIFunction.InvokeAsync"/> via the <see cref="AIFunctionArguments"/>'s <see cref="AIFunctionArguments.Context"/> dictionary; the default
    /// binding ignores this collection, but a custom binding supplied via <see cref="AIFunctionFactoryOptions.ConfigureParameterBinding"/> can choose to
    /// source arguments from this data.
    /// </para>
    /// <para>
    /// The default marshaling of parameters from the <see cref="AIFunctionArguments"/> dictionary permits values to be passed into the <paramref name="method"/>'s
    /// invocation directly if the object is already of a compatible type. Otherwise, if the argument is a <see cref="JsonElement"/>, <see cref="JsonDocument"/>,
    /// or <see cref="JsonNode"/>, it is deserialized into the parameter type, utilizing <see cref="AIFunctionFactoryOptions.SerializerOptions"/> if provided,
    /// or else using <see cref="AIJsonUtilities.DefaultOptions"/>. If the argument is anything else, it is round-tripped through JSON, serializing the object as JSON
    /// and then deserializing it to the expected type.
    /// </para>
    /// <para>
    /// In general, the data supplied via an <see cref="AIFunctionArguments"/>'s dictionary is supplied from an AI service and should be considered
    /// unvalidated and untrusted. To provide validated and trusted data to the invocation of <paramref name="method"/>, the instance constructed
    /// for each invocation can contain that data in it, such that it's then available to <paramref name="method"/> as instance data.
    /// An <see cref="IServiceProvider"/> parameter can also be used to resolve services from a dependency injection container.
    /// </para>
    /// <para>
    /// By default, return values are serialized to <see cref="JsonElement"/> using <paramref name="options"/>'s
    /// <see cref="AIFunctionFactoryOptions.SerializerOptions"/> if provided, or else using <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// However, return values whose declared type is <see cref="AIContent"/>, a derived type of <see cref="AIContent"/>, or any type
    /// assignable from <see cref="IEnumerable{AIContent}"/> are returned directly without serialization.
    /// Handling of return values can be overridden via <see cref="AIFunctionFactoryOptions.MarshalResult"/>.
    /// </para>
    /// <para>
    /// In addition to the parameter schema, a JSON schema is also derived from the method's return type and exposed via the
    /// returned <see cref="AIFunction"/>'s <see cref="AIFunctionDeclaration.ReturnJsonSchema"/>. For methods returning
    /// <see cref="void"/>, <see cref="Task"/>, or <see cref="ValueTask"/>, no return schema is produced (the property is <see langword="null"/>).
    /// For methods returning <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>, the schema is derived from the
    /// unwrapped result type. Return schema generation can be excluded via <see cref="AIFunctionFactoryOptions.ExcludeResultSchema"/>,
    /// and its generation is governed by <paramref name="options"/>'s <see cref="AIFunctionFactoryOptions.JsonSchemaCreateOptions"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="createInstanceFunc"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> represents a static method.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> represents an open generic method.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> contains a parameter without a parameter name.</exception>
    /// <exception cref="JsonException">A parameter to <paramref name="method"/> or its return type is not serializable.</exception>
    public static AIFunction Create(
        MethodInfo method,
        Func<AIFunctionArguments, object> createInstanceFunc,
        AIFunctionFactoryOptions? options = null) =>
        ReflectionAIFunction.Build(method, createInstanceFunc, options ?? _defaultOptions);

    /// <summary>Creates an <see cref="AIFunctionDeclaration"/> using the specified parameters as the implementation of its corresponding properties.</summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="description">A description of the function, suitable for use in describing the purpose to a model.</param>
    /// <param name="jsonSchema">A JSON schema describing the function and its input parameters.</param>
    /// <param name="returnJsonSchema">A JSON schema describing the function's return value.</param>
    /// <returns>The created <see cref="AIFunctionDeclaration"/> that describes a function.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="CreateDeclaration"/> creates an <see cref="AIFunctionDeclaration"/> that can be used to describe a function
    /// but not invoke it. To create an invocable <see cref="AIFunction"/>, use Create. A non-invocable <see cref="AIFunctionDeclaration"/>
    /// can also be created from an invocable <see cref="AIFunction"/> using that function's <see cref="AIFunction.AsDeclarationOnly"/> method.
    /// </remarks>
    public static AIFunctionDeclaration CreateDeclaration(
        string name,
        string? description,
        JsonElement jsonSchema,
        JsonElement? returnJsonSchema = null) =>
        new DefaultAIFunctionDeclaration(
            Throw.IfNullOrEmpty(name),
            description ?? string.Empty,
            jsonSchema,
            returnJsonSchema);

    private sealed class DefaultAIFunctionDeclaration(
        string name, string description, JsonElement jsonSchema, JsonElement? returnJsonSchema) :
        AIFunctionDeclaration
    {
        public override string Name => name;
        public override string Description => description;
        public override JsonElement JsonSchema => jsonSchema;
        public override JsonElement? ReturnJsonSchema => returnJsonSchema;
    }

    private sealed class ReflectionAIFunction : AIFunction
    {
        public static ReflectionAIFunction Build(MethodInfo method, object? target, AIFunctionFactoryOptions options)
        {
            _ = Throw.IfNull(method);

            if (method.ContainsGenericParameters)
            {
                Throw.ArgumentException(nameof(method), "Open generic methods are not supported");
            }

            if (!method.IsStatic && target is null)
            {
                Throw.ArgumentNullException(nameof(target), "Target must not be null for an instance method.");
            }

            var functionDescriptor = ReflectionAIFunctionDescriptor.GetOrCreate(method, options);

            if (target is null && options.AdditionalProperties is null)
            {
                // We can use a cached value for static methods not specifying additional properties.
                return functionDescriptor.CachedDefaultInstance ??= new(functionDescriptor, target, options);
            }

            return new(functionDescriptor, target, options);
        }

        public static ReflectionAIFunction Build(
            MethodInfo method,
            Func<AIFunctionArguments, object> createInstanceFunc,
            AIFunctionFactoryOptions options)
        {
            _ = Throw.IfNull(method);
            _ = Throw.IfNull(createInstanceFunc);

            if (method.ContainsGenericParameters)
            {
                Throw.ArgumentException(nameof(method), "Open generic methods are not supported");
            }

            if (method.IsStatic)
            {
                Throw.ArgumentException(nameof(method), "The method must be an instance method.");
            }

            return new(ReflectionAIFunctionDescriptor.GetOrCreate(method, options), createInstanceFunc, options);
        }

        private ReflectionAIFunction(ReflectionAIFunctionDescriptor functionDescriptor, object? target, AIFunctionFactoryOptions options)
        {
            FunctionDescriptor = functionDescriptor;
            Target = target;
            AdditionalProperties = options.AdditionalProperties ?? EmptyReadOnlyDictionary<string, object?>.Instance;
        }

        private ReflectionAIFunction(
            ReflectionAIFunctionDescriptor functionDescriptor,
            Func<AIFunctionArguments, object> createInstanceFunc,
            AIFunctionFactoryOptions options)
        {
            FunctionDescriptor = functionDescriptor;
            CreateInstanceFunc = createInstanceFunc;
            AdditionalProperties = options.AdditionalProperties ?? EmptyReadOnlyDictionary<string, object?>.Instance;
        }

        public ReflectionAIFunctionDescriptor FunctionDescriptor { get; }
        public object? Target { get; }
        public Func<AIFunctionArguments, object>? CreateInstanceFunc { get; }

        public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; }
        public override string Name => FunctionDescriptor.Name;
        public override string Description => FunctionDescriptor.Description;
        public override MethodInfo UnderlyingMethod => FunctionDescriptor.Method;
        public override JsonElement JsonSchema => FunctionDescriptor.JsonSchema;
        public override JsonElement? ReturnJsonSchema => FunctionDescriptor.ReturnJsonSchema;
        public override JsonSerializerOptions JsonSerializerOptions => FunctionDescriptor.JsonSerializerOptions;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            bool disposeTarget = false;
            object? target = Target;
            try
            {
                if (CreateInstanceFunc is { } func)
                {
                    Debug.Assert(target is null, "Expected target to be null when we have a non-null target type");
                    Debug.Assert(!FunctionDescriptor.Method.IsStatic, "Expected an instance method");

                    target = func(arguments);
                    if (target is null)
                    {
                        Throw.InvalidOperationException("Unable to create an instance of the target type.");
                    }

                    disposeTarget = true;
                }

                var paramMarshallers = FunctionDescriptor.ParameterMarshallers;
                object?[] args = paramMarshallers.Length != 0 ? new object?[paramMarshallers.Length] : [];

                // If the configured serializer options request strict handling of unmapped members,
                // verify that every argument key corresponds to a declared parameter name. This mirrors
                // JsonSerializerOptions.UnmappedMemberHandling behavior for object deserialization by
                // applying the same policy to top-level AIFunction argument binding. Argument name matching
                // honors the comparer of the supplied AIFunctionArguments dictionary (ordinal by default).
                //
                // Validation is skipped when custom ParameterBindingOptions.BindParameter callbacks are in
                // use, since those may legitimately source values from argument keys that do not correspond
                // to the .NET parameter names.
                if (FunctionDescriptor.JsonSerializerOptions.UnmappedMemberHandling is JsonUnmappedMemberHandling.Disallow &&
                    arguments.Count > 0 &&
                    !FunctionDescriptor.HasCustomParameterBinding)
                {
                    HashSet<string> expectedNames = FunctionDescriptor.ExpectedArgumentNames;
                    int matched = 0;
                    foreach (string name in expectedNames)
                    {
                        if (arguments.ContainsKey(name))
                        {
                            matched++;
                        }
                    }

                    if (matched != arguments.Count)
                    {
                        foreach (KeyValuePair<string, object?> kvp in arguments)
                        {
                            if (!expectedNames.Contains(kvp.Key))
                            {
                                Throw.ArgumentException(
                                    nameof(arguments),
                                    $"The arguments dictionary contains an unexpected key '{kvp.Key}' that does not correspond to any parameter of '{Name}'.");
                            }
                        }

                        // Fallback for comparer mismatches (e.g. case-insensitive arguments dictionary
                        // with duplicate-casing keys aliasing to the same parameter).
                        Throw.ArgumentException(
                            nameof(arguments),
                            $"The arguments dictionary contains keys that do not correspond to any parameter of '{Name}'.");
                    }
                }

                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = paramMarshallers[i](arguments, cancellationToken);
                }

                return await FunctionDescriptor.ReturnParameterMarshaller(
                    ReflectionInvoke(FunctionDescriptor.Method, target, args), cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                if (disposeTarget)
                {
                    if (target is IAsyncDisposable ad)
                    {
                        await ad.DisposeAsync().ConfigureAwait(true);
                    }
                    else if (target is IDisposable d)
                    {
                        d.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// A descriptor for a .NET method-backed AIFunction that precomputes its marshalling delegates and JSON schema.
    /// </summary>
    private sealed class ReflectionAIFunctionDescriptor
    {
        private const int InnerCacheSoftLimit = 512;
        private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<DescriptorKey, ReflectionAIFunctionDescriptor>> _descriptorCache = new();

        /// <summary>A boxed <see cref="CancellationToken.None"/>.</summary>
        private static readonly object? _boxedDefaultCancellationToken = default(CancellationToken);

        /// <summary>
        /// Gets or creates a descriptor using the specified method and options.
        /// </summary>
        public static ReflectionAIFunctionDescriptor GetOrCreate(MethodInfo method, AIFunctionFactoryOptions options)
        {
            JsonSerializerOptions serializerOptions = options.SerializerOptions ?? AIJsonUtilities.DefaultOptions;
            AIJsonSchemaCreateOptions schemaOptions = options.JsonSchemaCreateOptions ?? AIJsonSchemaCreateOptions.Default;
            serializerOptions.MakeReadOnly();
            ConcurrentDictionary<DescriptorKey, ReflectionAIFunctionDescriptor> innerCache = _descriptorCache.GetOrCreateValue(serializerOptions);

            DescriptorKey key = new(method, options.Name, options.Description, options.ConfigureParameterBinding, options.MarshalResult, options.ExcludeResultSchema, schemaOptions);
            if (innerCache.TryGetValue(key, out ReflectionAIFunctionDescriptor? descriptor))
            {
                return descriptor;
            }

            descriptor = new(key, serializerOptions);
            return innerCache.Count < InnerCacheSoftLimit
                ? innerCache.GetOrAdd(key, descriptor)
                : descriptor;
        }

        private ReflectionAIFunctionDescriptor(DescriptorKey key, JsonSerializerOptions serializerOptions)
        {
            ParameterInfo[] parameters = key.Method.GetParameters();

            // Determine how each parameter should be bound. This is keyed on the parameter's position
            // rather than the ParameterInfo instance: MethodInfo.GetParameters() does not guarantee the
            // same ParameterInfo instances across concurrent first calls on a cold MethodInfo, and the
            // schema generator invokes IncludeParameter with ParameterInfo instances from its own
            // GetParameters() call. Keying on identity would race and silently drop ExcludeFromSchema.
            AIFunctionFactoryOptions.ParameterBindingOptions[]? boundParameters = null;
            if (parameters.Length != 0 && key.GetBindParameterOptions is not null)
            {
                boundParameters = new AIFunctionFactoryOptions.ParameterBindingOptions[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    boundParameters[i] = key.GetBindParameterOptions(parameters[i]);
                }
            }

            // Use that binding information to impact the schema generation.
            AIJsonSchemaCreateOptions schemaOptions = key.SchemaOptions with
            {
                IncludeParameter = parameterInfo =>
                {
                    // AIFunctionArguments and IServiceProvider parameters are always excluded from the schema.
                    if (parameterInfo.ParameterType == typeof(AIFunctionArguments) ||
                        parameterInfo.ParameterType == typeof(IServiceProvider))
                    {
                        return false;
                    }

                    // If the parameter is marked as excluded by GetBindParameterOptions, exclude it.
                    // Look up by position because the ParameterInfo passed here may be a different
                    // instance than the one boundParameters was built from (see comment above).
                    if (boundParameters is not null &&
                        (uint)parameterInfo.Position < (uint)boundParameters.Length &&
                        boundParameters[parameterInfo.Position].ExcludeFromSchema)
                    {
                        return false;
                    }

                    // If there was an existing IncludeParameter delegate, now defer to it as we've
                    // excluded everything we need to exclude.
                    if (key.SchemaOptions.IncludeParameter is { } existingIncludeParameter)
                    {
                        return existingIncludeParameter(parameterInfo);
                    }

                    // Everything else is included.
                    return true;
                },
            };

            // Get marshaling delegates for parameters.
            ParameterMarshallers = parameters.Length > 0 ? new Func<AIFunctionArguments, CancellationToken, object?>[parameters.Length] : [];
            HashSet<string> expectedArgumentNames = new(StringComparer.Ordinal);
            bool hasCustomParameterBinding = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                AIFunctionFactoryOptions.ParameterBindingOptions options =
                    boundParameters is not null ? boundParameters[i] : default;

                ParameterMarshallers[i] = GetParameterMarshaller(serializerOptions, options, parameters[i]);

                if (options.BindParameter is not null)
                {
                    // Custom BindParameter callbacks can legally source their value from arbitrary keys in the
                    // AIFunctionArguments dictionary, so we cannot know in advance which keys are "expected".
                    // Note this down so that strict unmapped-member validation is skipped in InvokeCoreAsync.
                    hasCustomParameterBinding = true;
                }

                // Collect the set of parameter names that are potentially sourced from the arguments dictionary.
                // Infrastructure parameters (CancellationToken, AIFunctionArguments, IServiceProvider) are always
                // bound from dedicated sources and are never resolved by argument name, so they are excluded from
                // the permitted set.
                Type pType = parameters[i].ParameterType;
                if (pType != typeof(CancellationToken) &&
                    pType != typeof(AIFunctionArguments) &&
                    pType != typeof(IServiceProvider) &&
                    !string.IsNullOrEmpty(parameters[i].Name))
                {
                    string effectiveName = AIJsonUtilities.GetParameterSchemaName(parameters[i]);
                    if (!expectedArgumentNames.Add(effectiveName))
                    {
                        Throw.ArgumentException("method", $"Multiple parameters are mapped to the same name '{effectiveName}'. Ensure that any {nameof(AIParameterNameAttribute)} values do not collide with each other or with other parameter names.");
                    }
                }
            }

            ExpectedArgumentNames = expectedArgumentNames;
            HasCustomParameterBinding = hasCustomParameterBinding;

            ReturnParameterMarshaller = GetReturnParameterMarshaller(key, serializerOptions, out Type? returnType);
            Method = key.Method;
            Name = key.Name ?? key.Method.GetCustomAttribute<AIFunctionNameAttribute>(inherit: true)?.Name ?? key.Method.GetCustomAttribute<DisplayNameAttribute>(inherit: true)?.DisplayName ?? GetFunctionName(key.Method);
            Description = key.Description ?? key.Method.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description ?? string.Empty;
            JsonSerializerOptions = serializerOptions;
            ReturnJsonSchema = returnType is null || key.ExcludeResultSchema ? null : AIJsonUtilities.CreateJsonSchema(
                NormalizeReturnType(returnType, serializerOptions),
                description: GetReturnParameterDescription(key.Method),
                serializerOptions: serializerOptions,
                inferenceOptions: schemaOptions);

            JsonSchema = AIJsonUtilities.CreateFunctionJsonSchema(
                key.Method,
                title: string.Empty, // Forces skipping of the title keyword
                description: string.Empty, // Forces skipping of the description keyword
                serializerOptions: serializerOptions,
                inferenceOptions: schemaOptions);
        }

        public string Name { get; }
        public string Description { get; }
        public MethodInfo Method { get; }
        public JsonSerializerOptions JsonSerializerOptions { get; }
        public JsonElement JsonSchema { get; }
        public JsonElement? ReturnJsonSchema { get; }
        public Func<AIFunctionArguments, CancellationToken, object?>[] ParameterMarshallers { get; }
        public Func<object?, CancellationToken, ValueTask<object?>> ReturnParameterMarshaller { get; }
        public HashSet<string> ExpectedArgumentNames { get; }
        public bool HasCustomParameterBinding { get; }
        public ReflectionAIFunction? CachedDefaultInstance { get; set; }

        private static string GetFunctionName(MethodInfo method)
        {
            // Get the function name to use.
            string name = SanitizeMemberName(method.Name);

            const string AsyncSuffix = "Async";
            if (IsAsyncMethod(method))
            {
                // If the method ends in "Async" or contains "Async_", remove the "Async".
                int asyncIndex = name.LastIndexOf(AsyncSuffix, StringComparison.Ordinal);
                if (asyncIndex > 0 &&
                    (asyncIndex + AsyncSuffix.Length == name.Length ||
                     ((asyncIndex + AsyncSuffix.Length < name.Length) && (name[asyncIndex + AsyncSuffix.Length] == '_'))))
                {
                    name =
#if NET
                        string.Concat(name.AsSpan(0, asyncIndex), name.AsSpan(asyncIndex + AsyncSuffix.Length));
#else
                        string.Concat(name.Substring(0, asyncIndex), name.Substring(asyncIndex + AsyncSuffix.Length));
#endif
                }
            }

            return name;

            static bool IsAsyncMethod(MethodInfo method)
            {
                Type t = method.ReturnType;

                if (t == typeof(Task) || t == typeof(ValueTask))
                {
                    return true;
                }

                if (t.IsGenericType)
                {
                    t = t.GetGenericTypeDefinition();
                    if (t == typeof(Task<>) || t == typeof(ValueTask<>) || t == typeof(IAsyncEnumerable<>))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Gets a delegate for handling the marshaling of a parameter.
        /// </summary>
        private static Func<AIFunctionArguments, CancellationToken, object?> GetParameterMarshaller(
            JsonSerializerOptions serializerOptions,
            AIFunctionFactoryOptions.ParameterBindingOptions bindingOptions,
            ParameterInfo parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                Throw.ArgumentException(nameof(parameter), "Parameter is missing a name.");
            }

            Type parameterType = parameter.ParameterType;

            // For CancellationToken parameters, we always bind to the token passed directly to InvokeAsync.
            if (parameterType == typeof(CancellationToken))
            {
                return static (_, cancellationToken) =>
                    cancellationToken == default ? _boxedDefaultCancellationToken : // optimize common case of a default CT to avoid boxing
                    cancellationToken;
            }

            // CancellationToken is the only parameter type that's handled exclusively by the implementation.
            // Now that it's been processed, check to see if the parameter should be handled via BindParameter.
            if (bindingOptions.BindParameter is { } bindParameter)
            {
                return (arguments, _) => bindParameter(parameter, arguments);
            }

            // We're now into default handling of everything else.

            // For AIFunctionArgument parameters, we bind to the arguments passed to InvokeAsync.
            if (parameterType == typeof(AIFunctionArguments))
            {
                return static (arguments, _) => arguments;
            }

            // For IServiceProvider parameters, we bind to the services passed to InvokeAsync via AIFunctionArguments.
            if (parameterType == typeof(IServiceProvider))
            {
                bool hasDefault = AIJsonUtilities.TryGetEffectiveDefaultValue(parameter, out _);
                return (arguments, _) =>
                {
                    IServiceProvider? services = arguments.Services;
                    if (!hasDefault && services is null)
                    {
                        ThrowNullServices(parameter.Name);
                    }

                    return services;
                };
            }

            // For all other parameters, create a marshaller that tries to extract the value from the arguments dictionary.
            // Resolve the contract used to marshal the value from JSON -- can throw if not supported or not found.
            JsonTypeInfo? typeInfo = serializerOptions.GetTypeInfo(parameterType);
            bool hasDefaultValue = AIJsonUtilities.TryGetEffectiveDefaultValue(parameter, out object? effectiveDefaultValue);
            string argumentName = AIJsonUtilities.GetParameterSchemaName(parameter);
            return (arguments, _) =>
            {
                // If the parameter has an argument specified in the dictionary, return that argument.
                if (arguments.TryGetValue(argumentName, out object? value))
                {
                    return value switch
                    {
                        null => null, // Return as-is if null -- if the parameter is a struct this will be handled by MethodInfo.Invoke
                        _ when parameterType.IsInstanceOfType(value) => value, // Do nothing if value is assignable to parameter type
                        JsonElement element => JsonSerializer.Deserialize(element, typeInfo),
                        JsonDocument doc => JsonSerializer.Deserialize(doc, typeInfo),
                        JsonNode node => JsonSerializer.Deserialize(node, typeInfo),
                        _ => MarshallViaJsonRoundtrip(value),
                    };

                    object? MarshallViaJsonRoundtrip(object value)
                    {
                        try
                        {
                            if (value is string text && IsPotentiallyJson(text))
                            {
                                Debug.Assert(typeInfo.Type != typeof(string), "string parameters should not enter this branch.");

                                // Account for the parameter potentially being a JSON string.
                                // The value is a string but the type is not. Try to deserialize it under the assumption that it's JSON.
                                // If it's not, we'll fall through to the default path that makes it valid JSON and then tries to deserialize.
                                try
                                {
                                    return JsonSerializer.Deserialize(text, typeInfo);
                                }
                                catch (JsonException)
                                {
                                    // If the string is not valid JSON, fall through to the round-trip.
                                }
                            }

                            string json = JsonSerializer.Serialize(value, serializerOptions.GetTypeInfo(value.GetType()));
                            return JsonSerializer.Deserialize(json, typeInfo);
                        }
                        catch
                        {
                            // Eat any exceptions and fall back to the original value to force a cast exception later on.
                            return value;
                        }
                    }
                }

                // If the parameter is required and there's no argument specified for it, throw.
                if (!hasDefaultValue)
                {
                    Throw.ArgumentException(nameof(arguments), $"The arguments dictionary is missing a value for the required parameter '{argumentName}'.");
                }

                // Otherwise, use the optional parameter's default value.
                return effectiveDefaultValue;
            };

            // Throws an ArgumentNullException indicating that AIFunctionArguments.Services must be provided.
            static void ThrowNullServices(string parameterName) =>
                Throw.ArgumentNullException($"arguments.{nameof(AIFunctionArguments.Services)}", $"Services are required for parameter '{parameterName}'.");
        }

        /// <summary>
        /// Gets a delegate for handling the result value of a method, converting it into the <see cref="Task{FunctionResult}"/> to return from the invocation.
        /// </summary>
        private static Func<object?, CancellationToken, ValueTask<object?>> GetReturnParameterMarshaller(
            DescriptorKey key, JsonSerializerOptions serializerOptions, out Type? returnType)
        {
            returnType = key.Method.ReturnType;
            JsonTypeInfo returnTypeInfo;
            Func<object?, Type?, CancellationToken, ValueTask<object?>>? marshalResult = key.MarshalResult;

            // Void
            if (returnType == typeof(void))
            {
                returnType = null;
                if (marshalResult is not null)
                {
                    return (result, cancellationToken) => marshalResult(null, null, cancellationToken);
                }

                return static (_, _) => new ValueTask<object?>((object?)null);
            }

            // Task
            if (returnType == typeof(Task))
            {
                returnType = null;
                if (marshalResult is not null)
                {
                    return async (result, cancellationToken) =>
                    {
                        await ((Task)ThrowIfNullResult(result)).ConfigureAwait(true);
                        return await marshalResult(null, null, cancellationToken).ConfigureAwait(true);
                    };
                }

                return async static (result, _) =>
                {
                    await ((Task)ThrowIfNullResult(result)).ConfigureAwait(true);
                    return null;
                };
            }

            // ValueTask
            if (returnType == typeof(ValueTask))
            {
                returnType = null;
                if (marshalResult is not null)
                {
                    return async (result, cancellationToken) =>
                    {
                        await ((ValueTask)ThrowIfNullResult(result)).ConfigureAwait(true);
                        return await marshalResult(null, null, cancellationToken).ConfigureAwait(true);
                    };
                }

                return async static (result, _) =>
                {
                    await ((ValueTask)ThrowIfNullResult(result)).ConfigureAwait(true);
                    return null;
                };
            }

            if (returnType.IsGenericType)
            {
                // Task<T>
                if (returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    MethodInfo taskResultGetter = GetMethodFromGenericMethodDefinition(returnType, _taskGetResult);
                    returnType = taskResultGetter.ReturnType;

                    // If a MarshalResult delegate is provided, use it.
                    if (marshalResult is not null)
                    {
                        return async (taskObj, cancellationToken) =>
                        {
                            await ((Task)ThrowIfNullResult(taskObj)).ConfigureAwait(true);
                            object? result = ReflectionInvoke(taskResultGetter, taskObj, null);
                            return await marshalResult(result, taskResultGetter.ReturnType, cancellationToken).ConfigureAwait(true);
                        };
                    }

                    // Special-case AIContent results to not be serialized, so that IChatClients can type test and handle them
                    // specially, such as by returning content to the model/service in a manner appropriate to the content type.
                    if (IsAIContentRelatedType(returnType))
                    {
                        return async (taskObj, cancellationToken) =>
                        {
                            await ((Task)ThrowIfNullResult(taskObj)).ConfigureAwait(true);
                            return ReflectionInvoke(taskResultGetter, taskObj, null);
                        };
                    }

                    // For everything else, just serialize the result as-is.
                    returnTypeInfo = serializerOptions.GetTypeInfo(returnType);
                    return async (taskObj, cancellationToken) =>
                    {
                        await ((Task)ThrowIfNullResult(taskObj)).ConfigureAwait(true);
                        object? result = ReflectionInvoke(taskResultGetter, taskObj, null);
                        return await SerializeResultAsync(result, returnTypeInfo, cancellationToken).ConfigureAwait(true);
                    };
                }

                // ValueTask<T>
                if (returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    MethodInfo valueTaskAsTask = GetMethodFromGenericMethodDefinition(returnType, _valueTaskAsTask);
                    MethodInfo asTaskResultGetter = GetMethodFromGenericMethodDefinition(valueTaskAsTask.ReturnType, _taskGetResult);
                    returnType = asTaskResultGetter.ReturnType;

                    // If a MarshalResult delegate is provided, use it.
                    if (marshalResult is not null)
                    {
                        return async (taskObj, cancellationToken) =>
                        {
                            var task = (Task)ReflectionInvoke(valueTaskAsTask, ThrowIfNullResult(taskObj), null)!;
                            await task.ConfigureAwait(true);
                            object? result = ReflectionInvoke(asTaskResultGetter, task, null);
                            return await marshalResult(result, asTaskResultGetter.ReturnType, cancellationToken).ConfigureAwait(true);
                        };
                    }

                    // Special-case AIContent results to not be serialized, so that IChatClients can type test and handle them
                    // specially, such as by returning content to the model/service in a manner appropriate to the content type.
                    if (IsAIContentRelatedType(returnType))
                    {
                        return async (taskObj, cancellationToken) =>
                        {
                            var task = (Task)ReflectionInvoke(valueTaskAsTask, ThrowIfNullResult(taskObj), null)!;
                            await task.ConfigureAwait(true);
                            return ReflectionInvoke(asTaskResultGetter, task, null);
                        };
                    }

                    // For everything else, just serialize the result as-is.
                    returnTypeInfo = serializerOptions.GetTypeInfo(returnType);
                    return async (taskObj, cancellationToken) =>
                    {
                        var task = (Task)ReflectionInvoke(valueTaskAsTask, ThrowIfNullResult(taskObj), null)!;
                        await task.ConfigureAwait(true);
                        object? result = ReflectionInvoke(asTaskResultGetter, task, null);
                        return await SerializeResultAsync(result, returnTypeInfo, cancellationToken).ConfigureAwait(true);
                    };
                }
            }

            // If a MarshalResult delegate is provided, use it.
            if (marshalResult is not null)
            {
                Type returnTypeCopy = returnType;
                return (result, cancellationToken) => marshalResult(result, returnTypeCopy, cancellationToken);
            }

            // Special-case AIContent results to not be serialized, so that IChatClients can type test and handle them
            // specially, such as by returning content to the model/service in a manner appropriate to the content type.
            if (IsAIContentRelatedType(returnType))
            {
                return static (result, _) => new ValueTask<object?>(result);
            }

            // For everything else, just serialize the result as-is.
            returnTypeInfo = serializerOptions.GetTypeInfo(returnType);
            return (result, cancellationToken) => SerializeResultAsync(result, returnTypeInfo, cancellationToken);

            static async ValueTask<object?> SerializeResultAsync(object? result, JsonTypeInfo returnTypeInfo, CancellationToken cancellationToken)
            {
                if (returnTypeInfo.Kind is JsonTypeInfoKind.None)
                {
                    // Special-case trivial contracts to avoid the more expensive general-purpose serialization path.
                    return JsonSerializer.SerializeToElement(result, returnTypeInfo);
                }

                // Serialize asynchronously to support potential IAsyncEnumerable responses.
                using PooledMemoryStream stream = new();
                await JsonSerializer.SerializeAsync(stream, result, returnTypeInfo, cancellationToken).ConfigureAwait(true);
                Utf8JsonReader reader = new(stream.GetBuffer());
                return JsonElement.ParseValue(ref reader);
            }

            // Throws an exception if a result is found to be null unexpectedly
            static object ThrowIfNullResult(object? result) => result ?? throw new InvalidOperationException("Function returned null unexpectedly.");
        }

        private static readonly MethodInfo _taskGetResult = typeof(Task<>).GetProperty(nameof(Task<int>.Result), BindingFlags.Instance | BindingFlags.Public)!.GetMethod!;
        private static readonly MethodInfo _valueTaskAsTask = typeof(ValueTask<>).GetMethod(nameof(ValueTask<int>.AsTask), BindingFlags.Instance | BindingFlags.Public)!;

        private static MethodInfo GetMethodFromGenericMethodDefinition(Type specializedType, MethodInfo genericMethodDefinition)
        {
            Debug.Assert(specializedType.IsGenericType && specializedType.GetGenericTypeDefinition() == genericMethodDefinition.DeclaringType, "generic member definition doesn't match type.");
#if NET
            return (MethodInfo)specializedType.GetMemberWithSameMetadataDefinitionAs(genericMethodDefinition);
#else
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            return specializedType.GetMethods(All).First(m => m.MetadataToken == genericMethodDefinition.MetadataToken);
#endif
        }

        private static bool IsAIContentRelatedType(Type type) =>
            typeof(AIContent).IsAssignableFrom(type) ||
            typeof(IEnumerable<AIContent>).IsAssignableFrom(type);

        private static string? GetReturnParameterDescription(MethodInfo method)
        {
            try
            {
                return method.ReturnParameter.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
            }
            catch (Exception e) when (e is ArgumentNullException or NullReferenceException or IndexOutOfRangeException)
            {
                // DynamicMethod return parameters don't support GetCustomAttribute. Additionally, on .NET Framework,
                // querying inherited attributes on the return parameter of an overriding method can throw
                // IndexOutOfRangeException. In either case, treat the description as absent.
                return null;
            }
        }

        private static Type NormalizeReturnType(Type type, JsonSerializerOptions? options)
        {
            options ??= AIJsonUtilities.DefaultOptions;

            if (options == AIJsonUtilities.DefaultOptions && !options.TryGetTypeInfo(type, out _))
            {
                // GetTypeInfo is not polymorphic, so attempts to look up derived types will fail even if the
                // base type is registered. In some cases, though, we can fall back to using interfaces
                // we know we have contracts for in AIJsonUtilities.DefaultOptions where the semantics of using
                // that interface will be reasonable. This should really only affect situations where
                // reflection-based serialization is disabled.

                if (typeof(IEnumerable<AIContent>).IsAssignableFrom(type))
                {
                    return typeof(IEnumerable<AIContent>);
                }

                if (typeof(IEnumerable<ChatMessage>).IsAssignableFrom(type))
                {
                    return typeof(IEnumerable<ChatMessage>);
                }

                if (typeof(IEnumerable<string>).IsAssignableFrom(type))
                {
                    return typeof(IEnumerable<string>);
                }
            }

            return type;
        }

        private record struct DescriptorKey(
            MethodInfo Method,
            string? Name,
            string? Description,
            Func<ParameterInfo, AIFunctionFactoryOptions.ParameterBindingOptions>? GetBindParameterOptions,
            Func<object?, Type?, CancellationToken, ValueTask<object?>>? MarshalResult,
            bool ExcludeResultSchema,
            AIJsonSchemaCreateOptions SchemaOptions);
    }

    /// <summary>
    /// Quickly checks if the specified string is potentially JSON
    /// by checking if the first non-whitespace characters are valid JSON start tokens.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <returns>If <see langword="false"/> then the string is definitely not valid JSON.</returns>
    private static bool IsPotentiallyJson(string value) => PotentiallyJsonRegex().IsMatch(value);
#if NET
    [GeneratedRegex(PotentiallyJsonRegexString, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex PotentiallyJsonRegex();
#else
    private static Regex PotentiallyJsonRegex() => _potentiallyJsonRegex;
    private static readonly Regex _potentiallyJsonRegex = new(PotentiallyJsonRegexString, RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
#endif
    private const string PotentiallyJsonRegexString = """
        ^\s*        # Optional whitespace at the start of the string
           ( null   # null literal
           | false  # false literal
           | true   # true literal
           | -?[0-9]# number
           | "      # string
           | \[     # start array
           | {      # start object
           | //     # Start of single-line comment
           | /\*    # Start of multi-line comment
           )
        """;

    /// <summary>
    /// Removes characters from a .NET member name that shouldn't be used in an AI function name.
    /// </summary>
    /// <param name="memberName">The .NET member name that should be sanitized.</param>
    /// <returns>
    /// Replaces non-alphanumeric characters in the identifier with the underscore character.
    /// Primarily intended to remove characters produced by compiler-generated method name mangling.
    /// </returns>
    private static string SanitizeMemberName(string memberName)
    {
        // Handle compiler-generated names (local functions and lambdas)
        // Local functions: <ContainingMethod>g__LocalFunctionName|ordinal_depth -> ContainingMethod_LocalFunctionName_ordinal_depth
        // Lambdas: <ContainingMethod>b__ordinal_depth -> ContainingMethod_ordinal_depth
        if (CompilerGeneratedNameRegex().Match(memberName) is { Success: true } match)
        {
            memberName = $"{match.Groups[1].Value}_{match.Groups[2].Value}";
        }

        // Replace all non-alphanumeric characters with underscores.
        return InvalidNameCharsRegex().Replace(memberName, "_");
    }

    /// <summary>Regex that matches compiler-generated names (local functions and lambdas).</summary>
#if NET
    [GeneratedRegex(@"^<([^>]+)>\w__(.+)")]
    private static partial Regex CompilerGeneratedNameRegex();
#else
    private static Regex CompilerGeneratedNameRegex() => _compilerGeneratedNameRegex;
    private static readonly Regex _compilerGeneratedNameRegex = new(@"^<([^>]+)>\w__(.+)", RegexOptions.Compiled);
#endif

    /// <summary>Regex that flags any character other than ASCII digits or letters.</summary>
    /// <remarks>Underscore isn't included so that sequences of underscores are replaced by a single one.</remarks>
#if NET
    [GeneratedRegex("[^0-9A-Za-z]+")]
    private static partial Regex InvalidNameCharsRegex();
#else
    private static Regex InvalidNameCharsRegex() => _invalidNameCharsRegex;
    private static readonly Regex _invalidNameCharsRegex = new("[^0-9A-Za-z]+", RegexOptions.Compiled);
#endif

    /// <summary>Invokes the MethodInfo with the specified target object and arguments.</summary>
    private static object? ReflectionInvoke(MethodInfo method, object? target, object?[]? arguments)
    {
#if NET
        return method.Invoke(target, BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null);
#else
        try
        {
            return method.Invoke(target, BindingFlags.Default, binder: null, arguments, culture: null);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            // If we're targeting .NET Framework, such that BindingFlags.DoNotWrapExceptions
            // is ignored, the original exception will be wrapped in a TargetInvocationException.
            // Unwrap it and throw that original exception, maintaining its stack information.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
#endif
    }

    /// <summary>
    /// Implements a simple write-only memory stream that uses pooled buffers.
    /// </summary>
    private sealed class PooledMemoryStream : Stream
    {
        private const int DefaultBufferSize = 4096;
        private byte[] _buffer;
        private int _position;

        public PooledMemoryStream(int initialCapacity = DefaultBufferSize)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _position = 0;
        }

        public ReadOnlySpan<byte> GetBuffer() => _buffer.AsSpan(0, _position);
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => _position;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureNotDisposed();
            EnsureCapacity(_position + count);

            Buffer.BlockCopy(buffer, offset, _buffer, _position, count);
            _position += count;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();

#if NET
        public override
#else
        private
#endif
        ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask(Task.FromCanceled(cancellationToken));
            }

            EnsureCapacity(_position + buffer.Length);

            buffer.Span.CopyTo(_buffer.AsSpan(_position));
            _position += buffer.Length;

            return default;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }

            base.Dispose(disposing);
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= _buffer.Length)
            {
                return;
            }

            int newCapacity = Math.Max(requiredCapacity, _buffer.Length * 2);
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }

        private void EnsureNotDisposed()
        {
            if (_buffer is null)
            {
                Throw();
                static void Throw() => throw new ObjectDisposedException(nameof(PooledMemoryStream));
            }
        }
    }
}
