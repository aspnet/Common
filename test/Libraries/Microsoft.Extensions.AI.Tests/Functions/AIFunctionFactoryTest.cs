// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable S103 // Lines should not be too long
#pragma warning disable S107 // Methods should not have too many parameters
#pragma warning disable S1144 // Unused private types or members should be removed (accessed via reflection)
#pragma warning disable S2760 // Sequential tests should not check the same condition
#pragma warning disable S3358 // Ternary operators should not be nested
#pragma warning disable S5034 // "ValueTask" should be consumed correctly

namespace Microsoft.Extensions.AI;

public partial class AIFunctionFactoryTest
{
    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("method", () => AIFunctionFactory.Create(method: null!));
        Assert.Throws<ArgumentNullException>("method", () => AIFunctionFactory.Create(method: null!, target: new object()));
        Assert.Throws<ArgumentNullException>("method", () => AIFunctionFactory.Create(method: null!, target: new object(), name: "myAiFunk"));
        Assert.Throws<ArgumentNullException>("target", () => AIFunctionFactory.Create(typeof(AIFunctionFactoryTest).GetMethod(nameof(InvalidArguments_Throw))!, (object?)null));
        Assert.Throws<ArgumentNullException>("createInstanceFunc", () =>
            AIFunctionFactory.Create(typeof(AIFunctionFactoryTest).GetMethod(nameof(InvalidArguments_Throw))!, (Func<AIFunctionArguments, object>)null!));
        Assert.Throws<ArgumentException>("method", () => AIFunctionFactory.Create(typeof(List<>).GetMethod("Add")!, new List<int>()));
    }

    [Fact]
    public async Task Parameters_MappedByName_Async()
    {
        AIFunction func;

        func = AIFunctionFactory.Create((string a) => a + " " + a);
        AssertExtensions.EqualFunctionCallResults("test test", await func.InvokeAsync(new() { ["a"] = "test" }));

        func = AIFunctionFactory.Create((string a, string b) => b + " " + a);
        AssertExtensions.EqualFunctionCallResults("hello world", await func.InvokeAsync(new() { ["b"] = "hello", ["a"] = "world" }));

        func = AIFunctionFactory.Create((int a, long b) => a + b);
        AssertExtensions.EqualFunctionCallResults(3L, await func.InvokeAsync(new() { ["a"] = 1, ["b"] = 2L }));
    }

    [Fact]
    public async Task Parameters_DefaultValuesAreUsedButOverridable_Async()
    {
        AIFunction func = AIFunctionFactory.Create((string a = "test") => a + " " + a);
        AssertExtensions.EqualFunctionCallResults("test test", await func.InvokeAsync());
        AssertExtensions.EqualFunctionCallResults("hello hello", await func.InvokeAsync(new() { ["a"] = "hello" }));
    }

    [Fact]
    public async Task Parameters_DefaultValueAttributeIsRespected_Async()
    {
        // Test with null default value
        AIFunction funcNull = AIFunctionFactory.Create(([DefaultValue(null)] string? text) => text ?? "was null");

        // Schema should not list 'text' as required and should have default value
        string schema = funcNull.JsonSchema.ToString();
        Assert.Contains("\"text\"", schema);
        Assert.DoesNotContain("\"required\"", schema);
        Assert.Contains("\"default\":null", schema);

        // Should be invocable without providing the parameter
        AssertExtensions.EqualFunctionCallResults("was null", await funcNull.InvokeAsync());

        // Should be overridable
        AssertExtensions.EqualFunctionCallResults("hello", await funcNull.InvokeAsync(new() { ["text"] = "hello" }));

        // Test with non-null default value
        AIFunction funcValue = AIFunctionFactory.Create(([DefaultValue("default")] string text) => text);
        schema = funcValue.JsonSchema.ToString();
        Assert.DoesNotContain("\"required\"", schema);
        Assert.Contains("\"default\":\"default\"", schema);

        AssertExtensions.EqualFunctionCallResults("default", await funcValue.InvokeAsync());
        AssertExtensions.EqualFunctionCallResults("custom", await funcValue.InvokeAsync(new() { ["text"] = "custom" }));

        // Test with int default value
        AIFunction funcInt = AIFunctionFactory.Create(([DefaultValue(42)] int x) => x * 2);
        schema = funcInt.JsonSchema.ToString();
        Assert.DoesNotContain("\"required\"", schema);
        Assert.Contains("\"default\":42", schema);

        AssertExtensions.EqualFunctionCallResults(84, await funcInt.InvokeAsync());
        AssertExtensions.EqualFunctionCallResults(10, await funcInt.InvokeAsync(new() { ["x"] = 5 }));

        // Test that DefaultValue attribute takes precedence over C# default value
        AIFunction funcBoth = AIFunctionFactory.Create(([DefaultValue(100)] int y = 50) => y);
        schema = funcBoth.JsonSchema.ToString();
        Assert.DoesNotContain("\"required\"", schema);
        Assert.Contains("\"default\":100", schema); // DefaultValue should take precedence

        AssertExtensions.EqualFunctionCallResults(100, await funcBoth.InvokeAsync()); // Should use DefaultValue, not C# default
    }

    [Fact]
    public async Task Parameters_MissingRequiredParametersFail_Async()
    {
        AIFunction[] funcs =
        [
            AIFunctionFactory.Create((string theParam) => theParam + " " + theParam),
            AIFunctionFactory.Create((string? theParam) => theParam + " " + theParam),
            AIFunctionFactory.Create((int theParam) => theParam * 2),
            AIFunctionFactory.Create((int? theParam) => theParam * 2),
            AIFunctionFactory.Create(([AIParameterName("theParam")] string otherName) => otherName),
        ];

        foreach (AIFunction f in funcs)
        {
            Exception e = await Assert.ThrowsAsync<ArgumentException>(() => f.InvokeAsync().AsTask());
            Assert.Contains("'theParam'", e.Message);
        }
    }

    [Fact]
    public async Task Parameters_ToleratesJsonEncodedParameters()
    {
        AIFunction func = AIFunctionFactory.Create((int x, int y, int z, int w, int u) => x + y + z + w + u);

        var result = await func.InvokeAsync(new()
        {
            ["x"] = "1",
            ["y"] = JsonNode.Parse("2"),
            ["z"] = JsonDocument.Parse("3"),
            ["w"] = JsonDocument.Parse("4").RootElement,
            ["u"] = 5M, // boxed decimal cannot be cast to int, requires conversion
        });

        AssertExtensions.EqualFunctionCallResults(15, result);
    }

    [Theory]
    [InlineData("   null")]
    [InlineData("   false   ")]
    [InlineData("true   ")]
    [InlineData("42")]
    [InlineData("0.0")]
    [InlineData("-1e15")]
    [InlineData("  \"I am a string!\" ")]
    [InlineData("  {}")]
    [InlineData("[]")]
    [InlineData("// single-line comment\r\nnull")]
    [InlineData("/* multi-line\r\ncomment */\r\nnull")]
    public async Task Parameters_ToleratesJsonStringParameters(string jsonStringParam)
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions) { ReadCommentHandling = JsonCommentHandling.Skip };
        AIFunction func = AIFunctionFactory.Create((JsonElement param) => param, serializerOptions: options);
        JsonElement expectedResult = JsonDocument.Parse(jsonStringParam, new() { CommentHandling = JsonCommentHandling.Skip }).RootElement;

        var result = await func.InvokeAsync(new()
        {
            ["param"] = jsonStringParam
        });

        AssertExtensions.EqualFunctionCallResults(expectedResult, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("                 \r\n")]
    [InlineData("I am a string!")]
    [InlineData("/* Code snippet */ int main(void) { return 0; }")]
    [InlineData("let rec Y F x = F (Y F) x")]
    [InlineData("+3")]
    public async Task Parameters_ToleratesInvalidJsonStringParameters(string invalidJsonParam)
    {
        AIFunction func = AIFunctionFactory.Create((JsonElement param) => param);
        JsonElement expectedResult = JsonDocument.Parse(JsonSerializer.Serialize(invalidJsonParam, JsonContext.Default.String)).RootElement;

        var result = await func.InvokeAsync(new()
        {
            ["param"] = invalidJsonParam
        });

        AssertExtensions.EqualFunctionCallResults(expectedResult, result);
    }

    [Fact]
    public async Task Parameters_MappedByType_Async()
    {
        using var cts = new CancellationTokenSource();

        foreach (CancellationToken ctArg in new[] { cts.Token, default })
        {
            CancellationToken written = default;
            AIFunction func = AIFunctionFactory.Create((int value1 = 1, string value2 = "2", CancellationToken cancellationToken = default) =>
            {
                written = cancellationToken;
                return 42;
            });
            AssertExtensions.EqualFunctionCallResults(42, await func.InvokeAsync(cancellationToken: ctArg));
            Assert.Equal(ctArg, written);
            Assert.DoesNotContain("cancellationToken", func.JsonSchema.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Returns_AsyncReturnTypesSupported_Async()
    {
        AIFunction func;

        func = AIFunctionFactory.Create(Task<string> (string a) => Task.FromResult(a + " " + a));
        Assert.Equal("""{"type":"string"}""", func.ReturnJsonSchema.ToString());
        AssertExtensions.EqualFunctionCallResults("test test", await func.InvokeAsync(new() { ["a"] = "test" }));

        func = AIFunctionFactory.Create(ValueTask<string> (string a, string b) => new ValueTask<string>(b + " " + a));
        Assert.Equal("""{"type":"string"}""", func.ReturnJsonSchema.ToString());
        AssertExtensions.EqualFunctionCallResults("hello world", await func.InvokeAsync(new() { ["b"] = "hello", ["a"] = "world" }));

        long result = 0;
        func = AIFunctionFactory.Create(async Task (int a, long b) => { result = a + b; await Task.Yield(); });
        Assert.Null(func.ReturnJsonSchema);
        AssertExtensions.EqualFunctionCallResults(null, await func.InvokeAsync(new() { ["a"] = 1, ["b"] = 2L }));
        Assert.Equal(3, result);

        result = 0;
        func = AIFunctionFactory.Create(async ValueTask (int a, long b) => { result = a + b; await Task.Yield(); });
        Assert.Null(func.ReturnJsonSchema);
        AssertExtensions.EqualFunctionCallResults(null, await func.InvokeAsync(new() { ["a"] = 1, ["b"] = 2L }));
        Assert.Equal(3, result);

        func = AIFunctionFactory.Create((int count) => SimpleIAsyncEnumerable(count), serializerOptions: JsonContext.Default.Options);
        Assert.Equal("""{"type":"array","items":{"type":"integer"}}""", func.ReturnJsonSchema.ToString());
        AssertExtensions.EqualFunctionCallResults(new int[] { 0, 1, 2, 3, 4 }, await func.InvokeAsync(new() { ["count"] = 5 }), JsonContext.Default.Options);

        static async IAsyncEnumerable<int> SimpleIAsyncEnumerable(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }

        func = AIFunctionFactory.Create(() => (IAsyncEnumerable<int>)new ThrowingAsyncEnumerable(), serializerOptions: JsonContext.Default.Options);
        await Assert.ThrowsAsync<NotImplementedException>(() => func.InvokeAsync().AsTask());
    }

    private sealed class ThrowingAsyncEnumerable : IAsyncEnumerable<int>
    {
#pragma warning disable S3717 // Track use of "NotImplementedException"
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => throw new NotImplementedException();
#pragma warning restore S3717 // Track use of "NotImplementedException"
    }

    [Fact]
    public void Metadata_DerivedFromLambda()
    {
        AIFunction func;

        Func<string> dotnetFunc = () => "test";
        func = AIFunctionFactory.Create(dotnetFunc);
        Assert.Contains("Metadata_DerivedFromLambda", func.Name);
        Assert.Empty(func.Description);
        Assert.Same(dotnetFunc.Method, func.UnderlyingMethod);

        Func<string, string> dotnetFunc2 = a => a + " " + a;
        func = AIFunctionFactory.Create(dotnetFunc2);
        Assert.Contains("Metadata_DerivedFromLambda", func.Name);
        Assert.Empty(func.Description);
        Assert.Same(dotnetFunc2.Method, func.UnderlyingMethod);

        Func<string, string, string> dotnetFunc3 = [Description("This is a test function")] ([Description("This is A")] a, [Description("This is B")] b) => b + " " + a;
        func = AIFunctionFactory.Create(dotnetFunc3);
        Assert.Contains("Metadata_DerivedFromLambda", func.Name);
        Assert.Equal("This is a test function", func.Description);
        Assert.Same(dotnetFunc3.Method, func.UnderlyingMethod);
        Assert.Collection(func.UnderlyingMethod!.GetParameters(),
            p => Assert.Equal("This is A", p.GetCustomAttribute<DescriptionAttribute>()?.Description),
            p => Assert.Equal("This is B", p.GetCustomAttribute<DescriptionAttribute>()?.Description));
    }

    [Fact]
    public void Metadata_DisplayNameAttribute()
    {
        // Test DisplayNameAttribute on a delegate method
        Func<string> funcWithDisplayName = [DisplayName("get_user_id")] () => "test";
        AIFunction func = AIFunctionFactory.Create(funcWithDisplayName);
        Assert.Equal("get_user_id", func.Name);
        Assert.Empty(func.Description);

        // Test DisplayNameAttribute with DescriptionAttribute
        Func<string> funcWithBoth = [DisplayName("my_function")][Description("A test function")] () => "test";
        func = AIFunctionFactory.Create(funcWithBoth);
        Assert.Equal("my_function", func.Name);
        Assert.Equal("A test function", func.Description);

        // Test that explicit name parameter takes precedence over DisplayNameAttribute
        func = AIFunctionFactory.Create(funcWithDisplayName, name: "explicit_name");
        Assert.Equal("explicit_name", func.Name);

        // Test DisplayNameAttribute with options
        func = AIFunctionFactory.Create(funcWithDisplayName, new AIFunctionFactoryOptions());
        Assert.Equal("get_user_id", func.Name);

        // Test that options.Name takes precedence over DisplayNameAttribute
        func = AIFunctionFactory.Create(funcWithDisplayName, new AIFunctionFactoryOptions { Name = "options_name" });
        Assert.Equal("options_name", func.Name);

        // Test function without DisplayNameAttribute falls back to method name
        Func<string> funcWithoutDisplayName = () => "test";
        func = AIFunctionFactory.Create(funcWithoutDisplayName);
        Assert.Contains("Metadata_DisplayNameAttribute", func.Name); // Will contain the lambda method name
    }

    [Fact]
    public void Metadata_AIFunctionNameAttribute()
    {
        Func<string> funcWithAttribute = [AIFunctionName("get_user")] () => "test";
        AIFunction func = AIFunctionFactory.Create(funcWithAttribute);
        Assert.Equal("get_user", func.Name);

        Func<string> funcWithBoth = [AIFunctionName("my_function")][DisplayName("display_name")] () => "test";
        func = AIFunctionFactory.Create(funcWithBoth);
        Assert.Equal("my_function", func.Name);

        func = AIFunctionFactory.Create(funcWithAttribute, name: "explicit_name");
        Assert.Equal("explicit_name", func.Name);

        func = AIFunctionFactory.Create(funcWithAttribute, new AIFunctionFactoryOptions { Name = "options_name" });
        Assert.Equal("options_name", func.Name);
    }

    [Fact]
    public void Metadata_AIFunctionAndParameterNameAttributes_PreservedByAsDeclarationOnly()
    {
        AIFunction func = AIFunctionFactory.Create([AIFunctionName("my_tool")] ([AIParameterName("my_param")] string myParam) => myParam);

        AIFunctionDeclaration declaration = func.AsDeclarationOnly();

        Assert.Equal("my_tool", declaration.Name);
        Assert.Equal(func.JsonSchema.ToString(), declaration.JsonSchema.ToString());
        Assert.Contains("my_param", declaration.JsonSchema.ToString());
        Assert.IsNotAssignableFrom<AIFunction>(declaration);
    }

    [Fact]
    public async Task Parameters_MappedByAIParameterNameAttribute_Async()
    {
        AIFunction func = AIFunctionFactory.Create(([AIParameterName("$select")] string select, int top) => select + top);

        AssertExtensions.EqualFunctionCallResults("Name2", await func.InvokeAsync(new() { ["$select"] = "Name", ["top"] = 2 }));
    }

    [Fact]
    public void Parameters_AIParameterNameAttribute_OverridesSchemaPropertyName()
    {
        AIFunction func = AIFunctionFactory.Create(
            ([AIParameterName("my_param")] string myParam, int top) => myParam + top);

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "my_param": { "type": "string" },
                    "top": { "type": "integer" }
                },
                "required": ["my_param", "top"]
            }
            """).RootElement;

        AssertExtensions.EqualJsonValues(expectedSchema, func.JsonSchema);
    }

    [Fact]
    public async Task Parameters_AIParameterNameAttribute_StrictUnmappedMemberHandling_Async()
    {
        JsonSerializerOptions strictOptions = new(AIJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        AIFunction func = AIFunctionFactory.Create(
            ([AIParameterName("my_param")] string myParam) => myParam,
            new AIFunctionFactoryOptions { SerializerOptions = strictOptions });

        // The overridden name is "expected", so it passes strict validation.
        AssertExtensions.EqualFunctionCallResults("Name", await func.InvokeAsync(new() { ["my_param"] = "Name" }));

        // The underlying C# name is now an unexpected argument.
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>("arguments", async () =>
            await func.InvokeAsync(new() { ["myParam"] = "Name" }));
        Assert.Contains("myParam", ex.Message);
    }

    [Fact]
    public async Task Parameters_AIParameterNameAttribute_InheritedByOverride_Async()
    {
        MethodInfo overrideMethod = typeof(MyDerivedType).GetMethod(nameof(MyDerivedType.Method))!;
        AIFunction func = AIFunctionFactory.Create(overrideMethod, new MyDerivedType());

        Assert.Contains("my_param", func.JsonSchema.ToString());
        Assert.DoesNotContain("\"myParam\"", func.JsonSchema.ToString());

        AssertExtensions.EqualFunctionCallResults("param='Name'", await func.InvokeAsync(new() { ["my_param"] = "Name" }));
    }

    [Fact]
    public void Parameters_AIParameterNameAttribute_EscapesJsonPointerRef()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

        AIFunction func = AIFunctionFactory.Create(
            ([AIParameterName("a/b~c")] AIParameterNameAttributeRecursiveNode node) => node.ToString(),
            new AIFunctionFactoryOptions { SerializerOptions = options });

        string schema = func.JsonSchema.ToString();

        Assert.Contains("#/properties/a~1b~0c", schema);
        Assert.DoesNotContain("#/properties/a/b~c", schema);
    }

    [Fact]
    public void Parameters_AIParameterNameAttribute_DuplicateNames_Throw()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => AIFunctionFactory.Create(
            ([AIParameterName("dup")] string first, [AIParameterName("dup")] string second) => first + second));
        Assert.Contains("dup", ex.Message);
        Assert.Equal("method", ex.ParamName);

        ArgumentException ex2 = Assert.Throws<ArgumentException>(() => AIFunctionFactory.Create(
            ([AIParameterName("filter")] string select, string filter) => select + filter));
        Assert.Contains("filter", ex2.Message);
        Assert.Equal("method", ex2.ParamName);
    }

    [Fact]
    public void Parameters_AIParameterNameAttribute_DuplicateNames_ExcludedFromSchema()
    {
        // Validates that collision detection occurs in ExpectedArgumentNames collection
        // even when one of the colliding parameters is excluded from schema generation.
        // This addresses the concern that collisions might go undetected when
        // ExcludeFromSchema = true for one of the parameters.

        var options = new AIFunctionFactoryOptions
        {
            ConfigureParameterBinding = p => p.Name == "second"
                ? new AIFunctionFactoryOptions.ParameterBindingOptions { ExcludeFromSchema = true }
                : default
        };

        ArgumentException ex = Assert.Throws<ArgumentException>(() => AIFunctionFactory.Create(
            ([AIParameterName("dup")] string first, [AIParameterName("dup")] string second) => first + second,
            options));
        Assert.Contains("dup", ex.Message);
        Assert.Contains("AIParameterNameAttribute", ex.Message);
        Assert.Equal("method", ex.ParamName);
    }

    [Fact]
    public void AIFunctionFactory_InheritedDescriptionAttributes_OnOverride()
    {
        MethodInfo overrideMethod = typeof(DerivedDescribed).GetMethod(nameof(DerivedDescribed.Compute))!;
        AIFunction f = AIFunctionFactory.Create(overrideMethod, new DerivedDescribed());

        Assert.Equal("The compute method", f.Description);

        JsonElement valueParam = f.JsonSchema.GetProperty("properties").GetProperty("value");
        Assert.Equal("The input value", valueParam.GetProperty("description").GetString());

        Assert.NotNull(f.ReturnJsonSchema);
        Assert.Equal("integer", f.ReturnJsonSchema!.Value.GetProperty("type").GetString());
#if NET
        // On modern .NET the return-parameter inheritance walk is fixed, so the inherited description is read.
        Assert.Equal("The computed result", f.ReturnJsonSchema!.Value.GetProperty("description").GetString());
#else
        // On .NET Framework the inherited return-parameter description cannot be read and is silently dropped.
        Assert.False(f.ReturnJsonSchema!.Value.TryGetProperty("description", out _));
#endif
    }

    [Fact]
    public void AIFunctionFactoryCreateOptions_ValuesPropagateToAIFunction()
    {
        IReadOnlyDictionary<string, object?> metadata = new Dictionary<string, object?> { ["a"] = "b" };

        Func<ParameterInfo, AIFunctionFactoryOptions.ParameterBindingOptions> getBindParameterMode = _ => default;

        var options = new AIFunctionFactoryOptions
        {
            Name = "test name",
            Description = "test description",
            AdditionalProperties = metadata,
            ConfigureParameterBinding = getBindParameterMode,
        };

        Assert.Equal("test name", options.Name);
        Assert.Equal("test description", options.Description);
        Assert.Same(metadata, options.AdditionalProperties);
        Assert.Same(getBindParameterMode, options.ConfigureParameterBinding);

        Action dotnetFunc = () => { };
        AIFunction func = AIFunctionFactory.Create(dotnetFunc, options);

        Assert.Equal("test name", func.Name);
        Assert.Equal("test description", func.Description);
        Assert.Same(dotnetFunc.Method, func.UnderlyingMethod);
        Assert.Equal(metadata, func.AdditionalProperties);
    }

    [Fact]
    public void AIFunctionFactoryOptions_DefaultValues()
    {
        AIFunctionFactoryOptions options = new();

        Assert.Null(options.Name);
        Assert.Null(options.Description);
        Assert.Null(options.AdditionalProperties);
        Assert.Null(options.SerializerOptions);
        Assert.Null(options.JsonSchemaCreateOptions);
        Assert.Null(options.ConfigureParameterBinding);
        Assert.False(options.ExcludeResultSchema);
    }

    [Fact]
    public async Task AIFunctionFactoryOptions_SupportsSkippingParameters()
    {
        AIFunction func = AIFunctionFactory.Create(
            (string firstParameter, int secondParameter) => firstParameter + secondParameter,
            new()
            {
                ConfigureParameterBinding = p => p.Name == "firstParameter" ? new() { ExcludeFromSchema = true } : default,
            });

        Assert.DoesNotContain("firstParameter", func.JsonSchema.ToString());
        Assert.Contains("secondParameter", func.JsonSchema.ToString());

        Assert.Equal("""{"type":"string"}""", func.ReturnJsonSchema.ToString());

        var result = (JsonElement?)await func.InvokeAsync(new()
        {
            ["firstParameter"] = "test",
            ["secondParameter"] = 42
        });
        Assert.NotNull(result);
        Assert.Contains("test42", result.ToString());
    }

    [Fact]
    public void AIFunctionFactoryOptions_ExcludeFromSchema_HonoredUnderConcurrentCreation()
    {
        MethodInfo[] methods = typeof(ExcludeFromSchemaConcurrencyTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static);
        IExcludedService service = new ExcludedService();
        int leaks = 0;

        Parallel.For(0, 64, _ =>
        {
            foreach (MethodInfo method in methods)
            {
                AIFunctionFactoryOptions options = new()
                {
                    ConfigureParameterBinding = p => p.ParameterType == typeof(IExcludedService)
                        ? new AIFunctionFactoryOptions.ParameterBindingOptions
                        {
                            ExcludeFromSchema = true,
                            BindParameter = (_, _) => service,
                        }
                        : default,
                };

                JsonElement schema = AIFunctionFactory.Create(method, target: null, options).JsonSchema;

                bool inRequired = schema.TryGetProperty("required", out JsonElement required) &&
                    required.EnumerateArray().Any(e => e.GetString() == "service");
                bool inProperties = schema.TryGetProperty("properties", out JsonElement properties) &&
                    properties.TryGetProperty("service", out JsonElement _);
                if (inRequired || inProperties)
                {
                    Interlocked.Increment(ref leaks);
                }
            }
        });

        Assert.Equal(0, leaks);
    }

    private interface IExcludedService
    {
        string Value { get; }
    }

    private static class ExcludeFromSchemaConcurrencyTools
    {
        public static string One(IExcludedService service, [Description("q")] string query, [Description("o")] string? optional = null) => $"{service.Value}{query}{optional}";
        public static string Two(IExcludedService service, [Description("o")] string? optional = null) => $"{service.Value}{optional}";
        public static string Three(IExcludedService service, [Description("n")] string name) => $"{service.Value}{name}";
        public static string Four(IExcludedService service, [Description("n")] string name, [Description("a")] string? aspect = null) => $"{service.Value}{name}{aspect}";
        public static string Five(IExcludedService service, [Description("q")] string query, [Description("f")] string framework) => $"{service.Value}{query}{framework}";
        public static string Six(IExcludedService service, [Description("c")] string? category = null) => $"{service.Value}{category}";
        public static string Seven(IExcludedService service, [Description("t")] string? topic = null) => $"{service.Value}{topic}";
        public static string Eight(IExcludedService service) => service.Value;
    }

    private sealed class ExcludedService : IExcludedService
    {
        public string Value => "service";
    }

    [Fact]
    public void AIFunctionFactoryOptions_SupportsSkippingReturnSchema()
    {
        AIFunction func = AIFunctionFactory.Create(
            (string firstParameter, int secondParameter) => firstParameter + secondParameter,
            new()
            {
                ExcludeResultSchema = true,
            });

        Assert.Contains("firstParameter", func.JsonSchema.ToString());
        Assert.Contains("secondParameter", func.JsonSchema.ToString());
        Assert.Null(func.ReturnJsonSchema);
    }

    [Fact]
    public async Task AIFunctionArguments_SatisfiesParameters()
    {
        ServiceCollection sc = new();
        IServiceProvider sp = sc.BuildServiceProvider();

        AIFunctionArguments arguments = new() { ["myInteger"] = 42 };

        AIFunction func = AIFunctionFactory.Create((
            int myInteger,
            IServiceProvider services1,
            IServiceProvider services2,
            AIFunctionArguments arguments1,
            AIFunctionArguments arguments2,
            IServiceProvider? services3,
            AIFunctionArguments? arguments3,
            IServiceProvider? services4 = null,
            AIFunctionArguments? arguments4 = null) =>
        {
            Assert.Same(sp, services1);
            Assert.Same(sp, services2);
            Assert.Same(sp, services3);
            Assert.Same(sp, services4);

            Assert.Same(arguments, arguments1);
            Assert.Same(arguments, arguments2);
            Assert.Same(arguments, arguments3);
            Assert.Same(arguments, arguments4);

            return myInteger;
        });

        Assert.Contains("myInteger", func.JsonSchema.ToString());
        Assert.DoesNotContain("services", func.JsonSchema.ToString());
        Assert.DoesNotContain("arguments", func.JsonSchema.ToString());

        Assert.Equal("""{"type":"integer"}""", func.ReturnJsonSchema.ToString());

        await Assert.ThrowsAsync<ArgumentNullException>("arguments.Services", () => func.InvokeAsync(arguments).AsTask());

        arguments.Services = sp;
        var result = await func.InvokeAsync(arguments);

        Assert.Contains("42", result?.ToString());
    }

    [Fact]
    public async Task AIFunctionArguments_MissingServicesMayBeOptional()
    {
        ServiceCollection sc = new();
        IServiceProvider sp = sc.BuildServiceProvider();

        AIFunction func = AIFunctionFactory.Create((
            int? myInteger = null,
            AIFunctionArguments? arguments = null,
            IServiceProvider? services = null) =>
        {
            Assert.NotNull(arguments);
            Assert.Null(services);
            return myInteger;
        });

        Assert.Contains("myInteger", func.JsonSchema.ToString());
        Assert.DoesNotContain("services", func.JsonSchema.ToString());
        Assert.DoesNotContain("arguments", func.JsonSchema.ToString());

        var result = await func.InvokeAsync(new() { ["myInteger"] = 42 });
        Assert.Contains("42", result?.ToString());

        result = await func.InvokeAsync();
        Assert.Equal("", result?.ToString());
    }

    [Fact]
    public async Task Create_NoInstance_UsesActivatorUtilitiesWhenServicesAvailable()
    {
        MyFunctionTypeWithOneArg mft = new(new());
        MyArgumentType mat = new();

        ServiceCollection sc = new();
        sc.AddSingleton(mft);
        sc.AddSingleton(mat);
        IServiceProvider sp = sc.BuildServiceProvider();

        AIFunction func = AIFunctionFactory.Create(
            typeof(MyFunctionTypeWithOneArg).GetMethod(nameof(MyFunctionTypeWithOneArg.InstanceMethod))!,
            static arguments =>
            {
                Assert.NotNull(arguments.Services);
                return ActivatorUtilities.CreateInstance(arguments.Services, typeof(MyFunctionTypeWithOneArg));
            },
            new() { MarshalResult = (result, type, cancellationToken) => new ValueTask<object?>(result) });

        Assert.NotNull(func);
        var result = (Tuple<MyFunctionTypeWithOneArg, MyArgumentType>?)await func.InvokeAsync(new() { Services = sp });
        Assert.NotSame(mft, result?.Item1);
        Assert.Same(mat, result?.Item2);
    }

    [Fact]
    public async Task Create_CreateInstanceReturnsNull_ThrowsDuringInvocation()
    {
        AIFunction func = AIFunctionFactory.Create(
            typeof(MyFunctionTypeWithOneArg).GetMethod(nameof(MyFunctionTypeWithOneArg.InstanceMethod))!,
            static _ => null!);

        Assert.NotNull(func);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await func.InvokeAsync());
    }

    [Fact]
    public async Task Create_WrongConstructedType_ThrowsDuringInvocation()
    {
        AIFunction func = AIFunctionFactory.Create(
            typeof(MyFunctionTypeWithOneArg).GetMethod(nameof(MyFunctionTypeWithOneArg.InstanceMethod))!,
            static _ => new MyFunctionTypeWithNoArgs());

        Assert.NotNull(func);
        await Assert.ThrowsAsync<TargetException>(async () => await func.InvokeAsync());
    }

    [Fact]
    public void Create_NoInstance_ThrowsForStaticMethod()
    {
        Assert.Throws<ArgumentException>("method", () => AIFunctionFactory.Create(
            typeof(MyFunctionTypeWithNoArgs).GetMethod(nameof(MyFunctionTypeWithNoArgs.StaticMethod))!,
            static _ => new MyFunctionTypeWithNoArgs()));
    }

    [Fact]
    public async Task Create_NoInstance_DisposableInstanceCreatedDisposedEachInvocation()
    {
        AIFunction func = AIFunctionFactory.Create(
            typeof(DisposableService).GetMethod(nameof(DisposableService.GetThis))!,
            static _ => new DisposableService(),
            new()
            {
                MarshalResult = (result, type, cancellationToken) => new ValueTask<object?>(result),
            });

        var d1 = Assert.IsType<DisposableService>(await func.InvokeAsync());
        var d2 = Assert.IsType<DisposableService>(await func.InvokeAsync());
        Assert.NotSame(d1, d2);

        Assert.Equal(1, d1.Disposals);
        Assert.Equal(1, d2.Disposals);
    }

    [Fact]
    public async Task Create_NoInstance_AsyncDisposableInstanceCreatedDisposedEachInvocation()
    {
        AIFunction func = AIFunctionFactory.Create(
            typeof(AsyncDisposableService).GetMethod(nameof(AsyncDisposableService.GetThis))!,
            static _ => new AsyncDisposableService(),
            new()
            {
                MarshalResult = (result, type, cancellationToken) => new ValueTask<object?>(result),
            });

        var d1 = Assert.IsType<AsyncDisposableService>(await func.InvokeAsync());
        var d2 = Assert.IsType<AsyncDisposableService>(await func.InvokeAsync());
        Assert.NotSame(d1, d2);

        Assert.Equal(1, d1.AsyncDisposals);
        Assert.Equal(1, d2.AsyncDisposals);
    }

    [Fact]
    public async Task Create_NoInstance_DisposableAndAsyncDisposableInstanceCreatedDisposedEachInvocation()
    {
        AIFunction func = AIFunctionFactory.Create(
            typeof(DisposableAndAsyncDisposableService).GetMethod(nameof(DisposableAndAsyncDisposableService.GetThis))!,
            static _ => new DisposableAndAsyncDisposableService(),
            new()
            {
                MarshalResult = (result, type, cancellationToken) => new ValueTask<object?>(result),
            });

        var d1 = Assert.IsType<DisposableAndAsyncDisposableService>(await func.InvokeAsync());
        var d2 = Assert.IsType<DisposableAndAsyncDisposableService>(await func.InvokeAsync());
        Assert.NotSame(d1, d2);

        Assert.Equal(0, d1.Disposals);
        Assert.Equal(0, d2.Disposals);
        Assert.Equal(1, d1.AsyncDisposals);
        Assert.Equal(1, d2.AsyncDisposals);
    }

    [Fact]
    public async Task FromKeyedServices_ResolvesFromServiceProvider()
    {
        MyService service = new(42);

        ServiceCollection sc = new();
        sc.AddKeyedSingleton("key", service);
        IServiceProvider sp = sc.BuildServiceProvider();

        AIFunction f = AIFunctionFactory.Create(([FromKeyedServices("key")] MyService service, int myInteger) => service.Value + myInteger,
            CreateKeyedServicesSupportOptions());

        Assert.Contains("myInteger", f.JsonSchema.ToString());
        Assert.DoesNotContain("service", f.JsonSchema.ToString());

        Assert.Equal("""{"type":"integer"}""", f.ReturnJsonSchema.ToString());

        Exception e = await Assert.ThrowsAsync<ArgumentException>("arguments.Services", () => f.InvokeAsync(new() { ["myInteger"] = 1 }).AsTask());

        var result = await f.InvokeAsync(new() { ["myInteger"] = 1, Services = sp });
        Assert.Contains("43", result?.ToString());
    }

    [Fact]
    public async Task FromKeyedServices_NullKeysBindToNonKeyedServices()
    {
        MyService service = new(42);

        ServiceCollection sc = new();
        sc.AddSingleton(service);
        IServiceProvider sp = sc.BuildServiceProvider();

        AIFunction f = AIFunctionFactory.Create(([FromKeyedServices(null!)] MyService service, int myInteger) => service.Value + myInteger,
            CreateKeyedServicesSupportOptions());

        Assert.Contains("myInteger", f.JsonSchema.ToString());
        Assert.DoesNotContain("service", f.JsonSchema.ToString());

        Assert.Equal("""{"type":"integer"}""", f.ReturnJsonSchema.ToString());

        Exception e = await Assert.ThrowsAsync<ArgumentException>("arguments.Services", () => f.InvokeAsync(new() { ["myInteger"] = 1 }).AsTask());

        var result = await f.InvokeAsync(new() { ["myInteger"] = 1, Services = sp });
        Assert.Contains("43", result?.ToString());
    }

    [Fact]
    public async Task FromKeyedServices_OptionalDefaultsToNull()
    {
        MyService service = new(42);

        ServiceCollection sc = new();
        sc.AddKeyedSingleton("key", service);
        IServiceProvider sp = sc.BuildServiceProvider();

        AIFunction f = AIFunctionFactory.Create(([FromKeyedServices("key")] MyService? service = null, int myInteger = 0) =>
            service is null ? "null " + 1 : (service.Value + myInteger).ToString(),
            CreateKeyedServicesSupportOptions());

        Assert.Contains("myInteger", f.JsonSchema.ToString());
        Assert.DoesNotContain("service", f.JsonSchema.ToString());

        var result = await f.InvokeAsync(new() { ["myInteger"] = 1 });
        Assert.Contains("null 1", result?.ToString());

        result = await f.InvokeAsync(new() { ["myInteger"] = 1, Services = sp });
        Assert.Contains("43", result?.ToString());
    }

    [Fact]
    public async Task ConfigureParameterBinding_CanBeUsedToSupportFromContext()
    {
        MyService service = new(42);

        AIFunction f = AIFunctionFactory.Create(
            (MyService service, int myInteger) => service.Value + myInteger,
            new AIFunctionFactoryOptions
            {
                ConfigureParameterBinding = p =>
                {
                    if (p.ParameterType == typeof(MyService))
                    {
                        return new()
                        {
                            BindParameter = (p, a) =>
                                a.Context?.TryGetValue(typeof(MyService), out object? service) is true ? service :
                                p.HasDefaultValue ? p.DefaultValue :
                                throw new ArgumentException($"Unable to resolve argument for '{p.Name}'."),
                            ExcludeFromSchema = true
                        };
                    }

                    return default;
                }
            });

        Assert.Contains("myInteger", f.JsonSchema.ToString());
        Assert.DoesNotContain("service", f.JsonSchema.ToString());

        Exception e = await Assert.ThrowsAsync<ArgumentException>(() => f.InvokeAsync(new() { ["myInteger"] = 1 }).AsTask());
        Assert.Contains("Unable to resolve", e.Message);

        e = await Assert.ThrowsAsync<ArgumentException>(() => f.InvokeAsync(new()
        {
            ["myInteger"] = 1,
            Context = new Dictionary<object, object?>(),
        }).AsTask());
        Assert.Contains("Unable to resolve", e.Message);

        var result = await f.InvokeAsync(new()
        {
            ["myInteger"] = 1,
            Context = new Dictionary<object, object?>
            {
                [typeof(MyService)] = service
            },
        });
        Assert.Contains("43", result?.ToString());
    }

    [Fact]
    public async Task ConfigureParameterBinding_CanBeUsedToOverrideServiceProvider()
    {
        IServiceProvider sp1 = new ServiceCollection().AddSingleton(new MyService(42)).BuildServiceProvider();
        IServiceProvider sp2 = new ServiceCollection().AddSingleton(new MyService(43)).BuildServiceProvider();

        AIFunction f = AIFunctionFactory.Create(
            (IServiceProvider services) => services.GetRequiredService<MyService>().Value,
            new AIFunctionFactoryOptions
            {
                ConfigureParameterBinding = p => new() { BindParameter = (p, a) => sp2 },
            });

        var result = await f.InvokeAsync(new() { Services = sp1 });
        Assert.Contains("43", result?.ToString());
    }

    [Fact]
    public async Task ConfigureParameterBinding_CanBeUsedToOverrideAIFunctionArguments()
    {
        AIFunctionArguments args1 = new() { ["a"] = 42 };
        AIFunctionArguments args2 = new() { ["a"] = 43 };

        AIFunction f = AIFunctionFactory.Create(
            (AIFunctionArguments args) => (int)args["a"]!,
            new AIFunctionFactoryOptions
            {
                ConfigureParameterBinding = p => new() { BindParameter = (p, a) => args2 },
            });

        var result = await f.InvokeAsync(args1);
        Assert.Contains("43", result?.ToString());
    }

    [Fact]
    public async Task MarshalResult_UsedForVoidReturningMethods()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            (int i) => { },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Null(result);
                    Assert.Null(type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_UsedForTaskReturningMethods()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            async (int i) => { await Task.Yield(); },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Null(result);
                    Assert.Null(type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_UsedForValueTaskReturningMethods()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            async ValueTask (int i) => { await Task.Yield(); },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Null(result);
                    Assert.Null(type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_UsedForTReturningMethods()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            (int i) => i,
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Equal(42, result);
                    Assert.Equal(typeof(int), type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_UsedForTaskTReturningMethods()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            async (int i) => { await Task.Yield(); return i; },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Equal(42, result);
                    Assert.Equal(typeof(int), type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_UsedForValueTaskTReturningMethods()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            async ValueTask<int> (int i) => { await Task.Yield(); return i; },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Equal(42, result);
                    Assert.Equal(typeof(int), type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_TypeIsDeclaredTypeEvenWhenNullReturned()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            async ValueTask<string?> (int i) => { await Task.Yield(); return null; },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Null(result);
                    Assert.Equal(typeof(string), type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task MarshalResult_TypeIsDeclaredTypeEvenWhenDerivedTypeReturned()
    {
        using CancellationTokenSource cts = new();

        AIFunction f = AIFunctionFactory.Create(
            async ValueTask<B> (int i) => { await Task.Yield(); return new C(); },
            new()
            {
                MarshalResult = async (result, type, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.IsType<C>(result);
                    Assert.Equal(typeof(B), type);
                    Assert.Equal(cts.Token, cancellationToken);
                    return "marshalResultInvoked";
                },
                SerializerOptions = JsonContext.Default.Options,
            });

        object? result = await f.InvokeAsync(new() { ["i"] = 42 }, cts.Token);
        Assert.Equal("marshalResultInvoked", result);
    }

    [Fact]
    public async Task AIContentReturnType_NotSerializedByDefault()
    {
        await ValidateAsync<TextContent>(
        [
            AIFunctionFactory.Create(() => (AIContent)new TextContent("text")),
            AIFunctionFactory.Create(async () => (AIContent)new TextContent("text")),
            AIFunctionFactory.Create(async ValueTask<AIContent> () => (AIContent)new TextContent("text")),
            AIFunctionFactory.Create(() => new TextContent("text")),
            AIFunctionFactory.Create(async () => new TextContent("text")),
            AIFunctionFactory.Create(async ValueTask<AIContent> () => new TextContent("text")),
        ]);

        await ValidateAsync<DataContent>(
        [
            AIFunctionFactory.Create(() => new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")),
            AIFunctionFactory.Create(async () => new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")),
            AIFunctionFactory.Create(async ValueTask<DataContent> () => new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")),
        ]);

        await ValidateAsync<IEnumerable<AIContent>>(
        [
            AIFunctionFactory.Create(() => (IEnumerable<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async () => (IEnumerable<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async ValueTask<IEnumerable<AIContent>> () => (IEnumerable<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
        ]);

        await ValidateAsync<AIContent[]>(
        [
            AIFunctionFactory.Create(() => (AIContent[])[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async () => (AIContent[])[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async ValueTask<AIContent[]> () => (AIContent[])[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
        ]);

        await ValidateAsync<List<AIContent>>(
        [
            AIFunctionFactory.Create(() => (List<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async () => (List<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async ValueTask<List<AIContent>> () => (List<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
        ]);

        await ValidateAsync<IEnumerable<AIContent>>(
        [
            AIFunctionFactory.Create(() => (IList<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async () => (IList<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
            AIFunctionFactory.Create(async ValueTask<IList<AIContent>> () => (List<AIContent>)[new TextContent("text"), new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]),
        ]);

        static async Task ValidateAsync<T>(IEnumerable<AIFunction> functions)
        {
            foreach (var f in functions)
            {
                Assert.IsAssignableFrom<T>(await f.InvokeAsync());
            }
        }
    }

    [Fact]
    public async Task AIFunctionFactory_DefaultDefaultParameter()
    {
        Assert.NotEqual(new StructWithDefaultCtor().Value, default(StructWithDefaultCtor).Value);

        AIFunction f = AIFunctionFactory.Create((Guid g = default, StructWithDefaultCtor s = default) => g.ToString() + "," + s.Value.ToString(), serializerOptions: JsonContext.Default.Options);

        object? result = await f.InvokeAsync();
        Assert.Contains("00000000-0000-0000-0000-000000000000,0", result?.ToString());
    }

    [Fact]
    public async Task AIFunctionFactory_NullableParameters()
    {
        Assert.NotEqual(new StructWithDefaultCtor().Value, default(StructWithDefaultCtor).Value);

        AIFunction f = AIFunctionFactory.Create(
            (int? limit = null, DateTime? from = null) => Enumerable.Repeat(from ?? default, limit ?? 4).Select(d => d.Year).ToArray(),
            serializerOptions: JsonContext.Default.Options);

        JsonElement expectedSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "limit": {
                    "type": ["integer", "null"],
                    "default": null
                },
                "from": {
                    "type": ["string", "null"],
                    "format": "date-time",
                    "default": null
                }
            }
        }
        """).RootElement;

        AssertExtensions.EqualJsonValues(expectedSchema, f.JsonSchema);

        object? result = await f.InvokeAsync();
        Assert.Contains("[1,1,1,1]", result?.ToString());
    }

    [Fact]
    public async Task AIFunctionFactory_NullableParameters_AllowReadingFromString()
    {
        JsonSerializerOptions options = new(JsonContext.Default.Options) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        Assert.NotEqual(new StructWithDefaultCtor().Value, default(StructWithDefaultCtor).Value);

        AIFunction f = AIFunctionFactory.Create(
            (int? limit = null, DateTime? from = null) => Enumerable.Repeat(from ?? default, limit ?? 4).Select(d => d.Year).ToArray(),
            serializerOptions: options);

        JsonElement expectedSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "limit": {
                    "type": ["integer", "null"],
                    "default": null
                },
                "from": {
                    "type": ["string", "null"],
                    "format": "date-time",
                    "default": null
                }
            }
        }
        """).RootElement;

        AssertExtensions.EqualJsonValues(expectedSchema, f.JsonSchema);

        object? result = await f.InvokeAsync();
        Assert.Contains("[1,1,1,1]", result?.ToString());
    }

    [Fact]
    public void AIFunctionFactory_ReturnTypeWithDescriptionAttribute()
    {
        AIFunction f = AIFunctionFactory.Create(Add, serializerOptions: JsonContext.Default.Options);

        Assert.Equal("""{"description":"The summed result","type":"integer"}""", f.ReturnJsonSchema.ToString());

        [return: Description("The summed result")]
        static int Add(int a, int b) => a + b;
    }

    [Fact]
    public void CreateDeclaration_Roundtrips()
    {
        JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(int), serializerOptions: AIJsonUtilities.DefaultOptions);

        AIFunctionDeclaration f = AIFunctionFactory.CreateDeclaration("something", "amazing", schema);
        Assert.Equal("something", f.Name);
        Assert.Equal("amazing", f.Description);
        Assert.Equal("""{"type":"integer"}""", f.JsonSchema.ToString());
        Assert.Null(f.ReturnJsonSchema);

        f = AIFunctionFactory.CreateDeclaration("other", null, default, schema);
        Assert.Equal("other", f.Name);
        Assert.Empty(f.Description);
        Assert.Equal(default, f.JsonSchema);
        Assert.Equal("""{"type":"integer"}""", f.ReturnJsonSchema.ToString());

        Assert.Throws<ArgumentNullException>("name", () => AIFunctionFactory.CreateDeclaration(null!, "description", default));
    }

    private sealed class MyService(int value)
    {
        public int Value => value;
    }

    private class DisposableService : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;

        public object GetThis() => this;
    }

    private class AsyncDisposableService : IAsyncDisposable
    {
        public int AsyncDisposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            return default;
        }

        public object GetThis() => this;
    }

    private class DisposableAndAsyncDisposableService : IDisposable, IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public int AsyncDisposals { get; private set; }

        public void Dispose() => Disposals++;

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            return default;
        }

        public object GetThis() => this;
    }

    private sealed class MyFunctionTypeWithNoArgs
    {
        public static void StaticMethod() => throw new NotSupportedException();
    }

    private sealed class MyFunctionTypeWithOneArg(MyArgumentType arg)
    {
        public object InstanceMethod() => Tuple.Create(this, arg);
    }

    private sealed class MyArgumentType;

    private static int TestStaticMethod(int a, int b) => a + b;

    private class A;
    private class B : A;
    private sealed class C : B;

    public readonly struct StructWithDefaultCtor
    {
        public int Value { get; }
        public StructWithDefaultCtor()
        {
            Value = 42;
        }
    }

    private static AIFunctionFactoryOptions CreateKeyedServicesSupportOptions() =>
        new AIFunctionFactoryOptions
        {
            ConfigureParameterBinding = p =>
            {
                if (p.GetCustomAttribute<FromKeyedServicesAttribute>() is { } attr)
                {
                    return new()
                    {
                        BindParameter = (p, a) =>
                            (a.Services as IKeyedServiceProvider)?.GetKeyedService(p.ParameterType, attr.Key) is { } s ? s :
                            p.HasDefaultValue ? p.DefaultValue :
                            throw new ArgumentException($"Unable to resolve argument for '{p.Name}'.", "arguments.Services"),
                        ExcludeFromSchema = true
                    };
                }

                return default;
            },
        };

    [Fact]
    public void LocalFunction_NameCleanup()
    {
        static void DoSomething()
        {
            // Empty local function for testing name cleanup
        }

        var tool = AIFunctionFactory.Create(DoSomething);

        // The name should start with: ContainingMethodName_LocalFunctionName (followed by ordinal)
        Assert.StartsWith("LocalFunction_NameCleanup_DoSomething_", tool.Name);
    }

    [Fact]
    public void LocalFunction_MultipleInSameMethod()
    {
        static void FirstLocal()
        {
            // Empty local function for testing name cleanup
        }

        static void SecondLocal()
        {
            // Empty local function for testing name cleanup
        }

        var tool1 = AIFunctionFactory.Create(FirstLocal);
        var tool2 = AIFunctionFactory.Create(SecondLocal);

        // Each should have unique names based on the local function name (including ordinal)
        Assert.StartsWith("LocalFunction_MultipleInSameMethod_FirstLocal_", tool1.Name);
        Assert.StartsWith("LocalFunction_MultipleInSameMethod_SecondLocal_", tool2.Name);
        Assert.NotEqual(tool1.Name, tool2.Name);
    }

    [Fact]
    public void Lambda_NameCleanup()
    {
        Action lambda = () =>
        {
            // Empty lambda for testing name cleanup
        };

        var tool = AIFunctionFactory.Create(lambda);

        // The name should be the containing method name with ordinal for uniqueness
        Assert.StartsWith("Lambda_NameCleanup", tool.Name);
    }

    [Fact]
    public void Lambda_MultipleInSameMethod()
    {
        Action lambda1 = () =>
        {
            // Empty lambda for testing name cleanup
        };

        Action lambda2 = () =>
        {
            // Empty lambda for testing name cleanup
        };

        var tool1 = AIFunctionFactory.Create(lambda1);
        var tool2 = AIFunctionFactory.Create(lambda2);

        // Each lambda should have a unique name based on its ordinal
        // to allow the LLM to distinguish between them
        Assert.StartsWith("Lambda_MultipleInSameMethod", tool1.Name);
        Assert.StartsWith("Lambda_MultipleInSameMethod", tool2.Name);
        Assert.NotEqual(tool1.Name, tool2.Name);
    }

    [Fact]
    public void LocalFunction_WithParameters()
    {
        static int Add(int firstNumber, int secondNumber) => firstNumber + secondNumber;

        var tool = AIFunctionFactory.Create(Add);

        Assert.StartsWith("LocalFunction_WithParameters_Add_", tool.Name);
        Assert.Contains("firstNumber", tool.JsonSchema.ToString());
        Assert.Contains("secondNumber", tool.JsonSchema.ToString());
    }

    [Fact]
    public async Task LocalFunction_AsyncFunction()
    {
        static async Task<string> FetchDataAsync()
        {
            await Task.Yield();
            return "data";
        }

        var tool = AIFunctionFactory.Create(FetchDataAsync);

        // Should strip "Async" suffix and include ordinal
        Assert.StartsWith("LocalFunction_AsyncFunction_FetchData_", tool.Name);

        var result = await tool.InvokeAsync();
        AssertExtensions.EqualFunctionCallResults("data", result);
    }

    [Fact]
    public void LocalFunction_ExplicitNameOverride()
    {
        static void DoSomething()
        {
            // Empty local function for testing name cleanup
        }

        var tool = AIFunctionFactory.Create(DoSomething, name: "CustomName");

        Assert.Equal("CustomName", tool.Name);
    }

    [Fact]
    public void LocalFunction_InsideTestMethod()
    {
        // Even local functions defined in test methods get cleaned up
        var tool = AIFunctionFactory.Create(Add, serializerOptions: JsonContext.Default.Options);

        Assert.StartsWith("LocalFunction_InsideTestMethod_Add_", tool.Name);

        [return: Description("The summed result")]
        static int Add(int a, int b) => a + b;
    }

    [Fact]
    public void RegularStaticMethod_NameUnchanged()
    {
        // Test that actual static methods (not local functions) have names unchanged
        var tool = AIFunctionFactory.Create(TestStaticMethod, null, serializerOptions: JsonContext.Default.Options);

        Assert.Equal("TestStaticMethod", tool.Name);
    }

    [Fact]
    public void JsonSchema_NullableValueTypeParameters_AllowNull()
    {
        // Test that nullable value type parameters (e.g., int?) generate JSON schemas that allow null values.
        // This should work on all target frameworks.
        AIFunction func = AIFunctionFactory.Create(
            (int? nullableInt, int? nullableIntWithDefault = null) => { });

        JsonElement schema = func.JsonSchema;
        JsonElement properties = schema.GetProperty("properties");

        // nullableInt should have type ["integer", "null"]
        JsonElement nullableIntSchema = properties.GetProperty("nullableInt");
        Assert.True(
            nullableIntSchema.TryGetProperty("type", out JsonElement nullableIntType),
            "nullableInt schema should have a 'type' property");
        Assert.Equal(JsonValueKind.Array, nullableIntType.ValueKind);
        Assert.Contains("integer", nullableIntType.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("null", nullableIntType.EnumerateArray().Select(e => e.GetString()));

        // nullableIntWithDefault should have type ["integer", "null"] and default: null
        JsonElement nullableIntWithDefaultSchema = properties.GetProperty("nullableIntWithDefault");
        Assert.True(
            nullableIntWithDefaultSchema.TryGetProperty("type", out JsonElement nullableIntWithDefaultType),
            "nullableIntWithDefault schema should have a 'type' property");
        Assert.Equal(JsonValueKind.Array, nullableIntWithDefaultType.ValueKind);
        Assert.Contains("integer", nullableIntWithDefaultType.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("null", nullableIntWithDefaultType.EnumerateArray().Select(e => e.GetString()));
        Assert.True(
            nullableIntWithDefaultSchema.TryGetProperty("default", out JsonElement nullableIntWithDefaultDefault),
            "nullableIntWithDefault schema should have a 'default' property");
        Assert.Equal(JsonValueKind.Null, nullableIntWithDefaultDefault.ValueKind);

        // Required array should contain only parameters without default values
        JsonElement required = schema.GetProperty("required");
        List<string> requiredParams = required.EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains("nullableInt", requiredParams);
        Assert.DoesNotContain("nullableIntWithDefault", requiredParams);
    }

    [Fact]
    public void JsonSchema_NullableReferenceTypeParameters_AllowNull()
    {
        // Regression test for https://github.com/dotnet/extensions/issues/7182
        // Nullable reference type parameters (e.g., string?) should generate JSON schemas that allow null values.
        AIFunction func = AIFunctionFactory.Create(
            (string? nullableString, int? nullableInt, string? nullableStringWithDefault = null, int? nullableIntWithDefault = null) => { });

        JsonElement schema = func.JsonSchema;
        JsonElement properties = schema.GetProperty("properties");

        // nullableString should have type ["string", "null"]
        JsonElement nullableStringSchema = properties.GetProperty("nullableString");
        Assert.True(
            nullableStringSchema.TryGetProperty("type", out JsonElement nullableStringType),
            "nullableString schema should have a 'type' property");
        Assert.Equal(JsonValueKind.Array, nullableStringType.ValueKind);
        Assert.Contains("string", nullableStringType.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("null", nullableStringType.EnumerateArray().Select(e => e.GetString()));

        // nullableInt should have type ["integer", "null"]
        JsonElement nullableIntSchema = properties.GetProperty("nullableInt");
        Assert.True(
            nullableIntSchema.TryGetProperty("type", out JsonElement nullableIntType),
            "nullableInt schema should have a 'type' property");
        Assert.Equal(JsonValueKind.Array, nullableIntType.ValueKind);
        Assert.Contains("integer", nullableIntType.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("null", nullableIntType.EnumerateArray().Select(e => e.GetString()));

        // nullableStringWithDefault should have type ["string", "null"] and default: null
        JsonElement nullableStringWithDefaultSchema = properties.GetProperty("nullableStringWithDefault");
        Assert.True(
            nullableStringWithDefaultSchema.TryGetProperty("type", out JsonElement nullableStringWithDefaultType),
            "nullableStringWithDefault schema should have a 'type' property");
        Assert.Equal(JsonValueKind.Array, nullableStringWithDefaultType.ValueKind);
        Assert.Contains("string", nullableStringWithDefaultType.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("null", nullableStringWithDefaultType.EnumerateArray().Select(e => e.GetString()));
        Assert.True(
            nullableStringWithDefaultSchema.TryGetProperty("default", out JsonElement nullableStringWithDefaultDefault),
            "nullableStringWithDefault schema should have a 'default' property");
        Assert.Equal(JsonValueKind.Null, nullableStringWithDefaultDefault.ValueKind);

        // nullableIntWithDefault should have type ["integer", "null"] and default: null
        JsonElement nullableIntWithDefaultSchema = properties.GetProperty("nullableIntWithDefault");
        Assert.True(
            nullableIntWithDefaultSchema.TryGetProperty("type", out JsonElement nullableIntWithDefaultType),
            "nullableIntWithDefault schema should have a 'type' property");
        Assert.Equal(JsonValueKind.Array, nullableIntWithDefaultType.ValueKind);
        Assert.Contains("integer", nullableIntWithDefaultType.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("null", nullableIntWithDefaultType.EnumerateArray().Select(e => e.GetString()));
        Assert.True(
            nullableIntWithDefaultSchema.TryGetProperty("default", out JsonElement nullableIntWithDefaultDefault),
            "nullableIntWithDefault schema should have a 'default' property");
        Assert.Equal(JsonValueKind.Null, nullableIntWithDefaultDefault.ValueKind);

        // Required array should contain only parameters without default values
        JsonElement required = schema.GetProperty("required");
        List<string> requiredParams = required.EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains("nullableString", requiredParams);
        Assert.Contains("nullableInt", requiredParams);
        Assert.DoesNotContain("nullableStringWithDefault", requiredParams);
        Assert.DoesNotContain("nullableIntWithDefault", requiredParams);
    }

    [Fact]
    public async Task AIFunctionFactory_DynamicMethod()
    {
        DynamicMethod dynamicMethod = new DynamicMethod(
            "DoubleIt",
            typeof(Task<int>),
            new[] { typeof(int) },
            typeof(AIFunctionFactoryTest).Module);

        dynamicMethod.DefineParameter(1, ParameterAttributes.None, "value");

        ILGenerator il = dynamicMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(int)));
        il.Emit(OpCodes.Ret);

        Delegate testDelegate = dynamicMethod.CreateDelegate(typeof(Func<int, Task<int>>));

        AIFunction func = AIFunctionFactory.Create(testDelegate.GetMethodInfo(), testDelegate.Target);

        Assert.Equal("DoubleIt", func.Name);

        JsonElement schema = func.JsonSchema;
        JsonElement properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("value", out _));

#if NET
        // DynamicMethod invocation via MethodInfo.Invoke is not supported on .NET Framework.
        object? result = await func.InvokeAsync(new() { ["value"] = 21 });
        Assert.IsType<JsonElement>(result);
        Assert.Equal(42, ((JsonElement)result!).GetInt32());
#endif
    }

    [Fact]
    public async Task Parameters_UnmappedMemberHandlingDisallow_ThrowsOnExtraArgument_Async()
    {
        JsonSerializerOptions strictOptions = new(AIJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        AIFunction func = AIFunctionFactory.Create(
            (string taskId, string update, bool markComplete = false) => $"{taskId}:{update}:{markComplete}",
            new AIFunctionFactoryOptions { SerializerOptions = strictOptions });

        // Extra, unrecognized argument causes a throw.
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>("arguments", async () =>
            await func.InvokeAsync(new()
            {
                ["taskId"] = "abc",
                ["update"] = "Done",
                ["phase"] = "completed",
            }));
        Assert.Contains("phase", ex.Message);

        // Still succeeds when no unexpected arguments are present (optional parameter omitted).
        object? result = await func.InvokeAsync(new()
        {
            ["taskId"] = "abc",
            ["update"] = "Done",
        });
        AssertExtensions.EqualFunctionCallResults("abc:Done:False", result);
    }

    [Fact]
    public async Task Parameters_UnmappedMemberHandlingDefault_IgnoresExtraArgument_Async()
    {
        // Default behavior (Skip) should preserve pre-existing lenient binding.
        AIFunction func = AIFunctionFactory.Create(
            (string update, bool markComplete = false) => $"{update}:{markComplete}");

        object? result = await func.InvokeAsync(new()
        {
            ["update"] = "Done",
            ["phase"] = "completed",
        });
        AssertExtensions.EqualFunctionCallResults("Done:False", result);
    }

    [Fact]
    public async Task Parameters_UnmappedMemberHandlingDisallow_HonorsArgumentsComparer_Async()
    {
        JsonSerializerOptions strictOptions = new(AIJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        AIFunction func = AIFunctionFactory.Create(
            (string update, bool markComplete = false) => $"{update}:{markComplete}",
            new AIFunctionFactoryOptions { SerializerOptions = strictOptions });

        // Case-insensitive arguments dictionary: casing variations of the parameter name must not be
        // flagged as unmapped, since the binding lookup itself is case-insensitive.
        AIFunctionArguments caseInsensitive = new(StringComparer.OrdinalIgnoreCase)
        {
            ["UPDATE"] = "Done",
            ["MarkComplete"] = true,
        };
        AssertExtensions.EqualFunctionCallResults("Done:True", await func.InvokeAsync(caseInsensitive));

        // A genuinely unmapped key is still flagged even with a case-insensitive comparer.
        AIFunctionArguments withExtra = new(StringComparer.OrdinalIgnoreCase)
        {
            ["update"] = "Done",
            ["PHASE"] = "completed",
        };
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>("arguments", async () =>
            await func.InvokeAsync(withExtra));
        Assert.Contains("PHASE", ex.Message);
    }

    [Fact]
    public async Task Parameters_UnmappedMemberHandlingDisallow_ParameterlessMethod_ThrowsOnAnyArgument_Async()
    {
        JsonSerializerOptions strictOptions = new(AIJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        AIFunction func = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions { SerializerOptions = strictOptions });

        // No args is fine.
        AssertExtensions.EqualFunctionCallResults("ok", await func.InvokeAsync());

        // Any extra key is flagged.
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>("arguments", async () =>
            await func.InvokeAsync(new() { ["phase"] = "completed" }));
        Assert.Contains("phase", ex.Message);
    }

    [Fact]
    public async Task Parameters_UnmappedMemberHandlingDisallow_CustomBindParameter_SkipsStrictValidation_Async()
    {
        JsonSerializerOptions strictOptions = new(AIJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        // A custom BindParameter callback sources its value from a key that does not correspond
        // to the .NET parameter name. Strict validation must be skipped so such binders keep working.
        AIFunction func = AIFunctionFactory.Create(
            (string update) => $"update:{update}",
            new AIFunctionFactoryOptions
            {
                SerializerOptions = strictOptions,
                ConfigureParameterBinding = _ => new()
                {
                    BindParameter = (_, args) => args["aliasedKey"],
                },
            });

        object? result = await func.InvokeAsync(new()
        {
            ["aliasedKey"] = "hello",
            ["anotherKey"] = "world",
        });
        AssertExtensions.EqualFunctionCallResults("update:hello", result);
    }

    [JsonSerializable(typeof(IAsyncEnumerable<int>))]
    [JsonSerializable(typeof(int[]))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(StructWithDefaultCtor))]
    [JsonSerializable(typeof(B))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(DateTime?))]
    private partial class JsonContext : JsonSerializerContext;

    private abstract class MyBaseType
    {
        public abstract string Method([AIParameterName("my_param")] string myParam);
    }

    private sealed class MyDerivedType : MyBaseType
    {
        public override string Method(string myParam) => $"param='{myParam}'";
    }

    private sealed class AIParameterNameAttributeRecursiveNode
    {
        public AIParameterNameAttributeRecursiveNode? Next { get; set; }
    }

    private abstract class BaseDescribed
    {
        [Description("The compute method")]
        [return: Description("The computed result")]
        public abstract int Compute([Description("The input value")] int value);
    }

    private sealed class DerivedDescribed : BaseDescribed
    {
        public override int Compute(int value) => value;
    }
}
