// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using Microsoft.Extensions.AI.JsonSchemaExporter;
using Xunit;

#pragma warning disable SA1114 // parameter list should follow declaration
#pragma warning disable S1144 // Unused private types or members should be removed (members are used via reflection in schema tests)

namespace Microsoft.Extensions.AI;

public static partial class AIJsonUtilitiesTests
{
    [Fact]
    public static void DefaultOptions_HasExpectedConfiguration()
    {
        var options = AIJsonUtilities.DefaultOptions;

        // Must be read-only singleton.
        Assert.NotNull(options);
        Assert.Same(options, AIJsonUtilities.DefaultOptions);
        Assert.True(options.IsReadOnly);

        // Must conform to JsonSerializerDefaults.Web
        Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNumberHandling.AllowReadingFromString, options.NumberHandling);

        // Additional settings
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
        Assert.True(options.WriteIndented);
        Assert.Same(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, options.Encoder);
    }

    [Theory]
    [InlineData("<script>alert('XSS')</script>", "<script>alert('XSS')</script>")]
    [InlineData("""{"forecast":"sunny", "temperature":"75"}""", """{\"forecast\":\"sunny\", \"temperature\":\"75\"}""")]
    [InlineData("""{"message":"Πάντα ῥεῖ."}""", """{\"message\":\"Πάντα ῥεῖ.\"}""")]
    [InlineData("""{"message":"七転び八起き"}""", """{\"message\":\"七転び八起き\"}""")]
    [InlineData("""☺️🤖🌍𝄞""", """☺️\uD83E\uDD16\uD83C\uDF0D\uD834\uDD1E""")]
    public static void DefaultOptions_UsesExpectedEscaping(string input, string expectedJsonString)
    {
        var options = AIJsonUtilities.DefaultOptions;
        string json = JsonSerializer.Serialize(input, options);
        Assert.Equal($@"""{expectedJsonString}""", json);
    }

    [Fact]
    public static void DefaultOptions_UsesReflectionWhenDefault()
    {
        // Reflection is only turned off in .NET Core test environments.
        bool isDotnetCore = Type.GetType("System.Half") is not null;
        var options = AIJsonUtilities.DefaultOptions;
        Type anonType = new { Name = 42 }.GetType();

        Assert.Equal(!isDotnetCore, JsonSerializer.IsReflectionEnabledByDefault);
        Assert.Equal(JsonSerializer.IsReflectionEnabledByDefault, AIJsonUtilities.DefaultOptions.TryGetTypeInfo(anonType, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public static void AIJsonSchemaCreateOptions_DefaultInstance_ReturnsExpectedValues(bool useSingleton)
    {
        AIJsonSchemaCreateOptions options = useSingleton ? AIJsonSchemaCreateOptions.Default : new AIJsonSchemaCreateOptions();
        Assert.False(options.IncludeSchemaKeyword);
        Assert.Null(options.TransformSchemaNode);
        Assert.Null(options.TransformOptions);
    }

    [Fact]
    public static void AIJsonSchemaCreateOptions_UsesStructuralEquality()
    {
        AssertEqual(new AIJsonSchemaCreateOptions(), new AIJsonSchemaCreateOptions());

        foreach (PropertyInfo property in typeof(AIJsonSchemaCreateOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            AIJsonSchemaCreateOptions options1 = new AIJsonSchemaCreateOptions();
            AIJsonSchemaCreateOptions options2 = new AIJsonSchemaCreateOptions();
            switch (property.GetValue(AIJsonSchemaCreateOptions.Default))
            {
                case bool booleanFlag:
                    property.SetValue(options1, !booleanFlag);
                    property.SetValue(options2, !booleanFlag);
                    break;

                case null when property.PropertyType == typeof(Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>):
                    Func<AIJsonSchemaCreateContext, JsonNode, JsonNode> transformer = static (context, schema) => (JsonNode)true;
                    property.SetValue(options1, transformer);
                    property.SetValue(options2, transformer);
                    break;

                case null when property.PropertyType == typeof(Func<ParameterInfo, bool>):
                    Func<ParameterInfo, bool> includeParameter = static (parameter) => true;
                    property.SetValue(options1, includeParameter);
                    property.SetValue(options2, includeParameter);
                    break;

                case null when property.PropertyType == typeof(Func<ParameterInfo, string?>):
                    Func<ParameterInfo, string?> parameterDescriptionProvider = static (parameter) => "description";
                    property.SetValue(options1, parameterDescriptionProvider);
                    property.SetValue(options2, parameterDescriptionProvider);
                    break;

                case null when property.PropertyType == typeof(AIJsonSchemaTransformOptions):
                    AIJsonSchemaTransformOptions transformOptions = new AIJsonSchemaTransformOptions { RequireAllProperties = true };
                    property.SetValue(options1, transformOptions);
                    property.SetValue(options2, transformOptions);
                    break;

                default:
                    Assert.Fail($"Unexpected property type: {property.PropertyType}");
                    break;
            }

            AssertEqual(options1, options2);
            AssertNotEqual(AIJsonSchemaCreateOptions.Default, options1);
        }

        static void AssertEqual(AIJsonSchemaCreateOptions x, AIJsonSchemaCreateOptions y)
        {
            Assert.Equal(x.GetHashCode(), y.GetHashCode());
            Assert.Equal(x, x);
            Assert.Equal(y, y);
            Assert.Equal(x, y);
            Assert.Equal(y, x);
        }

        static void AssertNotEqual(AIJsonSchemaCreateOptions x, AIJsonSchemaCreateOptions y)
        {
            Assert.NotEqual(x, y);
            Assert.NotEqual(y, x);
        }
    }

    [Fact]
    public static void CreateJsonSchema_DefaultParameters_GeneratesExpectedJsonSchema()
    {
        JsonElement expected = JsonDocument.Parse("""
            {
                "description": "The type",
                "type": "object",
                "properties": {
                    "Key": {
                        "description": "The parameter",
                        "type": "integer"
                    },
                    "EnumValue": {
                        "type": "string",
                        "enum": ["A", "B"]
                    },
                    "Value": {
                        "type": ["string", "null"],
                        "default": "defaultValue"
                    }
                },
                "required": ["Key", "EnumValue"]
            }
            """).RootElement;

        JsonElement actual = AIJsonUtilities.CreateJsonSchema(typeof(MyPoco), serializerOptions: JsonContext.Default.Options);

        AssertDeepEquals(expected, actual);
    }

    [Fact]
    public static void CreateJsonSchema_TrivialArray_GeneratesExpectedJsonSchema()
    {
        JsonElement expected = JsonDocument.Parse("""
            {
                "type": "array",
                "items": {}
            }
            """).RootElement;

        JsonElement actual = AIJsonUtilities.CreateJsonSchema(typeof(object[]), serializerOptions: JsonContext.Default.Options);

        AssertDeepEquals(expected, actual);
    }

    [Fact]
    public static void CreateJsonSchema_OverriddenParameters_GeneratesExpectedJsonSchema()
    {
        JsonElement expected = JsonDocument.Parse("""
            {
                "$schema": "https://json-schema.org/draft/2020-12/schema",
                "description": "alternative description (Default value: null)",
                "type": "object",
                "properties": {
                    "Key": {
                        "description": "The parameter",
                        "type": "integer"
                    },
                    "EnumValue": {
                        "type": "string",
                        "enum": ["A", "B"]
                    },
                    "Value": {
                        "description": "Default value: \"defaultValue\"",
                        "type": ["string", "null"]
                    }
                },
                "required": ["Key", "EnumValue", "Value"],
                "additionalProperties": false
            }
            """).RootElement;

        AIJsonSchemaCreateOptions inferenceOptions = new AIJsonSchemaCreateOptions
        {
            IncludeSchemaKeyword = true,
            TransformOptions = new()
            {
                DisallowAdditionalProperties = true,
                RequireAllProperties = true,
                MoveDefaultKeywordToDescription = true,
            }
        };

        JsonElement actual = AIJsonUtilities.CreateJsonSchema(
            typeof(MyPoco),
            description: "alternative description",
            hasDefaultValue: true,
            defaultValue: null,
            serializerOptions: JsonContext.Default.Options,
            inferenceOptions: inferenceOptions);

        AssertDeepEquals(expected, actual);
    }

    [Fact]
    public static void CreateJsonSchema_UserDefinedTransformer()
    {
        JsonElement expected = JsonDocument.Parse("""
            {
                "description": "The type",
                "type": "object",
                "properties": {
                    "Key": {
                        "$comment": "Contains a DescriptionAttribute declaration with the text 'The parameter'.",
                        "type": "integer"
                    },
                    "EnumValue": {
                        "type": "string",
                        "enum": ["A", "B"]
                    },
                    "Value": {
                        "type": ["string", "null"],
                        "default": "defaultValue"
                    }
                },
                "required": ["Key", "EnumValue"]
            }
            """).RootElement;

        AIJsonSchemaCreateOptions inferenceOptions = new()
        {
            TransformSchemaNode = static (context, schema) =>
            {
                return context.TypeInfo.Type == typeof(int) && context.GetCustomAttribute<DescriptionAttribute>() is DescriptionAttribute attr
                ? new JsonObject
                {
                    ["$comment"] = $"Contains a DescriptionAttribute declaration with the text '{attr.Description}'.",
                    ["type"] = "integer",
                }
                : schema;
            }
        };

        JsonElement actual = AIJsonUtilities.CreateJsonSchema(typeof(MyPoco), serializerOptions: JsonContext.Default.Options, inferenceOptions: inferenceOptions);

        AssertDeepEquals(expected, actual);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ReturnsExpectedValue()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions);
        AIFunction func = AIFunctionFactory.Create((int x, int y) => x + y, serializerOptions: options);

        Assert.NotNull(func.UnderlyingMethod);

        JsonElement resolvedSchema = AIJsonUtilities.CreateFunctionJsonSchema(func.UnderlyingMethod, title: string.Empty);
        AssertDeepEquals(resolvedSchema, func.JsonSchema);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public static void CreateFunctionJsonSchema_OptionalParameters(bool requireAllProperties)
    {
        string unitJsonSchema = requireAllProperties ? """
            {
                "description": "The unit to calculate the current temperature to (Default value: \u0022celsius\u0022)",
                "type": "string"
            }
            """ :
            """
            {
                "description": "The unit to calculate the current temperature to",
                "type": "string",
                "default": "celsius"
            }
            """;

        string requiredParamsJsonSchema = requireAllProperties ?
            """["city", "unit"]""" :
            """["city"]""";

        JsonElement expected = JsonDocument.Parse($$"""
            {
              "type": "object",
              "properties": {
                "city": {
                  "description": "The city to get the weather for",
                  "type": "string"
                },
                "unit": {{unitJsonSchema}}
              },
              "required": {{requiredParamsJsonSchema}}
            }
            """).RootElement;

        AIJsonSchemaCreateOptions inferenceOptions = new()
        {
            TransformOptions = new()
            {
                RequireAllProperties = requireAllProperties,
                MoveDefaultKeywordToDescription = requireAllProperties,
            }
        };

        AIFunction func = AIFunctionFactory.Create((
            [Description("The city to get the weather for")] string city,
            [Description("The unit to calculate the current temperature to")] string unit = "celsius") => "sunny",
            new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Gets the current weather for a current location",
                JsonSchemaCreateOptions = inferenceOptions
            });

        Assert.NotNull(func.UnderlyingMethod);
        AssertDeepEquals(expected, func.JsonSchema);

        JsonElement resolvedSchema = AIJsonUtilities.CreateFunctionJsonSchema(func.UnderlyingMethod, title: string.Empty, description: string.Empty, inferenceOptions: inferenceOptions);
        AssertDeepEquals(expected, resolvedSchema);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_TreatsIntegralTypesAsInteger_EvenWithAllowReadingFromString()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        AIFunction func = AIFunctionFactory.Create((int a, int? b, long c, short d, float e, double f, decimal g) => { }, serializerOptions: options);

        JsonElement schemaParameters = func.JsonSchema.GetProperty("properties");
        Assert.NotNull(func.UnderlyingMethod);
        ParameterInfo[] parameters = func.UnderlyingMethod.GetParameters();
        Assert.Equal(parameters.Length, schemaParameters.GetPropertyCount());

        int i = 0;
        foreach (JsonProperty property in schemaParameters.EnumerateObject())
        {
            bool isNullable = false;
            Type type = parameters[i].ParameterType;
            if (Nullable.GetUnderlyingType(type) is { } elementType)
            {
                type = elementType;
                isNullable = true;
            }

            string numericType = Type.GetTypeCode(type) is TypeCode.Double or TypeCode.Single or TypeCode.Decimal
                ? "\"number\""
                : "\"integer\"";

            JsonElement expected = JsonDocument.Parse($$"""
                {
                  "type": {{(isNullable ? $"[{numericType}, \"null\"]" : numericType)}}
                }
                """).RootElement;

            JsonElement actualSchema = property.Value;
            AssertDeepEquals(expected, actualSchema);
            i++;
        }
    }

    [Description("The type")]
    public record MyPoco([Description("The parameter")] int Key, MyEnumValue EnumValue, string? Value = "defaultValue");

    [JsonConverter(typeof(JsonStringEnumConverter<MyEnumValue>))]
    public enum MyEnumValue
    {
        A = 1,
        B = 2
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ReadsParameterDataAnnotationAttributes()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        AIFunction func = AIFunctionFactory.Create(([Range(1, 10)] int num, [StringLength(100, MinimumLength = 1)] string str) => num + str.Length, serializerOptions: options);

        using JsonDocument expectedSchema = JsonDocument.Parse("""
            {
                "type":"object",
                "properties": {
                    "num": { "type":"integer", "minimum": 1, "maximum": 10 },
                    "str": { "type":"string", "minLength": 1, "maxLength": 100 }
                },
                "required":["num","str"]
            }
            """);

        AssertDeepEquals(expectedSchema.RootElement, func.JsonSchema);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_RequiredAttribute_MarksParameterAsRequired()
    {
        MethodInfo method = typeof(AIJsonUtilitiesTests).GetMethod(nameof(MethodWithRequiredAttributeCombinations), BindingFlags.NonPublic | BindingFlags.Static)!;
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method, title: string.Empty);

        Assert.True(schema.TryGetProperty("required", out JsonElement requiredElement));
        HashSet<string> requiredParams = new(requiredElement.EnumerateArray().Select(e => e.GetString()!));

        // Non-optional, no default value → required
        Assert.Contains("valueNoDefault", requiredParams);
        Assert.Contains("refNoDefault", requiredParams);
        Assert.Contains("nullableValueNoDefault", requiredParams);
        Assert.Contains("nullableRefNoDefault", requiredParams);

        // Non-optional, no default, with [Required] → required
        Assert.Contains("valueNoDefaultRequired", requiredParams);

        // Has [DefaultValue], with [Required] → required
        Assert.Contains("valueWithDefaultValueAttrRequired", requiredParams);

        // Has C# default, with [Required] → required
        Assert.Contains("valueWithCSharpDefaultRequired", requiredParams);
        Assert.Contains("nullableValueWithDefaultRequired", requiredParams);
        Assert.Contains("refWithDefaultRequired", requiredParams);
        Assert.Contains("nullableRefWithDefaultRequired", requiredParams);

        // Has [DefaultValue], no [Required] → not required
        Assert.DoesNotContain("valueWithDefaultValueAttr", requiredParams);

        // Has C# default, no [Required] → not required
        Assert.DoesNotContain("valueWithCSharpDefault", requiredParams);
        Assert.DoesNotContain("nullableValueWithDefault", requiredParams);
        Assert.DoesNotContain("refWithDefault", requiredParams);
        Assert.DoesNotContain("nullableRefWithDefault", requiredParams);

        Assert.Equal(10, requiredParams.Count);
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static void MethodWithRequiredAttributeCombinations(
        int valueNoDefault,
        [Required] int valueNoDefaultRequired,
        string refNoDefault,
        int? nullableValueNoDefault,
        string? nullableRefNoDefault,
        [DefaultValue(5)] int valueWithDefaultValueAttr,
        [Required, DefaultValue(5)] int valueWithDefaultValueAttrRequired,
        int valueWithCSharpDefault = 5,
        [Required] int valueWithCSharpDefaultRequired = 5,
        int? nullableValueWithDefault = null,
        [Required] int? nullableValueWithDefaultRequired = null,
        string refWithDefault = "default",
        [Required] string refWithDefaultRequired = "default",
        string? nullableRefWithDefault = null,
        [Required] string? nullableRefWithDefaultRequired = null)
    {
        // Method intentionally left empty; used only for schema generation reflection.
    }
#pragma warning restore IDE0060 // Remove unused parameter

    [Fact]
    public static void CreateFunctionJsonSchema_RequiredAttribute_MarksNestedPropertyAsRequired()
    {
        JsonSerializerOptions options = new(JsonContext.Default.Options);
        MethodInfo method = typeof(AIJsonUtilitiesTests).GetMethod(nameof(MethodWithRequiredNestedType), BindingFlags.NonPublic | BindingFlags.Static)!;
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method, title: string.Empty, serializerOptions: options);

        // Get the "input" parameter's schema under properties
        Assert.True(schema.TryGetProperty("properties", out JsonElement properties));
        Assert.True(properties.TryGetProperty("input", out JsonElement inputSchema));

        // The nested type should have its own "required" array for [Required]-attributed properties
        Assert.True(inputSchema.TryGetProperty("required", out JsonElement requiredElement));
        HashSet<string> requiredProps = new(requiredElement.EnumerateArray().Select(e => e.GetString()!));

        Assert.Contains("RequiredValue", requiredProps);
        Assert.Contains("RequiredRef", requiredProps);
        Assert.DoesNotContain("OptionalValue", requiredProps);
        Assert.DoesNotContain("OptionalRef", requiredProps);
    }

    [Fact]
    public static void CreateJsonSchema_RequiredAttribute_MarksPropertyAsRequired()
    {
        JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(TypeWithRequiredMembers), serializerOptions: JsonContext.Default.Options);

        Assert.True(schema.TryGetProperty("required", out JsonElement requiredElement));
        HashSet<string> requiredProps = new(requiredElement.EnumerateArray().Select(e => e.GetString()!));

        Assert.Contains("RequiredValue", requiredProps);
        Assert.Contains("RequiredRef", requiredProps);
        Assert.DoesNotContain("OptionalValue", requiredProps);
        Assert.DoesNotContain("OptionalRef", requiredProps);
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static void MethodWithRequiredNestedType(TypeWithRequiredMembers input)
    {
        // Method intentionally left empty; used only for schema generation reflection.
    }
#pragma warning restore IDE0060 // Remove unused parameter

    internal sealed class TypeWithRequiredMembers
    {
        [Required]
        public int RequiredValue { get; set; }

        [Required]
        public string? RequiredRef { get; set; }

        public int OptionalValue { get; set; }

        public string? OptionalRef { get; set; }
    }

    [Fact]
    public static void CreateFunctionJsonSchema_DisplayNameAttribute_UsedForTitle()
    {
        [DisplayName("custom_method_name")]
        [Description("Method description")]
        static void TestMethod(int x, int y)
        {
            // Test method for schema generation
        }

        var method = ((Action<int, int>)TestMethod).Method;
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method);

        using JsonDocument doc = JsonDocument.Parse(schema.GetRawText());
        Assert.True(doc.RootElement.TryGetProperty("title", out JsonElement titleElement));
        Assert.Equal("custom_method_name", titleElement.GetString());
        Assert.True(doc.RootElement.TryGetProperty("description", out JsonElement descElement));
        Assert.Equal("Method description", descElement.GetString());
    }

    [Fact]
    public static void CreateFunctionJsonSchema_AIFunctionNameAttribute_NotUsedForTitle()
    {
        MethodInfo method = ((Action)MethodWithAIFunctionName).Method;
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method);

        // The schema title is derived from the method name, not from AIFunctionNameAttribute.
        Assert.True(schema.TryGetProperty("title", out JsonElement titleElement));
        Assert.Equal(nameof(MethodWithAIFunctionName), titleElement.GetString());
        Assert.NotEqual("my_tool", titleElement.GetString());
    }

    // Local functions get unspeakable names; define as a private method instead.
    [AIFunctionName("my_tool")]
    private static void MethodWithAIFunctionName()
    {
        // Test method for schema generation
    }

    [Fact]
    public static void CreateFunctionJsonSchema_DisplayNameAttribute_CanBeOverridden()
    {
        [DisplayName("custom_method_name")]
        static void TestMethod()
        {
            // Test method for schema generation
        }

        var method = ((Action)TestMethod).Method;
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method, title: "override_title");

        using JsonDocument doc = JsonDocument.Parse(schema.GetRawText());
        Assert.True(doc.RootElement.TryGetProperty("title", out JsonElement titleElement));
        Assert.Equal("override_title", titleElement.GetString());
    }

    [Fact]
    public static void CreateFunctionJsonSchema_AIParameterNameAttribute_UsedForPropertyName()
    {
        static void TestMethod([AIParameterName("custom_property_name")] string select, int top)
        {
            // Test method for schema generation
        }

        var method = ((Action<string, int>)TestMethod).Method;
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method);

        JsonElement properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("custom_property_name", out _));
        Assert.False(properties.TryGetProperty("select", out _));

        string[] required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains("custom_property_name", required);
        Assert.Contains("top", required);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_AIParameterNameAttribute_DuplicateNamesThrow()
    {
        static void DuplicateByAttribute([AIParameterName("dup")] string first, [AIParameterName("dup")] string second)
        {
            // Test method for schema generation
        }

        ArgumentException ex = Assert.Throws<ArgumentException>(() => AIJsonUtilities.CreateFunctionJsonSchema(((Action<string, string>)DuplicateByAttribute).Method));
        Assert.Contains("dup", ex.Message);
        Assert.Equal("method", ex.ParamName);

        static void DuplicateByCollision([AIParameterName("filter")] string select, string filter)
        {
            // Test method for schema generation
        }

        ArgumentException ex2 = Assert.Throws<ArgumentException>(() => AIJsonUtilities.CreateFunctionJsonSchema(((Action<string, string>)DuplicateByCollision).Method));
        Assert.Contains("filter", ex2.Message);
        Assert.Equal("method", ex2.ParamName);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_AIParameterNameAttribute_EscapesJsonPointerSegment()
    {
        JsonSerializerOptions options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        static void TestMethod([AIParameterName("a/b~c")] RecursiveNode node)
        {
            // Test method for schema generation
        }

        string schema = AIJsonUtilities.CreateFunctionJsonSchema(((Action<RecursiveNode>)TestMethod).Method, serializerOptions: options).ToString();

        Assert.Contains("#/properties/a~1b~0c", schema);
        Assert.DoesNotContain("#/properties/a/b~c", schema);
    }

    [Fact]
    public static void CreateJsonSchema_CanBeBoolean()
    {
        JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(object));
        Assert.Equal(JsonValueKind.True, schema.ValueKind);
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestDataUsingAllValues), MemberType = typeof(TestTypes), DisableDiscoveryEnumeration = true)]
    public static void CreateJsonSchema_ValidateWithTestData(ITestData testData)
    {
        // Stress tests the schema generation method using types from the JsonSchemaExporter test battery.

        JsonSerializerOptions options = testData.Options is { } opts
            ? new(opts) { TypeInfoResolver = TestTypes.TestTypesContext.Default }
            : TestTypes.TestTypesContext.Default.Options;

        JsonTypeInfo typeInfo = options.GetTypeInfo(testData.Type);
        AIJsonSchemaCreateOptions? createOptions = typeInfo.Properties.Any(prop => prop.IsExtensionData)
            ? new() { TransformOptions = new() { DisallowAdditionalProperties = false } } // Do not append additionalProperties: false to the schema if the type has extension data.
            : null;

        JsonElement schema = AIJsonUtilities.CreateJsonSchema(testData.Type, serializerOptions: options, inferenceOptions: createOptions);
        JsonNode? schemaAsNode = JsonSerializer.SerializeToNode(schema, options);

        // NOTE: This is not validating the schemas match, only that they have the same top-level kind.
        Assert.NotNull(schemaAsNode);
        Assert.Equal(testData.ExpectedJsonSchema.GetValueKind(), schemaAsNode.GetValueKind());

        if (testData.Value is null || testData.WritesNumbersAsStrings)
        {
            // By design, our generated schema does not accept null root values
            // or numbers formatted as strings, so we skip schema validation.
            return;
        }

        JsonNode? serializedValue = JsonSerializer.SerializeToNode(testData.Value, testData.Type, options);
        SchemaTestHelpers.AssertDocumentMatchesSchema(schemaAsNode, serializedValue);
    }

    [Fact]
    public static void CreateJsonSchema_AcceptsOptionsWithoutResolver()
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        Assert.Null(options.TypeInfoResolver);
        Assert.False(options.IsReadOnly);

        JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(AIContent), serializerOptions: options);
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);

        Assert.True(options.IsReadOnly);
        Assert.Same(options.TypeInfoResolver, AIJsonUtilities.DefaultOptions.TypeInfoResolver);
    }

    [Fact]
    public static void CreateJsonSchema_NullableEnum_IncludesTypeKeyword()
    {
        JsonElement expectedSchema = JsonDocument.Parse("""
        {
            "type": ["string", "null"],
            "enum": ["A", "B", null]
        }
        """).RootElement;

        JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(MyEnumValue?), serializerOptions: JsonContext.Default.Options);
        AssertDeepEquals(expectedSchema, schema);
    }

    [Fact]
    public static void CreateJsonSchema_IncorporatesTypesAndAnnotations_Net()
    {
        Assert.SkipUnless(!RuntimeInformation.FrameworkDescription.Contains(".NET Framework"), "Only runs on .NET Core");

        AssertDeepEquals(JsonSerializer.Deserialize(
            """
            {
                "type": "object",
                "properties": {
                    "DisplayNameProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "title": "Display Name Title"
                    },
                    "DateTimeProp": {
                        "type": "string",
                        "format": "date-time"
                    },
                    "DateTimeOffsetProp": {
                        "type": "string",
                        "format": "date-time"
                    },
                    "TimeSpanProp": {
                        "$comment": "Represents a System.TimeSpan value.",
                        "type": "string",
                        "pattern": "^-?(\\d+\\.)?\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,7})?$"
                    },
                    "GuidProp": {
                        "type": "string",
                        "format": "uuid"
                    },
                    "UriProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "Base64Prop": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "contentEncoding": "base64"
                    },
                    "RegexProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "pattern": "[abc]|[def]"
                    },
                    "EmailProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "email"
                    },
                    "UrlProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "RangeProp": {
                        "type": "integer",
                        "minimum": 12,
                        "maximum": 34
                    },
                    "AllowedStringValuesProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "enum": [
                            "abc",
                            "def",
                            "ghi"
                        ]
                    },
                    "AllowedInt32ValuesProp": {
                        "type": "integer",
                        "enum": [
                            1,
                            3,
                            5
                        ]
                    },
                    "AllowedDoubleValuesProp": {
                        "type": "number",
                        "enum": [
                            1.2,
                            3.4
                        ]
                    },
                    "DeniedValuesProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "not": {
                            "enum": [
                                "jkl",
                                "mnop"
                            ]
                        }
                    },
                    "DataTypeDateTimeProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "date-time"
                    },
                    "DataTypeDateProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "date"
                    },
                    "DataTypeTimeProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "time"
                    },
                    "DataTypeEmailProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "email"
                    },
                    "DataTypeUrlProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "DataTypeImageUrlProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri",
                        "contentMediaType": "image/*"
                    },
                    "DateOnlyProp": {
                        "type": "string",
                        "format": "date"
                    },
                    "TimeOnlyProp": {
                        "type": "string",
                        "format": "time"
                    },
                    "StringLengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "minLength": 10,
                        "maxLength": 100
                    },
                    "MinLengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "minLength": 5
                    },
                    "MaxLengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "maxLength": 50
                    },
                    "LengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "minLength": 3,
                        "maxLength": 10
                    },
                    "MinLengthArrayProp": {
                        "type": [
                            "array",
                            "null"
                        ],
                        "items": {
                            "type": "integer"
                        },
                        "minItems": 2
                    },
                    "MaxLengthArrayProp": {
                        "type": [
                            "array",
                            "null"
                        ],
                        "items": {
                            "type": "integer"
                        },
                        "maxItems": 8
                    },
                    "LengthArrayProp": {
                        "type": [
                            "array",
                            "null"
                        ],
                        "items": {
                            "type": "integer"
                        },
                        "minItems": 1,
                        "maxItems": 4
                    }
                }
            }
            """,
            JsonContext.Default.JsonElement),
            AIJsonUtilities.CreateJsonSchema(typeof(CreateJsonSchema_IncorporatesTypesAndAnnotations_Type), serializerOptions: JsonContext.Default.Options));
    }

    // .NET Framework only has a subset of the available data annotation attributes.
    // .NET Standard doesn't have any (the M.E.AI.Abstractions library doesn't reference the additional package).
    [Fact]
    public static void CreateJsonSchema_IncorporatesTypesAndAnnotations_NetFx()
    {
        Assert.SkipUnless(RuntimeInformation.FrameworkDescription.Contains(".NET Framework"), "Only runs on .NET Framework");

        AssertDeepEquals(JsonSerializer.Deserialize(
            """
            {
                "type": "object",
                "properties": {
                    "DisplayNameProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "title": "Display Name Title"
                    },
                    "DateTimeProp": {
                        "type": "string",
                        "format": "date-time"
                    },
                    "DateTimeOffsetProp": {
                        "type": "string",
                        "format": "date-time"
                    },
                    "TimeSpanProp": {
                        "$comment": "Represents a System.TimeSpan value.",
                        "type": "string",
                        "pattern": "^-?(\\d+\\.)?\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,7})?$"
                    },
                    "GuidProp": {
                        "type": "string",
                        "format": "uuid"
                    },
                    "UriProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "RegexProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "pattern": "[abc]|[def]"
                    },
                    "EmailProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "email"
                    },
                    "UrlProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "RangeProp": {
                        "type": "integer",
                        "minimum": 12,
                        "maximum": 34
                    },
                    "DataTypeDateTimeProp": {
                        "type": [
                            "string",
                            "null"
                        ]
                    },
                    "DataTypeDateProp": {
                        "type": [
                            "string",
                            "null"
                        ]
                    },
                    "DataTypeTimeProp": {
                        "type": [
                            "string",
                            "null"
                        ]
                    },
                    "DataTypeEmailProp": {
                        "type": [
                            "string",
                            "null"
                        ]
                    },
                    "DataTypeUrlProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "DataTypeImageUrlProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "format": "uri"
                    },
                    "StringLengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "minLength": 10,
                        "maxLength": 100
                    },
                    "MinLengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "minLength": 5
                    },
                    "MaxLengthProp": {
                        "type": [
                            "string",
                            "null"
                        ],
                        "maxLength": 50
                    },
                    "MinLengthArrayProp": {
                        "type": [
                            "array",
                            "null"
                        ],
                        "items": {
                            "type": "integer"
                        },
                        "minItems": 2
                    },
                    "MaxLengthArrayProp": {
                        "type": [
                            "array",
                            "null"
                        ],
                        "items": {
                            "type": "integer"
                        },
                        "maxItems": 8
                    }
                }
            }
            """,
            JsonContext.Default.JsonElement),
            AIJsonUtilities.CreateJsonSchema(typeof(CreateJsonSchema_IncorporatesTypesAndAnnotations_Type), serializerOptions: JsonContext.Default.Options));
    }

    private sealed class CreateJsonSchema_IncorporatesTypesAndAnnotations_Type
    {
        [DisplayName("Display Name Title")]
        public string? DisplayNameProp { get; set; }

        public DateTime DateTimeProp { get; set; }

        public DateTimeOffset DateTimeOffsetProp { get; set; }

        public TimeSpan TimeSpanProp { get; set; }

        public Guid GuidProp { get; set; }

        public Uri? UriProp { get; set; }

        [RegularExpression("[abc]|[def]")]
        public string? RegexProp { get; set; }

        [EmailAddress]
        public string? EmailProp { get; set; }

        [Url]
        public Uri? UrlProp { get; set; }

        [Range(12, 34)]
        public int RangeProp { get; set; }

        [DataType(DataType.DateTime)]
        public string? DataTypeDateTimeProp { get; set; }

        [DataType(DataType.Date)]
        public string? DataTypeDateProp { get; set; }

        [DataType(DataType.Time)]
        public string? DataTypeTimeProp { get; set; }

        [DataType(DataType.EmailAddress)]
        public string? DataTypeEmailProp { get; set; }

        [DataType(DataType.Url)]
        public Uri? DataTypeUrlProp { get; set; }

        [DataType(DataType.ImageUrl)]
        public Uri? DataTypeImageUrlProp { get; set; }

        [StringLength(100, MinimumLength = 10)]
        public string? StringLengthProp { get; set; }

        [MinLength(5)]
        public string? MinLengthProp { get; set; }

        [MaxLength(50)]
        public string? MaxLengthProp { get; set; }

        [MinLength(2)]
        public int[]? MinLengthArrayProp { get; set; }

        [MaxLength(8)]
        public int[]? MaxLengthArrayProp { get; set; }

#if NET
        [Base64String]
        public string? Base64Prop { get; set; }

        [AllowedValues("abc", "def", "ghi")]
        public string? AllowedStringValuesProp { get; set; }

        [AllowedValues(1, 3, 5)]
        public int AllowedInt32ValuesProp { get; set; }

        [AllowedValues(1.2, 3.4)]
        public double AllowedDoubleValuesProp { get; set; }

        [DeniedValues("jkl", "mnop")]
        public string? DeniedValuesProp { get; set; }

        public DateOnly DateOnlyProp { get; set; }

        public TimeOnly TimeOnlyProp { get; set; }

        [Length(3, 10)]
        public string? LengthProp { get; set; }

        [Length(1, 4)]
        public int[]? LengthArrayProp { get; set; }
#endif
    }

    [Fact]
    public static void ClassWithNullableMaxLengthProperty_ReturnsExpectedSchema()
    {
        JsonElement expectedSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "Value": {
                    "type": ["string", "null"],
                    "maxLength": 24,
                    "minLength": 10
                }
            }
        }
        """).RootElement;

        JsonElement actualSchema = AIJsonUtilities.CreateJsonSchema(typeof(ClassWithNullableMaxLengthProperty), serializerOptions: JsonContext.Default.Options);
        AssertDeepEquals(expectedSchema, actualSchema);
    }

    public class ClassWithNullableMaxLengthProperty
    {
        [MinLength(10)]
        [MaxLength(24)]
        public string? Value { get; set; }
    }

    [Fact]
    public static void AddAIContentType_DerivedAIContent()
    {
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(AIJsonUtilities.DefaultOptions.TypeInfoResolver, JsonContext.Default),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.AddAIContentType<DerivedAIContent>("derivativeContent");

        AIContent c = new DerivedAIContent { DerivedValue = 42 };
        string json = JsonSerializer.Serialize(c, options);
        Assert.Equal("""{"$type":"derivativeContent","DerivedValue":42}""", json);

        AIContent? deserialized = JsonSerializer.Deserialize<AIContent>(json, options);
        Assert.IsType<DerivedAIContent>(deserialized);
    }

    [Fact]
    public static void AddAIContentType_ReadOnlyJsonSerializerOptions_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => AIJsonUtilities.DefaultOptions.AddAIContentType<DerivedAIContent>("derivativeContent"));
    }

    [Fact]
    public static void AddAIContentType_NonAIContent_ThrowsArgumentException()
    {
        JsonSerializerOptions options = new();
        Assert.Throws<ArgumentException>("contentType", () => options.AddAIContentType(typeof(int), "discriminator"));
        Assert.Throws<ArgumentException>("contentType", () => options.AddAIContentType(typeof(object), "discriminator"));
        Assert.Throws<ArgumentException>("contentType", () => options.AddAIContentType(typeof(ChatMessage), "discriminator"));
    }

    [Fact]
    public static void AddAIContentType_BuiltInAIContent_ThrowsArgumentException()
    {
        JsonSerializerOptions options = new();
        Assert.Throws<ArgumentException>("contentType", () => options.AddAIContentType<AIContent>("discriminator"));
        Assert.Throws<ArgumentException>("contentType", () => options.AddAIContentType<TextContent>("discriminator"));
    }

    [Fact]
    public static void AddAIContentType_ConflictingIdentifier_ThrowsInvalidOperationException()
    {
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(AIJsonUtilities.DefaultOptions.TypeInfoResolver, JsonContext.Default),
        };
        options.AddAIContentType<DerivedAIContent>("text");
        options.AddAIContentType<DerivedAIContent>("audio");

        AIContent c = new DerivedAIContent();
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(c, options));
    }

    [Fact]
    public static void AddAIContentType_NullArguments_ThrowsArgumentNullException()
    {
        JsonSerializerOptions options = new();
        Assert.Throws<ArgumentNullException>("options", () => ((JsonSerializerOptions)null!).AddAIContentType<DerivedAIContent>("discriminator"));
        Assert.Throws<ArgumentNullException>("options", () => ((JsonSerializerOptions)null!).AddAIContentType(typeof(DerivedAIContent), "discriminator"));
        Assert.Throws<ArgumentNullException>("typeDiscriminatorId", () => options.AddAIContentType<DerivedAIContent>(null!));
        Assert.Throws<ArgumentNullException>("typeDiscriminatorId", () => options.AddAIContentType(typeof(DerivedAIContent), null!));
        Assert.Throws<ArgumentNullException>("contentType", () => options.AddAIContentType(null!, "discriminator"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public static void AddAIContentType_LeafContent_RegistersInChain(bool useGenericOverload)
    {
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(AIJsonUtilities.DefaultOptions.TypeInfoResolver, JsonContext.Default),
        };

        if (useGenericOverload)
        {
            options.AddAIContentType<LeafContent>("leaf");
        }
        else
        {
            options.AddAIContentType(typeof(LeafContent), "leaf");
        }

        // Verify LeafContent is registered as a derived type of both DerivedAIContent and AIContent.
        var derivedAIContentTypes = options.GetTypeInfo(typeof(DerivedAIContent)).PolymorphismOptions!.DerivedTypes;
        var aiContentTypes = options.GetTypeInfo(typeof(AIContent)).PolymorphismOptions!.DerivedTypes;
        Assert.Contains(derivedAIContentTypes, dt => dt.DerivedType == typeof(LeafContent));
        Assert.Contains(aiContentTypes, dt => dt.DerivedType == typeof(LeafContent));

        // Verify serialization/deserialization works when typed as DerivedAIContent.
        DerivedAIContent dc = new LeafContent { DerivedValue = 1, LeafValue = 42 };
        string json = JsonSerializer.Serialize(dc, options);
        Assert.Contains("\"$type\":\"leaf\"", json);
        Assert.Contains("\"DerivedValue\":1", json);
        Assert.Contains("\"LeafValue\":42", json);

        LeafContent deserializedDc = Assert.IsType<LeafContent>(JsonSerializer.Deserialize<DerivedAIContent>(json, options));
        Assert.Equal(1, deserializedDc.DerivedValue);
        Assert.Equal(42, deserializedDc.LeafValue);

        // Verify serialization/deserialization works when typed as AIContent.
        AIContent ac = new LeafContent { DerivedValue = 2, LeafValue = 99 };
        string json2 = JsonSerializer.Serialize(ac, options);
        Assert.Contains("\"$type\":\"leaf\"", json2);
        Assert.Contains("\"DerivedValue\":2", json2);
        Assert.Contains("\"LeafValue\":99", json2);

        LeafContent deserializedAc = Assert.IsType<LeafContent>(JsonSerializer.Deserialize<AIContent>(json2, options));
        Assert.Equal(2, deserializedAc.DerivedValue);
        Assert.Equal(99, deserializedAc.LeafValue);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public static void AddAIContentType_ChainWalksThroughBuiltInBaseTypes(bool useGenericOverload)
    {
        // Verifies that the chain registration walks through built-in intermediate types.
        // For DerivedToolCallContent : ToolCallContent : AIContent, a single call should
        // register against both ToolCallContent (built-in) and AIContent (built-in), so that
        // deserialization works regardless of which base type the variable is declared as.
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(AIJsonUtilities.DefaultOptions.TypeInfoResolver, JsonContext.Default),
        };

        if (useGenericOverload)
        {
            options.AddAIContentType<DerivedToolCallContent>("derivedToolCall");
        }
        else
        {
            options.AddAIContentType(typeof(DerivedToolCallContent), "derivedToolCall");
        }

        // Verify DerivedToolCallContent is registered as a derived type of both ToolCallContent and AIContent.
        var toolCallTypes = options.GetTypeInfo(typeof(ToolCallContent)).PolymorphismOptions!.DerivedTypes;
        var aiContentTypes = options.GetTypeInfo(typeof(AIContent)).PolymorphismOptions!.DerivedTypes;
        Assert.Contains(toolCallTypes, dt => dt.DerivedType == typeof(DerivedToolCallContent));
        Assert.Contains(aiContentTypes, dt => dt.DerivedType == typeof(DerivedToolCallContent));

        // Verify roundtrip when typed as ToolCallContent (built-in intermediate base).
        ToolCallContent tc = new DerivedToolCallContent { CustomValue = 42 };
        string json = JsonSerializer.Serialize(tc, options);
        Assert.Contains("\"$type\":\"derivedToolCall\"", json);
        Assert.Contains("\"CustomValue\":42", json);

        DerivedToolCallContent deserializedTc = Assert.IsType<DerivedToolCallContent>(JsonSerializer.Deserialize<ToolCallContent>(json, options));
        Assert.Equal(42, deserializedTc.CustomValue);

        // Verify roundtrip when typed as AIContent (root base).
        AIContent ac = new DerivedToolCallContent { CustomValue = 99 };
        string json2 = JsonSerializer.Serialize(ac, options);
        Assert.Contains("\"$type\":\"derivedToolCall\"", json2);
        Assert.Contains("\"CustomValue\":99", json2);

        DerivedToolCallContent deserializedAc = Assert.IsType<DerivedToolCallContent>(JsonSerializer.Deserialize<AIContent>(json2, options));
        Assert.Equal(99, deserializedAc.CustomValue);
    }

    [Fact]
    public static void AddAIContentType_OverlappingChains_RegistersBothCorrectly()
    {
        // Registers DerivedAIContent and LeafContent (which derives from DerivedAIContent).
        // Both types should coexist: DerivedAIContent is registered under AIContent,
        // and LeafContent is registered under both DerivedAIContent and AIContent.
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(AIJsonUtilities.DefaultOptions.TypeInfoResolver, JsonContext.Default),
        };

        options.AddAIContentType<DerivedAIContent>("derived");
        options.AddAIContentType<LeafContent>("leaf");

        // Verify registrations: AIContent should have both derived and leaf; DerivedAIContent should have leaf.
        var aiContentDerivedTypes = options.GetTypeInfo(typeof(AIContent)).PolymorphismOptions!.DerivedTypes;
        var derivedAIContentTypes = options.GetTypeInfo(typeof(DerivedAIContent)).PolymorphismOptions!.DerivedTypes;
        Assert.Contains(aiContentDerivedTypes, dt => dt.DerivedType == typeof(DerivedAIContent));
        Assert.Contains(aiContentDerivedTypes, dt => dt.DerivedType == typeof(LeafContent));
        Assert.Contains(derivedAIContentTypes, dt => dt.DerivedType == typeof(LeafContent));

        // DerivedAIContent roundtrips when typed as AIContent.
        AIContent ac = new DerivedAIContent { DerivedValue = 1 };
        string json = JsonSerializer.Serialize(ac, options);
        Assert.Contains("\"$type\":\"derived\"", json);
        DerivedAIContent deserializedDc = Assert.IsType<DerivedAIContent>(JsonSerializer.Deserialize<AIContent>(json, options));
        Assert.Equal(1, deserializedDc.DerivedValue);

        // LeafContent roundtrips when typed as DerivedAIContent.
        DerivedAIContent dc = new LeafContent { DerivedValue = 2, LeafValue = 42 };
        string json2 = JsonSerializer.Serialize(dc, options);
        Assert.Contains("\"$type\":\"leaf\"", json2);
        LeafContent deserializedLeaf = Assert.IsType<LeafContent>(JsonSerializer.Deserialize<DerivedAIContent>(json2, options));
        Assert.Equal(2, deserializedLeaf.DerivedValue);
        Assert.Equal(42, deserializedLeaf.LeafValue);

        // LeafContent roundtrips when typed as AIContent.
        AIContent ac2 = new LeafContent { DerivedValue = 3, LeafValue = 99 };
        string json3 = JsonSerializer.Serialize(ac2, options);
        Assert.Contains("\"$type\":\"leaf\"", json3);
        LeafContent deserializedAc = Assert.IsType<LeafContent>(JsonSerializer.Deserialize<AIContent>(json3, options));
        Assert.Equal(3, deserializedAc.DerivedValue);
        Assert.Equal(99, deserializedAc.LeafValue);
    }

    [Fact]
    public static void AddAIContentType_DoesNotRegisterIntermediateTypes()
    {
        // Registering LeafContent should NOT automatically register DerivedAIContent.
        // Only the explicitly provided type gets a discriminator. This is by design:
        // auto-registering intermediates would make it impossible to register multiple
        // leaf types sharing a common base (the intermediate would be registered twice
        // with no discriminator, or a conflicting one).
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(AIJsonUtilities.DefaultOptions.TypeInfoResolver, JsonContext.Default),
        };

        options.AddAIContentType<LeafContent>("leaf");

        // Verify LeafContent is registered under both bases, but DerivedAIContent is NOT.
        var aiContentDerivedTypes = options.GetTypeInfo(typeof(AIContent)).PolymorphismOptions!.DerivedTypes;
        var derivedAIContentTypes = options.GetTypeInfo(typeof(DerivedAIContent)).PolymorphismOptions!.DerivedTypes;
        Assert.Contains(aiContentDerivedTypes, dt => dt.DerivedType == typeof(LeafContent));
        Assert.DoesNotContain(aiContentDerivedTypes, dt => dt.DerivedType == typeof(DerivedAIContent));
        Assert.Contains(derivedAIContentTypes, dt => dt.DerivedType == typeof(LeafContent));

        // LeafContent roundtrips fine when typed as AIContent.
        AIContent ac = new LeafContent { DerivedValue = 1, LeafValue = 42 };
        string json = JsonSerializer.Serialize(ac, options);
        Assert.Contains("\"$type\":\"leaf\"", json);
        Assert.IsType<LeafContent>(JsonSerializer.Deserialize<AIContent>(json, options));

        // But a plain DerivedAIContent instance is NOT known to AIContent — no discriminator was registered for it.
        AIContent unregistered = new DerivedAIContent { DerivedValue = 99 };
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(unregistered, options));
    }

    [Fact]
    public static void HashData_Idempotent()
    {
        JsonSerializerOptions customOptions = new()
        {
            TypeInfoResolver = AIJsonUtilities.DefaultOptions.TypeInfoResolver
        };

        foreach (JsonSerializerOptions? options in new[] { AIJsonUtilities.DefaultOptions, null, customOptions })
        {
            string key1 = AIJsonUtilities.HashDataToString(["a", 'b', 42], options);
            string key2 = AIJsonUtilities.HashDataToString(["a", 'b', 42], options);
            string key3 = AIJsonUtilities.HashDataToString([TimeSpan.FromSeconds(1), null, 1.23], options);
            string key4 = AIJsonUtilities.HashDataToString([TimeSpan.FromSeconds(1), null, 1.23], options);
            string key5 = AIJsonUtilities.HashDataToString([new Dictionary<string, object> { ["key1"] = 1, ["key2"] = 2 }], options);
            string key6 = AIJsonUtilities.HashDataToString([new Dictionary<string, object> { ["key2"] = 2, ["key1"] = 1 }], options);

            Assert.Equal(key1, key2);
            Assert.Equal(key3, key4);
            Assert.Equal(key5, key6);
            Assert.NotEqual(key1, key3);
            Assert.NotEqual(key1, key5);
        }
    }

    [Fact]
    public static void HashData_IndentationInvariant()
    {
        JsonSerializerOptions indentOptions = new(AIJsonUtilities.DefaultOptions) { WriteIndented = true };
        JsonSerializerOptions noIndentOptions = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

        Dictionary<string, object> dict = new() { ["key1"] = 1, ["key2"] = 2 };
        string key1 = AIJsonUtilities.HashDataToString([dict], indentOptions);
        string key2 = AIJsonUtilities.HashDataToString([dict], noIndentOptions);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_InvokesIncludeParameterCallbackForEveryParameter()
    {
        Delegate method = (int first, string second, bool third, CancellationToken fourth, DateTime fifth) => { };

        List<string?> names = [];
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method.Method, inferenceOptions: new()
        {
            IncludeParameter = p =>
            {
                names.Add(p.Name);
                return p.Name is "first" or "fifth";
            },
        });

        Assert.Equal(["first", "second", "third", "fifth"], names);

        string schemaString = schema.ToString();
        Assert.Contains("first", schemaString);
        Assert.DoesNotContain("second", schemaString);
        Assert.DoesNotContain("third", schemaString);
        Assert.DoesNotContain("fourth", schemaString);
        Assert.Contains("fifth", schemaString);
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ParameterDescriptionProvider_OverridesDescriptionAttribute()
    {
        Delegate method = (
            [Description("Original description for first")] int first,
            [Description("Original description for second")] string second) =>
        {
        };

        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method.Method, inferenceOptions: new()
        {
            ParameterDescriptionProvider = p => p.Name == "first" ? "Overridden description for first" : null
        });

        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal("Overridden description for first", properties.GetProperty("first").GetProperty("description").GetString());
        Assert.Equal("Original description for second", properties.GetProperty("second").GetProperty("description").GetString());
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ParameterDescriptionProvider_AddsDescriptionWhenAttributeMissing()
    {
        Delegate method = (int first, string second) =>
        {
        };

        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method.Method, inferenceOptions: new()
        {
            ParameterDescriptionProvider = p => p.Name switch
            {
                "first" => "Added description for first",
                "second" => "Added description for second",
                _ => null
            }
        });

        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal("Added description for first", properties.GetProperty("first").GetProperty("description").GetString());
        Assert.Equal("Added description for second", properties.GetProperty("second").GetProperty("description").GetString());
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ParameterDescriptionProvider_ReturnsNull_UsesAttributeDescriptions()
    {
        Delegate method = (
            [Description("Description from attribute")] int first,
            string second) =>
        {
        };

        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method.Method, inferenceOptions: new()
        {
            ParameterDescriptionProvider = _ => null
        });

        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal("Description from attribute", properties.GetProperty("first").GetProperty("description").GetString());
        Assert.False(properties.GetProperty("second").TryGetProperty("description", out _));
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ParameterDescriptionProvider_NullValue_UsesAttributeDescriptions()
    {
        Delegate method = (
            [Description("Description from attribute")] int first,
            string second) =>
        {
        };

        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method.Method, inferenceOptions: new()
        {
            ParameterDescriptionProvider = null
        });

        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal("Description from attribute", properties.GetProperty("first").GetProperty("description").GetString());
        Assert.False(properties.GetProperty("second").TryGetProperty("description", out _));
    }

    [Fact]
    public static void CreateFunctionJsonSchema_ParameterDescriptionProvider_OnlyCalledForActualParameters()
    {
        Delegate method = (int first, string second) =>
        {
        };

        List<string?> calledParameterNames = [];
        JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(method.Method, inferenceOptions: new()
        {
            ParameterDescriptionProvider = p =>
            {
                calledParameterNames.Add(p.Name);
                return p.Name == "first" ? "Description for first" : null;
            }
        });

        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal(2, properties.EnumerateObject().Count());
        Assert.Equal("Description for first", properties.GetProperty("first").GetProperty("description").GetString());
        Assert.Equal(["first", "second"], calledParameterNames);
    }

    [Fact]
    public static void TransformJsonSchema_ConvertBooleanSchemas()
    {
        JsonElement schema = JsonDocument.Parse("""
            {
                "properties" : {
                    "foo": { "type": "string" },
                    "bar": { "properties": { "x": false } },
                    "baz": true
                },
                "required": ["foo"]
            }
            """).RootElement;

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "properties" : {
                    "foo": { "type": "string" },
                    "bar": { "properties": { "x": { "not": true } } },
                    "baz": { }
                },
                "required": ["foo"]
            }
            """).RootElement;

        AIJsonSchemaTransformOptions options = new()
        {
            ConvertBooleanSchemas = true,
        };

        JsonElement transformedSchema = AIJsonUtilities.TransformSchema(schema, options);
        AssertDeepEquals(expectedSchema, transformedSchema);
    }

    [Fact]
    public static void TransformJsonSchema_RequireAllProperties()
    {
        JsonElement schema = JsonDocument.Parse("""
            {
                "properties" : {
                    "foo": { "type": "string" },
                    "bar": { "properties": { "x": false } },
                    "baz": true
                },
                "required": ["foo"]
            }
            """).RootElement;

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "properties" : {
                    "foo": { "type": "string" },
                    "bar": { "properties": { "x": false }, "required": ["x"] },
                    "baz": true
                },
                "required": ["foo", "bar", "baz"]
            }
            """).RootElement;

        AIJsonSchemaTransformOptions options = new()
        {
            RequireAllProperties = true,
        };

        JsonElement transformedSchema = AIJsonUtilities.TransformSchema(schema, options);
        AssertDeepEquals(expectedSchema, transformedSchema);
    }

    [Fact]
    public static void TransformJsonSchema_DisallowAdditionalProperties()
    {
        JsonElement schema = JsonDocument.Parse("""
            {
                "properties" : {
                    "foo": { "type": "string" },
                    "bar": { "properties": { "x": false } },
                    "baz": true
                },
                "required": ["foo"]
            }
            """).RootElement;

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "properties" : {
                    "foo": { "type": "string" },
                    "bar": { "properties": { "x": false }, "additionalProperties": false },
                    "baz": true
                },
                "required": ["foo"],
                "additionalProperties": false
            }
            """).RootElement;

        AIJsonSchemaTransformOptions options = new()
        {
            DisallowAdditionalProperties = true,
        };

        JsonElement transformedSchema = AIJsonUtilities.TransformSchema(schema, options);
        AssertDeepEquals(expectedSchema, transformedSchema);
    }

    [Fact]
    public static void TransformJsonSchema_UseNullableKeyword()
    {
        JsonElement schema = JsonDocument.Parse("""
            {
                "type": ["object","null"],
                "properties" : {
                    "foo": { "type": ["null","string"] },
                    "bar": { "type":"object", "properties": { "x": false } },
                    "baz": { "type" : ["string","array","null"] }
                },
                "required": ["foo"]
            }
            """).RootElement;

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties" : {
                    "foo": { "type": "string", "nullable": true },
                    "bar": { "type":"object", "properties": { "x": false } },
                    "baz": { "type" : ["string","array","null"] }
                },
                "required": ["foo"],
                "nullable": true
            }
            """).RootElement;

        AIJsonSchemaTransformOptions options = new()
        {
            UseNullableKeyword = true,
        };

        JsonElement transformedSchema = AIJsonUtilities.TransformSchema(schema, options);
        AssertDeepEquals(expectedSchema, transformedSchema);
    }

    [Fact]
    public static void TransformJsonSchema_MoveDefaultKeywordToDescription()
    {
        JsonElement schema = JsonDocument.Parse("""
            {
                "description": "My awesome schema",
                "type": "array",
                "default": [1,2,3]
            }
            """).RootElement;

        JsonElement expectedSchema = JsonDocument.Parse("""
            {
                "description": "My awesome schema (Default value: [1,2,3])",
                "type": "array"
            }
            """).RootElement;

        AIJsonSchemaTransformOptions options = new()
        {
            MoveDefaultKeywordToDescription = true,
        };

        JsonElement transformedSchema = AIJsonUtilities.TransformSchema(schema, options);
        AssertDeepEquals(expectedSchema, transformedSchema);
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestDataUsingAllValues), MemberType = typeof(TestTypes), DisableDiscoveryEnumeration = true)]
    public static void TransformJsonSchema_ValidateWithTestData(ITestData testData)
    {
        // Stress tests the schema generation method using types from the JsonSchemaExporter test battery.

        JsonSerializerOptions options = testData.Options is { } opts
            ? new(opts) { TypeInfoResolver = TestTypes.TestTypesContext.Default }
            : TestTypes.TestTypesContext.Default.Options;

        JsonTypeInfo typeInfo = options.GetTypeInfo(testData.Type);
        AIJsonSchemaCreateOptions? createOptions = typeInfo.Properties.Any(prop => prop.IsExtensionData)
            ? new() { TransformOptions = new() { DisallowAdditionalProperties = false } } // Do not append additionalProperties: false to the schema if the type has extension data.
            : null;

        JsonElement schema = AIJsonUtilities.CreateJsonSchema(testData.Type, serializerOptions: options, inferenceOptions: createOptions);

        int totalSchemaNodes = 0;
        AIJsonSchemaTransformOptions transformOptions = new()
        {
            ConvertBooleanSchemas = true,
            RequireAllProperties = true,
            DisallowAdditionalProperties = true,
            UseNullableKeyword = true,
            TransformSchemaNode = (context, schema) =>
            {
                totalSchemaNodes++;
                var schemaObj = Assert.IsType<JsonObject>(schema);
                schemaObj.Add("myAwesomeKeyword", (JsonNode)42);
                return schemaObj;
            }
        };

        JsonElement transformedSchema = AIJsonUtilities.TransformSchema(schema, transformOptions);
        Assert.True(totalSchemaNodes > 0, "TransformSchema was not invoked.");

        int totalSchemaNodes2 = 0;
        transformOptions = new()
        {
            TransformSchemaNode = (context, schema) =>
            {
                totalSchemaNodes2++;
                var schemaObj = Assert.IsType<JsonObject>(schema);
                Assert.Contains("myAwesomeKeyword", schemaObj);
                if (schemaObj.TryGetPropertyValue("properties", out JsonNode? props))
                {
                    Assert.Contains("required", schemaObj);
                    Assert.Contains("additionalProperties", schemaObj);
                    Assert.Equal(((JsonArray)schemaObj["required"]!).Count, ((JsonObject)props!).Count);
                }

                if (schemaObj.TryGetPropertyValue("type", out JsonNode? type) && type is JsonArray typeArray)
                {
                    Assert.DoesNotContain("null", typeArray);
                }

                return schemaObj;
            }
        };

        AIJsonUtilities.TransformSchema(transformedSchema, transformOptions);
        Assert.Equal(totalSchemaNodes, totalSchemaNodes2);
    }

    [Fact]
    public static void TransformJsonSchema_InvalidOptions_ThrowsArgumentException()
    {
        JsonElement schema = JsonDocument.Parse("{}").RootElement;
        Assert.Throws<ArgumentNullException>("transformOptions", () => AIJsonUtilities.TransformSchema(schema, transformOptions: null!));
        Assert.Throws<ArgumentException>("transformOptions", () => AIJsonUtilities.TransformSchema(schema, transformOptions: new()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"properties":{"x": 42 }}""")]
    [InlineData("""{"oneOf":[42]}""")]
    public static void TransformJsonSchema_InvalidInput_ThrowsArgumentException(string invalidSchema)
    {
        JsonElement schema = JsonDocument.Parse(invalidSchema).RootElement;
        AIJsonSchemaTransformOptions transformOptions = new() { ConvertBooleanSchemas = true };
        Assert.Throws<ArgumentException>("schema", () => AIJsonUtilities.TransformSchema(schema, transformOptions));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public static void TransformJsonSchema_BooleanSchemas_Success(string booleanSchema)
    {
        // Boolean schemas (true/false) are valid JSON schemas per the spec.
        // This test verifies they are accepted by TransformSchema.
        JsonElement schema = JsonDocument.Parse(booleanSchema).RootElement;
        AIJsonSchemaTransformOptions transformOptions = new() { ConvertBooleanSchemas = true };

        // Should not throw - boolean schemas are valid
        JsonElement result = AIJsonUtilities.TransformSchema(schema, transformOptions);

        // Verify the transformation happened correctly
        if (booleanSchema == "true")
        {
            // 'true' schema should be converted to empty object
            Assert.Equal(JsonValueKind.Object, result.ValueKind);
        }
        else
        {
            // 'false' schema should be converted to {"not": true}
            Assert.Equal(JsonValueKind.Object, result.ValueKind);
            Assert.True(result.TryGetProperty("not", out JsonElement notValue));
            Assert.Equal(JsonValueKind.True, notValue.ValueKind);
        }
    }

    private class LeafContent : DerivedAIContent
    {
        public int LeafValue { get; set; }
    }

    private class DerivedAIContent : AIContent
    {
        public int DerivedValue { get; set; }
    }

    private class DerivedToolCallContent : ToolCallContent
    {
        public DerivedToolCallContent()
            : base("callId")
        {
        }

        public int CustomValue { get; set; }
    }

    private sealed class RecursiveNode
    {
        public RecursiveNode? Next { get; set; }
    }

    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(CreateJsonSchema_IncorporatesTypesAndAnnotations_Type))]
    [JsonSerializable(typeof(DerivedAIContent))]
    [JsonSerializable(typeof(DerivedToolCallContent))]
    [JsonSerializable(typeof(LeafContent))]
    [JsonSerializable(typeof(MyPoco))]
    [JsonSerializable(typeof(MyEnumValue?))]
    [JsonSerializable(typeof(object[]))]
    [JsonSerializable(typeof(ClassWithNullableMaxLengthProperty))]
    [JsonSerializable(typeof(TypeWithRequiredMembers))]
    private partial class JsonContext : JsonSerializerContext;

    private static bool DeepEquals(JsonElement element1, JsonElement element2)
    {
        return JsonElement.DeepEquals(element1, element2);
    }

    private static void AssertDeepEquals(JsonElement element1, JsonElement element2)
    {
#pragma warning disable SA1118 // Parameter should not span multiple lines
        Assert.True(DeepEquals(element1, element2), $"""
            Elements are not equal.
            Expected:
            {element1}
            Actual:
            {element2}
            """);
#pragma warning restore SA1118 // Parameter should not span multiple lines
    }
}
