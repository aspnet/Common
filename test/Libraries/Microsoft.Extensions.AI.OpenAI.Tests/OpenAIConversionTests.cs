// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI.Realtime;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001 // Experimental OpenAI APIs

namespace Microsoft.Extensions.AI;

public class OpenAIConversionTests
{
    private static readonly AIFunction _testFunction = AIFunctionFactory.Create(
        ([Description("The name parameter")] string name) => name,
        "test_function",
        "A test function for conversion");

    [Fact]
    public void AsOpenAIChatResponseFormat_HandlesVariousFormats()
    {
        Assert.Null(MicrosoftExtensionsAIChatExtensions.AsOpenAIChatResponseFormat(null));

        var text = MicrosoftExtensionsAIChatExtensions.AsOpenAIChatResponseFormat(ChatResponseFormat.Text);
        Assert.NotNull(text);
        Assert.Equal("""{"type":"text"}""", ((IJsonModel<OpenAI.Chat.ChatResponseFormat>)text).Write(ModelReaderWriterOptions.Json).ToString());

        var json = MicrosoftExtensionsAIChatExtensions.AsOpenAIChatResponseFormat(ChatResponseFormat.Json);
        Assert.NotNull(json);
        Assert.Equal("""{"type":"json_object"}""", ((IJsonModel<OpenAI.Chat.ChatResponseFormat>)json).Write(ModelReaderWriterOptions.Json).ToString());

        var jsonSchema = ChatResponseFormat.ForJsonSchema(typeof(int), schemaName: "my_schema", schemaDescription: "A test schema").AsOpenAIChatResponseFormat();
        Assert.NotNull(jsonSchema);
        Assert.Equal(RemoveWhitespace("""
            {"type":"json_schema","json_schema":{"description":"A test schema","name":"my_schema","schema":{
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "integer"
            }}}
            """), RemoveWhitespace(((IJsonModel<OpenAI.Chat.ChatResponseFormat>)jsonSchema).Write(ModelReaderWriterOptions.Json).ToString()));

        jsonSchema = ChatResponseFormat.ForJsonSchema(typeof(int), schemaName: "my_schema", schemaDescription: "A test schema").AsOpenAIChatResponseFormat(
            new() { AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true } });
        Assert.NotNull(jsonSchema);
        Assert.Equal(RemoveWhitespace("""
            {
            "type":"json_schema","json_schema":{"description":"A test schema","name":"my_schema","schema":{
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "integer"
            },"strict":true}}
            """), RemoveWhitespace(((IJsonModel<OpenAI.Chat.ChatResponseFormat>)jsonSchema).Write(ModelReaderWriterOptions.Json).ToString()));
    }

    [Fact]
    public void AsOpenAIResponseTextFormat_HandlesVariousFormats()
    {
        Assert.Null(MicrosoftExtensionsAIResponsesExtensions.AsOpenAIResponseTextFormat(null));

        var text = MicrosoftExtensionsAIResponsesExtensions.AsOpenAIResponseTextFormat(ChatResponseFormat.Text);
        Assert.NotNull(text);
        Assert.Equal(ResponseTextFormatKind.Text, text.Kind);

        var json = MicrosoftExtensionsAIResponsesExtensions.AsOpenAIResponseTextFormat(ChatResponseFormat.Json);
        Assert.NotNull(json);
        Assert.Equal(ResponseTextFormatKind.JsonObject, json.Kind);

        var jsonSchema = ChatResponseFormat.ForJsonSchema(typeof(int), schemaName: "my_schema", schemaDescription: "A test schema").AsOpenAIResponseTextFormat();
        Assert.NotNull(jsonSchema);
        Assert.Equal(ResponseTextFormatKind.JsonSchema, jsonSchema.Kind);
        Assert.Equal(RemoveWhitespace("""
            {"type":"json_schema","description":"A test schema","name":"my_schema","schema":{
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "integer"
            }}
            """), RemoveWhitespace(((IJsonModel<ResponseTextFormat>)jsonSchema).Write(ModelReaderWriterOptions.Json).ToString()));

        jsonSchema = ChatResponseFormat.ForJsonSchema(typeof(int), schemaName: "my_schema", schemaDescription: "A test schema").AsOpenAIResponseTextFormat(
            new() { AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true } });
        Assert.NotNull(jsonSchema);
        Assert.Equal(ResponseTextFormatKind.JsonSchema, jsonSchema.Kind);
        Assert.Equal(RemoveWhitespace("""
            {"type":"json_schema","description":"A test schema","name":"my_schema","schema":{
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "integer"
            },"strict":true}
            """), RemoveWhitespace(((IJsonModel<ResponseTextFormat>)jsonSchema).Write(ModelReaderWriterOptions.Json).ToString()));
    }

    [Fact]
    public void AsOpenAIChatTool_ProducesValidInstance()
    {
        var tool = _testFunction.AsOpenAIChatTool();

        Assert.NotNull(tool);
        Assert.Equal("test_function", tool.FunctionName);
        Assert.Equal("A test function for conversion", tool.FunctionDescription);
        ValidateSchemaParameters(tool.FunctionParameters);
    }

    [Fact]
    public void AsOpenAIChatTool_ExplicitSchema_PreservesObjectAdditionalProperties()
    {
        var schema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "labels": { "type": "object" }
                },
                "additionalProperties": { "type": "string" }
            }
            """).RootElement;

        var function = AIFunctionFactory.CreateDeclaration(
            "set_labels",
            "Stores labels and arbitrary string metadata.",
            schema);

        var tool = function.AsOpenAIChatTool();
        Assert.NotNull(tool);
        Assert.Equal("set_labels", tool.FunctionName);

        using var parsedParams = JsonDocument.Parse(tool.FunctionParameters);
        var root = parsedParams.RootElement;
        Assert.True(root.TryGetProperty("additionalProperties", out var ap), "additionalProperties should be preserved");
        Assert.Equal(JsonValueKind.Object, ap.ValueKind);
        Assert.Equal("string", ap.GetProperty("type").GetString());
    }

    [Fact]
    public void AsOpenAIChatTool_PreservesExtraTopLevelPropertiesLikeDefs()
    {
        // Create a JSON schema with $defs (used for reference types)
        var jsonSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "person": { "$ref": "#/$defs/Person" }
                },
                "required": ["person"],
                "$defs": {
                    "Person": {
                        "type": "object",
                        "properties": {
                            "name": { "type": "string" }
                        }
                    }
                }
            }
            """).RootElement;

        var functionWithDefs = AIFunctionFactory.CreateDeclaration(
            "test_function_with_defs",
            "A test function with $defs",
            jsonSchema);

        var tool = functionWithDefs.AsOpenAIChatTool();

        Assert.NotNull(tool);
        Assert.Equal("test_function_with_defs", tool.FunctionName);
        Assert.Equal("A test function with $defs", tool.FunctionDescription);

        // Verify that $defs is preserved in the function parameters
        using var parsedParams = JsonDocument.Parse(tool.FunctionParameters);
        var root = parsedParams.RootElement;

        Assert.True(root.TryGetProperty("$defs", out var defs), "The $defs property should be preserved in the function parameters");
        Assert.True(defs.TryGetProperty("Person", out var person), "The Person definition should exist in $defs");
        Assert.True(person.TryGetProperty("properties", out var properties), "Person should have properties");
        Assert.True(properties.TryGetProperty("name", out _), "Person should have a name property");
    }

    [Fact]
    public void AsOpenAIResponseTool_ProducesValidInstance()
    {
        var tool = _testFunction.AsOpenAIResponseTool();

        Assert.NotNull(tool);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithAIFunctionTool_ProducesValidFunctionTool()
    {
        var tool = MicrosoftExtensionsAIResponsesExtensions.AsOpenAIResponseTool(tool: _testFunction);

        Assert.NotNull(tool);
        var functionTool = Assert.IsType<FunctionTool>(tool);
        Assert.Equal("test_function", functionTool.FunctionName);
        Assert.Equal("A test function for conversion", functionTool.FunctionDescription);
    }

    [Fact]
    public void AsOpenAIResponseTool_ExplicitSchema_PreservesObjectAdditionalProperties()
    {
        var schema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "labels": { "type": "object" }
                },
                "additionalProperties": { "type": "string" }
            }
            """).RootElement;

        var function = AIFunctionFactory.CreateDeclaration(
            "set_labels",
            "Stores labels and arbitrary string metadata.",
            schema);

        var tool = function.AsOpenAIResponseTool();
        Assert.NotNull(tool);
        var functionTool = Assert.IsType<FunctionTool>(tool);
        Assert.Equal("set_labels", functionTool.FunctionName);

        using var parsedParams = JsonDocument.Parse(functionTool.FunctionParameters);
        var root = parsedParams.RootElement;
        Assert.True(root.TryGetProperty("additionalProperties", out var ap), "additionalProperties should be preserved");
        Assert.Equal(JsonValueKind.Object, ap.ValueKind);
        Assert.Equal("string", ap.GetProperty("type").GetString());
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedWebSearchTool_ProducesValidWebSearchTool()
    {
        var webSearchTool = new HostedWebSearchTool();

        var result = webSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<WebSearchTool>(result);
        Assert.NotNull(tool);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedWebSearchToolWithAdditionalProperties_ProducesValidWebSearchTool()
    {
        var location = WebSearchToolLocation.CreateApproximateLocation("US", "Region", "City", "UTC");
        var webSearchTool = new HostedWebSearchToolWithProperties(new Dictionary<string, object?>
        {
            [nameof(WebSearchTool.UserLocation)] = location,
            [nameof(WebSearchTool.SearchContextSize)] = WebSearchToolContextSize.High
        });

        var result = webSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);

        var tool = Assert.IsType<WebSearchTool>(result);
        Assert.Equal(WebSearchToolContextSize.High, tool.SearchContextSize);
        Assert.NotNull(tool);

        var approximateLocation = Assert.IsType<WebSearchToolApproximateLocation>(tool.UserLocation);
        Assert.Equal(location.Country, approximateLocation.Country);
        Assert.Equal(location.Region, approximateLocation.Region);
        Assert.Equal(location.City, approximateLocation.City);
        Assert.Equal(location.Timezone, approximateLocation.Timezone);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedFileSearchTool_ProducesValidFileSearchTool()
    {
        var fileSearchTool = new HostedFileSearchTool { MaximumResultCount = 10 };

        var result = fileSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<FileSearchTool>(result);
        Assert.Empty(tool.VectorStoreIds);
        Assert.Equal(fileSearchTool.MaximumResultCount, tool.MaxResultCount);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedFileSearchToolWithVectorStores_ProducesValidFileSearchTool()
    {
        var vectorStoreContent = new HostedVectorStoreContent("vector-store-123");
        var fileSearchTool = new HostedFileSearchTool
        {
            Inputs = [vectorStoreContent]
        };

        var result = fileSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<FileSearchTool>(result);
        Assert.Single(tool.VectorStoreIds);
        Assert.Equal(vectorStoreContent.VectorStoreId, tool.VectorStoreIds[0]);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedFileSearchToolWithMaxResults_ProducesValidFileSearchTool()
    {
        var fileSearchTool = new HostedFileSearchTool
        {
            MaximumResultCount = 10
        };

        var result = fileSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<FileSearchTool>(result);
        Assert.Equal(10, tool.MaxResultCount);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedFileSearchToolWithAdditionalProperties_ProducesValidFileSearchTool()
    {
        var rankingOptions = new FileSearchToolRankingOptions { ScoreThreshold = 0.5f };
        var filters = BinaryData.FromString("{\"type\":\"eq\",\"key\":\"status\",\"value\":\"published\"}");
        var fileSearchTool = new HostedFileSearchTool(new Dictionary<string, object?>
        {
            [nameof(FileSearchTool.RankingOptions)] = rankingOptions,
            [nameof(FileSearchTool.Filters)] = filters
        })
        {
            MaximumResultCount = 15
        };

        var result = fileSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<FileSearchTool>(result);
        Assert.NotNull(tool.RankingOptions);
        Assert.Equal(0.5f, tool.RankingOptions.ScoreThreshold);
        Assert.NotNull(tool.Filters);
        Assert.Equal(15, tool.MaxResultCount);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedCodeInterpreterTool_ProducesValidCodeInterpreterTool()
    {
        var codeTool = new HostedCodeInterpreterTool();

        var result = codeTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<CodeInterpreterTool>(result);
        Assert.NotNull(tool.Container);
        Assert.NotNull(tool.Container.ContainerConfiguration);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedCodeInterpreterToolWithFiles_ProducesValidCodeInterpreterTool()
    {
        var fileContent = new HostedFileContent("file-123");
        var codeTool = new HostedCodeInterpreterTool
        {
            Inputs = [fileContent]
        };

        var result = codeTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<CodeInterpreterTool>(result);
        var autoContainerConfig = Assert.IsType<AutomaticCodeInterpreterToolContainerConfiguration>(tool.Container.ContainerConfiguration);
        Assert.Single(autoContainerConfig.FileIds);
        Assert.Equal(fileContent.FileId, autoContainerConfig.FileIds[0]);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedImageGenerationTool_ProducesValidImageGenerationTool()
    {
        var imageGenTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions { MediaType = "image/png" }
        };

        var result = imageGenTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<ImageGenerationTool>(result);
        Assert.NotNull(tool);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedImageGenerationToolWithoutMediaType_PreservesUnspecifiedOutputFileFormat()
    {
        var imageGenTool = new HostedImageGenerationTool();

        var result = imageGenTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<ImageGenerationTool>(result);
        Assert.Null(tool.OutputFileFormat);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedImageGenerationToolWithOptions_ProducesValidImageGenerationTool()
    {
        var imageGenTool = new HostedImageGenerationTool
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                MediaType = "image/png",
                ImageSize = new System.Drawing.Size(1024, 1024),
                StreamingCount = 2
            }
        };

        var result = imageGenTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<ImageGenerationTool>(result);
        Assert.Equal("gpt-image-1", tool.Model);
        Assert.Equal(ImageGenerationToolOutputFileFormat.Png, tool.OutputFileFormat);
        Assert.NotNull(tool.Size);
        Assert.Equal(2, tool.PartialImageCount);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedImageGenerationToolWithAdditionalProperties_ProducesValidImageGenerationTool()
    {
        var imageGenTool = new HostedImageGenerationTool(new Dictionary<string, object?>
        {
            [nameof(ImageGenerationTool.Background)] = ImageGenerationToolBackground.Transparent,
            [nameof(ImageGenerationTool.InputFidelity)] = ImageGenerationToolInputFidelity.High,
            [nameof(ImageGenerationTool.ModerationLevel)] = ImageGenerationToolModerationLevel.Low,
            [nameof(ImageGenerationTool.OutputCompressionFactor)] = 50,
            [nameof(ImageGenerationTool.Quality)] = ImageGenerationToolQuality.High
        })
        {
            Options = new ImageGenerationOptions
            {
                ModelId = "gpt-image-1",
                MediaType = "image/jpeg",
            }
        };

        var result = imageGenTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<ImageGenerationTool>(result);
        Assert.Equal("gpt-image-1", tool.Model);
        Assert.Equal(ImageGenerationToolOutputFileFormat.Jpeg, tool.OutputFileFormat);
        Assert.Equal(ImageGenerationToolBackground.Transparent, tool.Background);
        Assert.Equal(ImageGenerationToolInputFidelity.High, tool.InputFidelity);
        Assert.Equal(ImageGenerationToolModerationLevel.Low, tool.ModerationLevel);
        Assert.Equal(50, tool.OutputCompressionFactor);
        Assert.Equal(ImageGenerationToolQuality.High, tool.Quality);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedImageGenerationToolWithInputImageMask_ProducesValidImageGenerationTool()
    {
        var inputImageMask = new ImageGenerationToolInputImageMask(
            new Uri(new DataContent((byte[])[0x89, 0x50, 0x4E, 0x47], "image/png").Uri));

        var imageGenTool = new HostedImageGenerationTool(new Dictionary<string, object?>
        {
            [nameof(ImageGenerationTool.InputImageMask)] = inputImageMask
        })
        {
            Options = new ImageGenerationOptions { MediaType = "image/png" }
        };

        var result = imageGenTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<ImageGenerationTool>(result);
        Assert.NotNull(tool.InputImageMask);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerTool_ProducesValidMcpTool()
    {
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000");

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.Equal(new Uri("http://localhost:8000"), tool.ServerUri);
        Assert.Equal("test-server", tool.ServerLabel);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithDescription_ProducesValidMcpTool()
    {
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            ServerDescription = "A test MCP server"
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.Equal("A test MCP server", tool.ServerDescription);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithAuthToken_ProducesValidMcpTool()
    {
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer test-token" }
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.Null(tool.AuthorizationToken);
        Assert.NotNull(tool.Headers);
        Assert.Single(tool.Headers);
        Assert.Equal("Bearer test-token", tool.Headers["Authorization"]);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithAuthTokenAndCustomHeaders_ProducesValidMcpTool()
    {
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-token",
                ["X-Custom-Header"] = "custom-value"
            }
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.Null(tool.AuthorizationToken);
        Assert.NotNull(tool.Headers);
        Assert.Equal(2, tool.Headers.Count);
        Assert.Equal("Bearer test-token", tool.Headers["Authorization"]);
        Assert.Equal("custom-value", tool.Headers["X-Custom-Header"]);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithUri_ProducesValidMcpTool()
    {
        var expectedUri = new Uri("http://localhost:8000");
        var mcpTool = new HostedMcpServerTool("test-server", expectedUri);

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.Equal(expectedUri, tool.ServerUri);
        Assert.Equal("test-server", tool.ServerLabel);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithAllowedTools_ProducesValidMcpTool()
    {
        var allowedTools = new List<string> { "tool1", "tool2", "tool3" };
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            AllowedTools = allowedTools
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.NotNull(tool.AllowedTools);
        Assert.Equal(3, tool.AllowedTools.ToolNames.Count);
        Assert.Contains("tool1", tool.AllowedTools.ToolNames);
        Assert.Contains("tool2", tool.AllowedTools.ToolNames);
        Assert.Contains("tool3", tool.AllowedTools.ToolNames);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithAlwaysRequireApprovalMode_ProducesValidMcpTool()
    {
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.AlwaysRequire
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.NotNull(tool.ToolCallApprovalPolicy);
        Assert.NotNull(tool.ToolCallApprovalPolicy.DefaultPolicy);
        Assert.Equal(DefaultMcpToolCallApprovalPolicy.AlwaysRequireApproval, tool.ToolCallApprovalPolicy.DefaultPolicy);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithNeverRequireApprovalMode_ProducesValidMcpTool()
    {
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.NotNull(tool.ToolCallApprovalPolicy);
        Assert.NotNull(tool.ToolCallApprovalPolicy.DefaultPolicy);
        Assert.Equal(DefaultMcpToolCallApprovalPolicy.NeverRequireApproval, tool.ToolCallApprovalPolicy.DefaultPolicy);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolWithRequireSpecificApprovalMode_ProducesValidMcpTool()
    {
        var alwaysRequireTools = new List<string> { "tool1", "tool2" };
        var neverRequireTools = new List<string> { "tool3" };
        var approvalMode = HostedMcpServerToolApprovalMode.RequireSpecific(alwaysRequireTools, neverRequireTools);
        var mcpTool = new HostedMcpServerTool("test-server", "http://localhost:8000")
        {
            ApprovalMode = approvalMode
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.NotNull(tool.ToolCallApprovalPolicy);
        Assert.NotNull(tool.ToolCallApprovalPolicy.CustomPolicy);
        Assert.NotNull(tool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval);
        Assert.NotNull(tool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval);
        Assert.Equal(2, tool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval.ToolNames.Count);
        Assert.Single(tool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval.ToolNames);
        Assert.Contains("tool1", tool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval.ToolNames);
        Assert.Contains("tool2", tool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval.ToolNames);
        Assert.Contains("tool3", tool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval.ToolNames);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedMcpServerToolConnector_ExtractsAuthToken()
    {
        var mcpTool = new HostedMcpServerTool("calendar", "connector_googlecalendar")
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer connector-token" }
        };

        var result = mcpTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var tool = Assert.IsType<McpTool>(result);
        Assert.Equal("connector-token", tool.AuthorizationToken);

        // For connectors, headers should not be set - only AuthorizationToken
        Assert.Empty(tool.Headers);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithUnknownToolType_ReturnsNull()
    {
        var unknownTool = new UnknownAITool();

        var result = unknownTool.AsOpenAIResponseTool();

        Assert.Null(result);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedToolSearchTool_ProducesValidToolSearchTool()
    {
        var toolSearchTool = new HostedToolSearchTool();

        var result = toolSearchTool.AsOpenAIResponseTool();

        Assert.NotNull(result);
        var json = ModelReaderWriter.Write(result, ModelReaderWriterOptions.Json).ToString();
        Assert.Contains("\"type\"", json);
        Assert.Contains("tool_search", json);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithHostedToolSearchTool_ProducesNewInstanceEachTime()
    {
        var result1 = new HostedToolSearchTool().AsOpenAIResponseTool();
        var result2 = new HostedToolSearchTool().AsOpenAIResponseTool();

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void AsOpenAIResponseTool_WithNullTool_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("tool", () => ((AITool)null!).AsOpenAIResponseTool());
    }

    [Fact]
    public void AsOpenAIConversationFunctionTool_ProducesValidInstance()
    {
        var tool = _testFunction.AsOpenAIRealtimeFunctionTool();

        Assert.NotNull(tool);
        Assert.Equal("test_function", tool.FunctionName);
        Assert.Equal("A test function for conversion", tool.FunctionDescription);
        ValidateSchemaParameters(tool.FunctionParameters);
    }

    /// <summary>Helper method to validate function parameters match our schema.</summary>
    private static void ValidateSchemaParameters(BinaryData parameters)
    {
        Assert.NotNull(parameters);

        using var jsonDoc = JsonDocument.Parse(parameters);
        var root = jsonDoc.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("name", out var nameProperty));
        Assert.Equal("string", nameProperty.GetProperty("type").GetString());
        Assert.Equal("The name parameter", nameProperty.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsOpenAIChatMessages_ProducesExpectedOutput(bool withOptions)
    {
        Assert.Throws<ArgumentNullException>("messages", () => ((IEnumerable<ChatMessage>)null!).AsOpenAIChatMessages());

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Hello") { AuthorName = "Jane" },
            new(ChatRole.Assistant,
            [
                new TextContent("Hi there!"),
                new FunctionCallContent("callid123", "SomeFunction", new Dictionary<string, object?>
                {
                    ["param1"] = "value1",
                    ["param2"] = 42
                }),
            ]) { AuthorName = "!@#$%John Smith^*)" },
            new(ChatRole.Tool, [new FunctionResultContent("callid123", "theresult")]),
            new(ChatRole.Assistant, "The answer is 42.") { AuthorName = "@#$#$@$" },
        ];

        ChatOptions? options = withOptions ? new ChatOptions { Instructions = "You talk like a parrot." } : null;

        var convertedMessages = messages.AsOpenAIChatMessages(options).ToArray();

        int index = 0;
        if (withOptions)
        {
            Assert.Equal(6, convertedMessages.Length);

            index = 1;
            SystemChatMessage instructionsMessage = Assert.IsType<SystemChatMessage>(convertedMessages[0], exactMatch: false);
            Assert.Equal("You talk like a parrot.", Assert.Single(instructionsMessage.Content).Text);
        }
        else
        {
            Assert.Equal(5, convertedMessages.Length);
        }

        SystemChatMessage m0 = Assert.IsType<SystemChatMessage>(convertedMessages[index], exactMatch: false);
        Assert.Equal("You are a helpful assistant.", Assert.Single(m0.Content).Text);

        UserChatMessage m1 = Assert.IsType<UserChatMessage>(convertedMessages[index + 1], exactMatch: false);
        Assert.Equal("Hello", Assert.Single(m1.Content).Text);
        Assert.Equal("Jane", m1.ParticipantName);

        AssistantChatMessage m2 = Assert.IsType<AssistantChatMessage>(convertedMessages[index + 2], exactMatch: false);
        Assert.Single(m2.Content);
        Assert.Equal("Hi there!", m2.Content[0].Text);
        var tc = Assert.Single(m2.ToolCalls);
        Assert.Equal("callid123", tc.Id);
        Assert.Equal("SomeFunction", tc.FunctionName);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["param1"] = "value1",
            ["param2"] = 42
        }), JsonElement.Parse(tc.FunctionArguments.ToMemory().Span)));
        Assert.Equal("JohnSmith", m2.ParticipantName);

        ToolChatMessage m3 = Assert.IsType<ToolChatMessage>(convertedMessages[index + 3], exactMatch: false);
        Assert.Equal("callid123", m3.ToolCallId);
        Assert.Equal("theresult", Assert.Single(m3.Content).Text);

        AssistantChatMessage m4 = Assert.IsType<AssistantChatMessage>(convertedMessages[index + 4], exactMatch: false);
        Assert.Equal("The answer is 42.", Assert.Single(m4.Content).Text);
        Assert.Null(m4.ParticipantName);
    }

    [Fact]
    public void AsOpenAIResponseItems_ProducesExpectedOutput()
    {
        Assert.Throws<ArgumentNullException>("messages", () => ((IEnumerable<ChatMessage>)null!).AsOpenAIResponseItems());

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant,
            [
                new TextContent("Hi there!"),
                new FunctionCallContent("callid123", "SomeFunction", new Dictionary<string, object?>
                {
                    ["param1"] = "value1",
                    ["param2"] = 42
                }),
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("callid123", "theresult")]),
            new(ChatRole.Assistant, "The answer is 42."),
        ];

        var convertedItems = messages.AsOpenAIResponseItems().ToArray();

        Assert.Equal(6, convertedItems.Length);

        MessageResponseItem m0 = Assert.IsAssignableFrom<MessageResponseItem>(convertedItems[0]);
        Assert.Equal("You are a helpful assistant.", Assert.Single(m0.Content).Text);

        MessageResponseItem m1 = Assert.IsAssignableFrom<MessageResponseItem>(convertedItems[1]);
        Assert.Equal(OpenAI.Responses.MessageRole.User, m1.Role);
        Assert.Equal("Hello", Assert.Single(m1.Content).Text);

        MessageResponseItem m2 = Assert.IsAssignableFrom<MessageResponseItem>(convertedItems[2]);
        Assert.Equal(OpenAI.Responses.MessageRole.Assistant, m2.Role);
        Assert.Equal("Hi there!", Assert.Single(m2.Content).Text);

        FunctionCallResponseItem m3 = Assert.IsAssignableFrom<FunctionCallResponseItem>(convertedItems[3]);
        Assert.Equal("callid123", m3.CallId);
        Assert.Equal("SomeFunction", m3.FunctionName);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["param1"] = "value1",
            ["param2"] = 42
        }), JsonElement.Parse(m3.FunctionArguments.ToMemory().Span)));

        FunctionCallOutputResponseItem m4 = Assert.IsAssignableFrom<FunctionCallOutputResponseItem>(convertedItems[4]);
        Assert.Equal("callid123", m4.CallId);
        Assert.Equal("theresult", m4.FunctionOutput);

        MessageResponseItem m5 = Assert.IsAssignableFrom<MessageResponseItem>(convertedItems[5]);
        Assert.Equal(OpenAI.Responses.MessageRole.Assistant, m5.Role);
        Assert.Equal("The answer is 42.", Assert.Single(m5.Content).Text);
    }

    [Fact]
    public void AsOpenAIResponseItems_RoundtripsRawRepresentation()
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.User,
            [
                new TextContent("Hello, "),
                new AIContent { RawRepresentation = ResponseItem.CreateWebSearchCallItem() },
                new AIContent { RawRepresentation = ResponseItem.CreateReferenceItem("123") },
                new TextContent("World"),
                new TextContent("!"),
            ]),
            new(ChatRole.Assistant,
            [
                new TextContent("Hi!"),
                new AIContent { RawRepresentation = ResponseItem.CreateReasoningItem("text") },
            ]),
            new(ChatRole.User,
            [
                new AIContent { RawRepresentation = ResponseItem.CreateSystemMessageItem("test") },
            ]),
        ];

        var items = messages.AsOpenAIResponseItems().ToArray();

        Assert.Equal(7, items.Length);
        Assert.Equal("Hello, ", ((MessageResponseItem)items[0]).Content[0].Text);
        Assert.Same(messages[0].Contents[1].RawRepresentation, items[1]);
        Assert.Same(messages[0].Contents[2].RawRepresentation, items[2]);
        Assert.Equal("World", ((MessageResponseItem)items[3]).Content[0].Text);
        Assert.Equal("!", ((MessageResponseItem)items[3]).Content[1].Text);
        Assert.Equal("Hi!", ((MessageResponseItem)items[4]).Content[0].Text);
        Assert.Same(messages[1].Contents[1].RawRepresentation, items[5]);
        Assert.Same(messages[2].Contents[0].RawRepresentation, items[6]);
    }

    [Fact]
    public void AsChatResponse_ConvertsOpenAIChatCompletion()
    {
        Assert.Throws<ArgumentNullException>("chatCompletion", () => ((ChatCompletion)null!).AsChatResponse());

        ChatCompletion cc = OpenAIChatModelFactory.ChatCompletion(
            "id", OpenAI.Chat.ChatFinishReason.Length, null, null,
            [ChatToolCall.CreateFunctionToolCall("id", "functionName", BinaryData.FromString("test"))],
            ChatMessageRole.User, null, null, null, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "model123", null, OpenAIChatModelFactory.ChatTokenUsage(2, 1, 3));
        cc.Content.Add(ChatMessageContentPart.CreateTextPart("Hello, world!"));
        cc.Content.Add(ChatMessageContentPart.CreateImagePart(new Uri("http://example.com/image.png")));

        ChatResponse response = cc.AsChatResponse();

        Assert.Equal("id", response.ResponseId);
        Assert.Equal(ChatFinishReason.Length, response.FinishReason);
        Assert.Equal("model123", response.ModelId);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), response.CreatedAt);
        Assert.NotNull(response.Usage);
        Assert.Equal(1, response.Usage.InputTokenCount);
        Assert.Equal(2, response.Usage.OutputTokenCount);
        Assert.Equal(3, response.Usage.TotalTokenCount);

        ChatMessage message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.User, message.Role);

        Assert.Equal(3, message.Contents.Count);
        Assert.Equal("Hello, world!", Assert.IsType<TextContent>(message.Contents[0], exactMatch: false).Text);
        Assert.Equal("http://example.com/image.png", Assert.IsType<UriContent>(message.Contents[1], exactMatch: false).Uri.ToString());
        Assert.Equal("functionName", Assert.IsType<FunctionCallContent>(message.Contents[2], exactMatch: false).Name);
    }

    [Fact]
    public async Task AsChatResponse_ConvertsOpenAIStreamingChatCompletionUpdates()
    {
        Assert.Throws<ArgumentNullException>("chatCompletionUpdates", () => ((IAsyncEnumerable<StreamingChatCompletionUpdate>)null!).AsChatResponseUpdatesAsync());

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in CreateUpdates().AsChatResponseUpdatesAsync())
        {
            updates.Add(update);
        }

        var response = updates.ToChatResponse();

        Assert.Equal("id", response.ResponseId);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal("model123", response.ModelId);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), response.CreatedAt);
        Assert.NotNull(response.Usage);
        Assert.Equal(1, response.Usage.InputTokenCount);
        Assert.Equal(2, response.Usage.OutputTokenCount);
        Assert.Equal(3, response.Usage.TotalTokenCount);

        ChatMessage message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);

        Assert.Equal(3, message.Contents.Count);
        Assert.Equal("Hello, world!", Assert.IsType<TextContent>(message.Contents[0], exactMatch: false).Text);
        Assert.Equal("http://example.com/image.png", Assert.IsType<UriContent>(message.Contents[1], exactMatch: false).Uri.ToString());
        Assert.Equal("functionName", Assert.IsType<FunctionCallContent>(message.Contents[2], exactMatch: false).Name);

        static async IAsyncEnumerable<StreamingChatCompletionUpdate> CreateUpdates()
        {
            await Task.Yield();
            yield return OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                "id",
                new ChatMessageContent(
                    ChatMessageContentPart.CreateTextPart("Hello, world!"),
                    ChatMessageContentPart.CreateImagePart(new Uri("http://example.com/image.png"))),
                null,
                [OpenAIChatModelFactory.StreamingChatToolCallUpdate(0, "id", ChatToolCallKind.Function, "functionName", BinaryData.FromString("test"))],
                ChatMessageRole.Assistant,
                null, null, null, OpenAI.Chat.ChatFinishReason.ToolCalls, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "model123", null, OpenAIChatModelFactory.ChatTokenUsage(2, 1, 3));
        }
    }

    [Fact]
    public void AsChatResponse_ConvertsOpenAIResponse()
    {
        Assert.Throws<ArgumentNullException>("response", () => ((ResponseResult)null!).AsChatResponse());

        // The OpenAI library currently doesn't provide any way to create an OpenAIResponse instance,
        // as all constructors/factory methods currently are internal. Update this test when such functionality is available.
    }

    /// <summary>
    /// Derived type to allow creating StreamingResponseOutputItemDoneUpdate instances for testing.
    /// The base class has internal constructors, but we can derive and set the Item property.
    /// </summary>
    private sealed class TestableStreamingResponseOutputItemDoneUpdate : StreamingResponseOutputItemDoneUpdate
    {
    }

    [Fact]
    public async Task AsChatResponseUpdatesAsync_ConvertsOpenAIStreamingResponseUpdates()
    {
        Assert.Throws<ArgumentNullException>("responseUpdates", () => ((IAsyncEnumerable<StreamingResponseUpdate>)null!).AsChatResponseUpdatesAsync());

        // Create streaming updates with various ResponseItem types
        FunctionCallResponseItem functionCall = ResponseItem.CreateFunctionCallItem("call_abc", "MyFunction", BinaryData.FromString("""{"arg":"value"}"""));
        McpToolCallItem mcpToolCall = ResponseItem.CreateMcpToolCallItem("deepwiki", "ask_question", BinaryData.FromString("""{"query":"hello"}"""));
        mcpToolCall.Id = "mcp_call_123";
        mcpToolCall.ToolOutput = "The answer is 42";
        McpToolCallApprovalRequestItem mcpApprovalRequest = ResponseItem.CreateMcpApprovalRequestItem(
            "mcpr_123",
            "deepwiki",
            "ask_question",
            BinaryData.FromString("""{"repo":"dotnet/extensions"}"""));
        McpToolCallApprovalResponseItem mcpApprovalResponse = ResponseItem.CreateMcpApprovalResponseItem("mcpr_123", approved: true);

        WebSearchCallResponseItem webSearchItem = ResponseItem.CreateWebSearchCallItem();
        webSearchItem.Id = "ws_123";
        FileSearchCallResponseItem fileSearchItem = ModelReaderWriter.Read<FileSearchCallResponseItem>(BinaryData.FromString(
            """{"type":"file_search_call","id":"fs_123","status":"completed","queries":["test query"],"results":[]}"""))!;
#pragma warning disable OPENAICUA001 // OpenAI Computer Use is experimental
        ComputerCallResponseItem computerItem = ModelReaderWriter.Read<ComputerCallResponseItem>(BinaryData.FromString(
            """{"type":"computer_call","id":"cu_123","call_id":"call_456","action":{"type":"screenshot"},"pending_safety_checks":[],"status":"completed"}"""))!;
        ComputerCallOutputResponseItem computerOutput = ModelReaderWriter.Read<ComputerCallOutputResponseItem>(BinaryData.FromString(
            """{"type":"computer_call_output","id":"co_123","call_id":"call_456","output":{"type":"computer_screenshot","image_url":"https://example.com/screenshot.png"}}"""))!;
#pragma warning restore OPENAICUA001

        ApplyPatchCallItem applyPatchItem = ModelReaderWriter.Read<ApplyPatchCallItem>(BinaryData.FromString(
            """{"type":"apply_patch_call","id":"ap_123","call_id":"call_patch_789","status":"completed","patch":"*** hello.txt\n+++ hello.txt\n@@ -0,0 +1 @@\n+Hello World"}"""))!;
        ApplyPatchCallOutputItem applyPatchOutputItem = ModelReaderWriter.Read<ApplyPatchCallOutputItem>(BinaryData.FromString(
            """{"type":"apply_patch_call_output","id":"apo_123","call_id":"call_patch_789","status":"completed","output":"Applied patch successfully."}"""))!;

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in CreateStreamingUpdates().AsChatResponseUpdatesAsync())
        {
            updates.Add(update);
        }

        // Verify we got the expected updates
        Assert.Equal(11, updates.Count);

        // First update should be FunctionCallContent
        FunctionCallContent? fcc = updates[0].Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("call_abc", fcc.CallId);
        Assert.Equal("MyFunction", fcc.Name);

        // Second update should be McpServerToolCallContent + McpServerToolResultContent
        McpServerToolCallContent? mcpToolCallContent = updates[1].Contents.OfType<McpServerToolCallContent>().FirstOrDefault();
        Assert.NotNull(mcpToolCallContent);
        Assert.Equal("mcp_call_123", mcpToolCallContent.CallId);
        Assert.Equal("ask_question", mcpToolCallContent.Name);
        Assert.Equal("deepwiki", mcpToolCallContent.ServerName);
        Assert.Null(mcpToolCallContent.RawRepresentation); // Intentionally null to avoid duplication during roundtrip

        McpServerToolResultContent? mcpToolResultContent = updates[1].Contents.OfType<McpServerToolResultContent>().FirstOrDefault();
        Assert.NotNull(mcpToolResultContent);
        Assert.Equal("mcp_call_123", mcpToolResultContent.CallId);
        Assert.NotNull(mcpToolResultContent.RawRepresentation);
        Assert.Same(mcpToolCall, mcpToolResultContent.RawRepresentation);

        // Third update should be ToolApprovalRequestContent with McpServerToolCallContent
        ToolApprovalRequestContent? approvalRequest = updates[2].Contents.OfType<ToolApprovalRequestContent>().FirstOrDefault();
        Assert.NotNull(approvalRequest);
        Assert.Equal("mcpr_123", approvalRequest.RequestId);
        Assert.NotNull(approvalRequest.RawRepresentation);
        Assert.Same(mcpApprovalRequest, approvalRequest.RawRepresentation);

        McpServerToolCallContent nestedMcpCall = Assert.IsType<McpServerToolCallContent>(approvalRequest.ToolCall);
        Assert.Equal("ask_question", nestedMcpCall.Name);
        Assert.Equal("deepwiki", nestedMcpCall.ServerName);

        // Fourth update should be ToolApprovalResponseContent correlated with request
        ToolApprovalResponseContent? approvalResponse = updates[3].Contents.OfType<ToolApprovalResponseContent>().FirstOrDefault();
        Assert.NotNull(approvalResponse);
        Assert.Equal("mcpr_123", approvalResponse.RequestId);
        Assert.True(approvalResponse.Approved);
        Assert.NotNull(approvalResponse.RawRepresentation);
        Assert.Same(mcpApprovalResponse, approvalResponse.RawRepresentation);

        // The correlated FunctionCall should be McpServerToolCallContent with tool details from the request
        McpServerToolCallContent correlatedMcpCall = Assert.IsType<McpServerToolCallContent>(approvalResponse.ToolCall);
        Assert.Equal("mcpr_123", correlatedMcpCall.CallId);
        Assert.Equal("ask_question", correlatedMcpCall.Name);
        Assert.Equal("deepwiki", correlatedMcpCall.ServerName);
        Assert.NotNull(correlatedMcpCall.Arguments);
        Assert.Equal("dotnet/extensions", correlatedMcpCall.Arguments["repo"]?.ToString());

        // Fifth update: WebSearchCallResponseItem -> WebSearchToolCallContent
        WebSearchToolCallContent? wsToolCall = updates[4].Contents.OfType<WebSearchToolCallContent>().FirstOrDefault();
        Assert.NotNull(wsToolCall);
        Assert.Equal("ws_123", wsToolCall.CallId);
        Assert.Null(wsToolCall.RawRepresentation);

        // Sixth update: WebSearchCallResponseItem -> WebSearchToolResultContent
        WebSearchToolResultContent? wsToolResult = updates[5].Contents.OfType<WebSearchToolResultContent>().FirstOrDefault();
        Assert.NotNull(wsToolResult);
        Assert.Equal("ws_123", wsToolResult.CallId);
        Assert.Same(webSearchItem, wsToolResult.RawRepresentation);

        // Seventh update: FileSearchCallResponseItem -> ToolCallContent + ToolResultContent
        ToolCallContent? fsToolCall = updates[6].Contents.OfType<ToolCallContent>().FirstOrDefault();
        Assert.NotNull(fsToolCall);
        Assert.Equal("fs_123", fsToolCall.CallId);
        Assert.Null(fsToolCall.RawRepresentation);

        ToolResultContent? fsToolResult = updates[6].Contents.OfType<ToolResultContent>().FirstOrDefault();
        Assert.NotNull(fsToolResult);
        Assert.Equal("fs_123", fsToolResult.CallId);
        Assert.Same(fileSearchItem, fsToolResult.RawRepresentation);

        // Eighth update: ComputerCallResponseItem -> ToolCallContent
        ToolCallContent? cuToolCall = updates[7].Contents.OfType<ToolCallContent>().FirstOrDefault();
        Assert.NotNull(cuToolCall);
        Assert.Equal("call_456", cuToolCall.CallId);
        Assert.Same(computerItem, cuToolCall.RawRepresentation);

        // Ninth update: ComputerCallOutputResponseItem -> ToolResultContent
        ToolResultContent? cuToolResult = updates[8].Contents.OfType<ToolResultContent>().FirstOrDefault();
        Assert.NotNull(cuToolResult);
        Assert.Equal("call_456", cuToolResult.CallId);
        Assert.Same(computerOutput, cuToolResult.RawRepresentation);

        // Tenth update: ApplyPatchCallItem -> AIContent (streaming falls through to default)
        AIContent? apCallContent = updates[9].Contents.FirstOrDefault(c => c.RawRepresentation == applyPatchItem);
        Assert.NotNull(apCallContent);
        Assert.Same(applyPatchItem, apCallContent.RawRepresentation);

        // Eleventh update: ApplyPatchCallOutputItem -> AIContent (streaming falls through to default)
        AIContent? apOutputContent = updates[10].Contents.FirstOrDefault(c => c.RawRepresentation == applyPatchOutputItem);
        Assert.NotNull(apOutputContent);
        Assert.Same(applyPatchOutputItem, apOutputContent.RawRepresentation);

        async IAsyncEnumerable<StreamingResponseUpdate> CreateStreamingUpdates()
        {
            await Task.Yield();

            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = functionCall };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = mcpToolCall };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = mcpApprovalRequest };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = mcpApprovalResponse };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = webSearchItem };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = fileSearchItem };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = computerItem };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = computerOutput };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = applyPatchItem };
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = applyPatchOutputItem };
        }
    }

    [Fact]
    public async Task AsChatResponseUpdatesAsync_McpToolCallApprovalResponseItem_WithoutCorrelatedRequest_FallsBackToAIContent()
    {
        // Create an approval response without a matching request in the stream.
        McpToolCallApprovalResponseItem mcpApprovalResponse = ResponseItem.CreateMcpApprovalResponseItem("unknown_request_id", approved: true);

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in CreateStreamingUpdates().AsChatResponseUpdatesAsync())
        {
            updates.Add(update);
        }

        Assert.Single(updates);

        // Should NOT have a ToolApprovalResponseContent since there was no correlated request
        Assert.Empty(updates[0].Contents.OfType<ToolApprovalResponseContent>());

        // Should have a generic AIContent with RawRepresentation set to the response item
        AIContent? genericContent = updates[0].Contents.FirstOrDefault(c => c.RawRepresentation == mcpApprovalResponse);
        Assert.NotNull(genericContent);
        Assert.IsNotType<ToolApprovalResponseContent>(genericContent);
        Assert.Same(mcpApprovalResponse, genericContent.RawRepresentation);

        async IAsyncEnumerable<StreamingResponseUpdate> CreateStreamingUpdates()
        {
            await Task.Yield();
            yield return new TestableStreamingResponseOutputItemDoneUpdate { Item = mcpApprovalResponse };
        }
    }

    [Fact]
    public void AsChatMessages_FromOpenAIChatMessages_ProducesExpectedOutput()
    {
        Assert.Throws<ArgumentNullException>("messages", () => ((IEnumerable<OpenAI.Chat.ChatMessage>)null!).AsChatMessages().ToArray());

        List<OpenAI.Chat.ChatMessage> openAIMessages =
        [
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage("Hello"),
            new AssistantChatMessage(ChatMessageContentPart.CreateTextPart("Hi there!")),
            new ToolChatMessage("call456", "Function output")
        ];

        var convertedMessages = openAIMessages.AsChatMessages().ToArray();

        Assert.Equal(4, convertedMessages.Length);

        Assert.Equal("You are a helpful assistant.", convertedMessages[0].Text);
        Assert.Equal("Hello", convertedMessages[1].Text);
        Assert.Equal("Hi there!", convertedMessages[2].Text);
        Assert.Equal("Function output", convertedMessages[3].Contents.OfType<FunctionResultContent>().First().Result);
    }

    [Fact]
    public void AsChatMessages_FromResponseItems_WithNullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("items", () => ((IEnumerable<ResponseItem>)null!).AsChatMessages());
    }

    [Fact]
    public void AsChatMessages_FromResponseItems_ProducesExpectedOutput()
    {
        List<ChatMessage> inputMessages =
        [
            new(ChatRole.Assistant, "Hi there!")
        ];

        var responseItems = inputMessages.AsOpenAIResponseItems().ToArray();

        var convertedMessages = responseItems.AsChatMessages().ToArray();

        Assert.Single(convertedMessages);

        var message = convertedMessages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal("Hi there!", message.Text);
    }

    [Fact]
    public void AsChatMessages_FromResponseItems_WithEmptyCollection_ReturnsEmptyCollection()
    {
        var convertedMessages = Array.Empty<ResponseItem>().AsChatMessages().ToArray();
        Assert.Empty(convertedMessages);
    }

    [Fact]
    public void AsChatMessages_FromResponseItems_WithFunctionCall_HandlesCorrectly()
    {
        List<ChatMessage> inputMessages =
        [
            new(ChatRole.Assistant,
            [
                new TextContent("I'll call a function."),
                new FunctionCallContent("call123", "TestFunction", new Dictionary<string, object?> { ["param"] = "value" })
            ])
        ];

        var responseItems = inputMessages.AsOpenAIResponseItems().ToArray();
        var convertedMessages = responseItems.AsChatMessages().ToArray();

        Assert.Single(convertedMessages);

        var message = convertedMessages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);

        var textContent = message.Contents.OfType<TextContent>().FirstOrDefault();
        var functionCall = message.Contents.OfType<FunctionCallContent>().FirstOrDefault();

        Assert.NotNull(textContent);
        Assert.Equal("I'll call a function.", textContent.Text);

        Assert.NotNull(functionCall);
        Assert.Equal("call123", functionCall.CallId);
        Assert.Equal("TestFunction", functionCall.Name);
        Assert.Equal("value", functionCall.Arguments!["param"]?.ToString());
    }

    [Fact]
    public void AsChatMessages_FromResponseItems_AllContentTypes_SetsRawRepresentation()
    {
        // Create ResponseItems of various types that ToChatMessages handles.
        // Each type should roundtrip with RawRepresentation set.
        MessageResponseItem assistantItem = ResponseItem.CreateAssistantMessageItem("Hello from the assistant!");
        ReasoningResponseItem reasoningItem = ResponseItem.CreateReasoningItem("This is reasoning text");
        FunctionCallResponseItem functionCallItem = ResponseItem.CreateFunctionCallItem("call_abc", "MyFunction", BinaryData.FromString("""{"arg": "value"}"""));
        FunctionCallOutputResponseItem functionOutputItem = ResponseItem.CreateFunctionCallOutputItem("call_abc", "function result output");
        McpToolCallItem mcpToolCallItem = ResponseItem.CreateMcpToolCallItem("deepwiki", "ask_question", BinaryData.FromString("""{"query":"hello"}"""));
        mcpToolCallItem.Id = "mcp_call_123";
        mcpToolCallItem.ToolOutput = "The answer is 42";
        McpToolCallApprovalRequestItem mcpApprovalRequestItem = ResponseItem.CreateMcpApprovalRequestItem(
            "mcpr_123",
            "deepwiki",
            "ask_question",
            BinaryData.FromString("""{"repoName":"dotnet/extensions"}"""));

        // Use matching ID so response can correlate with the request
        McpToolCallApprovalResponseItem mcpApprovalResponseItem = ResponseItem.CreateMcpApprovalResponseItem("mcpr_123", approved: true);

        WebSearchCallResponseItem webSearchItem = ResponseItem.CreateWebSearchCallItem();
        webSearchItem.Id = "ws_123";
        FileSearchCallResponseItem fileSearchItem = ModelReaderWriter.Read<FileSearchCallResponseItem>(BinaryData.FromString(
            """{"type":"file_search_call","id":"fs_123","status":"completed","queries":["test query"],"results":[]}"""))!;
#pragma warning disable OPENAICUA001 // OpenAI Computer Use is experimental
        ComputerCallResponseItem computerItem = ModelReaderWriter.Read<ComputerCallResponseItem>(BinaryData.FromString(
            """{"type":"computer_call","id":"cu_123","call_id":"call_456","action":{"type":"screenshot"},"pending_safety_checks":[],"status":"completed"}"""))!;
        ComputerCallOutputResponseItem computerOutput = ModelReaderWriter.Read<ComputerCallOutputResponseItem>(BinaryData.FromString(
            """{"type":"computer_call_output","id":"co_123","call_id":"call_456","output":{"type":"computer_screenshot","image_url":"https://example.com/screenshot.png"}}"""))!;
#pragma warning restore OPENAICUA001

        ApplyPatchCallItem applyPatchItem = ModelReaderWriter.Read<ApplyPatchCallItem>(BinaryData.FromString(
            """{"type":"apply_patch_call","id":"ap_123","call_id":"call_patch_789","status":"completed","patch":"*** hello.txt\n+++ hello.txt\n@@ -0,0 +1 @@\n+Hello World"}"""))!;
        ApplyPatchCallOutputItem applyPatchOutputItem = ModelReaderWriter.Read<ApplyPatchCallOutputItem>(BinaryData.FromString(
            """{"type":"apply_patch_call_output","id":"apo_123","call_id":"call_patch_789","status":"completed","output":"Applied patch successfully."}"""))!;

        ResponseItem[] items =
        [
            assistantItem, reasoningItem, functionCallItem, functionOutputItem,
            mcpToolCallItem, mcpApprovalRequestItem, mcpApprovalResponseItem,
            webSearchItem, fileSearchItem, computerItem, computerOutput,
            applyPatchItem, applyPatchOutputItem,
        ];

        // Convert to ChatMessages
        ChatMessage[] messages = items.AsChatMessages().ToArray();

        // All items should be grouped into a single assistant message
        Assert.Single(messages);
        ChatMessage message = messages[0];
        Assert.Equal(ChatRole.Assistant, message.Role);

        // The message itself should have RawRepresentation from MessageResponseItem
        Assert.NotNull(message.RawRepresentation);
        Assert.Same(assistantItem, message.RawRepresentation);

        // Verify each content type has RawRepresentation set

        // 1. MessageResponseItem -> TextContent with ResponseContentPart as RawRepresentation
        TextContent? textContent = message.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Equal("Hello from the assistant!", textContent.Text);
        Assert.NotNull(textContent.RawRepresentation);
        Assert.IsAssignableFrom<ResponseContentPart>(textContent.RawRepresentation);

        // 2. ReasoningResponseItem -> TextReasoningContent
        TextReasoningContent? reasoningContent = message.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoningContent);
        Assert.Equal("This is reasoning text", reasoningContent.Text);
        Assert.NotNull(reasoningContent.RawRepresentation);
        Assert.Same(reasoningItem, reasoningContent.RawRepresentation);

        // 3. FunctionCallResponseItem -> FunctionCallContent
        FunctionCallContent? functionCallContent = message.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(functionCallContent);
        Assert.Equal("call_abc", functionCallContent.CallId);
        Assert.Equal("MyFunction", functionCallContent.Name);
        Assert.NotNull(functionCallContent.RawRepresentation);
        Assert.Same(functionCallItem, functionCallContent.RawRepresentation);

        // 4. FunctionCallOutputResponseItem -> FunctionResultContent
        FunctionResultContent? functionResultContent = message.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.NotNull(functionResultContent);
        Assert.Equal("call_abc", functionResultContent.CallId);
        Assert.Equal("function result output", functionResultContent.Result);
        Assert.NotNull(functionResultContent.RawRepresentation);
        Assert.Same(functionOutputItem, functionResultContent.RawRepresentation);

        // 5. McpToolCallItem -> McpServerToolCallContent + McpServerToolResultContent
        // Note: AddMcpToolCallContent creates both contents; RawRepresentation is only on the result, not the call
        McpServerToolCallContent? mcpToolCall = message.Contents.OfType<McpServerToolCallContent>().FirstOrDefault(c => c.CallId == "mcp_call_123");
        Assert.NotNull(mcpToolCall);
        Assert.Equal("mcp_call_123", mcpToolCall.CallId);
        Assert.Equal("ask_question", mcpToolCall.Name);
        Assert.Equal("deepwiki", mcpToolCall.ServerName);
        Assert.Null(mcpToolCall.RawRepresentation); // Intentionally null to avoid duplication during roundtrip

        McpServerToolResultContent? mcpToolResult = message.Contents.OfType<McpServerToolResultContent>().FirstOrDefault(c => c.CallId == "mcp_call_123");
        Assert.NotNull(mcpToolResult);
        Assert.Equal("mcp_call_123", mcpToolResult.CallId);
        Assert.NotNull(mcpToolResult.RawRepresentation);
        Assert.Same(mcpToolCallItem, mcpToolResult.RawRepresentation);

        // 6. McpToolCallApprovalRequestItem -> ToolApprovalRequestContent
        ToolApprovalRequestContent? approvalRequestContent = message.Contents.OfType<ToolApprovalRequestContent>().FirstOrDefault();
        Assert.NotNull(approvalRequestContent);
        Assert.Equal("mcpr_123", approvalRequestContent.RequestId);
        Assert.NotNull(approvalRequestContent.RawRepresentation);
        Assert.Same(mcpApprovalRequestItem, approvalRequestContent.RawRepresentation);

        // The nested FunctionCall should be McpServerToolCallContent
        McpServerToolCallContent nestedMcpCall = Assert.IsType<McpServerToolCallContent>(approvalRequestContent.ToolCall);
        Assert.Equal("ask_question", nestedMcpCall.Name);
        Assert.Equal("deepwiki", nestedMcpCall.ServerName);
        Assert.NotNull(nestedMcpCall.RawRepresentation);
        Assert.Same(mcpApprovalRequestItem, nestedMcpCall.RawRepresentation);

        // 7. McpToolCallApprovalResponseItem -> ToolApprovalResponseContent (correlated with request)
        ToolApprovalResponseContent? approvalResponseContent = message.Contents.OfType<ToolApprovalResponseContent>().FirstOrDefault();
        Assert.NotNull(approvalResponseContent);
        Assert.Equal("mcpr_123", approvalResponseContent.RequestId);
        Assert.True(approvalResponseContent.Approved);
        Assert.NotNull(approvalResponseContent.RawRepresentation);
        Assert.Same(mcpApprovalResponseItem, approvalResponseContent.RawRepresentation);

        // The correlated FunctionCall should be McpServerToolCallContent with tool details from the request
        McpServerToolCallContent correlatedMcpCall = Assert.IsType<McpServerToolCallContent>(approvalResponseContent.ToolCall);
        Assert.Equal("mcpr_123", correlatedMcpCall.CallId);
        Assert.Equal("ask_question", correlatedMcpCall.Name);
        Assert.Equal("deepwiki", correlatedMcpCall.ServerName);
        Assert.NotNull(correlatedMcpCall.Arguments);
        Assert.Equal("dotnet/extensions", correlatedMcpCall.Arguments["repoName"]?.ToString());

        // 8. WebSearchCallResponseItem -> WebSearchToolCallContent + WebSearchToolResultContent
        WebSearchToolCallContent? webSearchToolCall = message.Contents.OfType<WebSearchToolCallContent>().FirstOrDefault(c => c.CallId == "ws_123");
        Assert.NotNull(webSearchToolCall);
        Assert.Null(webSearchToolCall.RawRepresentation); // Intentionally null to avoid duplication during roundtrip

        WebSearchToolResultContent? webSearchToolResult = message.Contents.OfType<WebSearchToolResultContent>().FirstOrDefault(c => c.CallId == "ws_123");
        Assert.NotNull(webSearchToolResult);
        Assert.Same(webSearchItem, webSearchToolResult.RawRepresentation);

        // 9. FileSearchCallResponseItem -> ToolCallContent + ToolResultContent
        ToolCallContent? fileSearchToolCall = message.Contents.OfType<ToolCallContent>().FirstOrDefault(c => c.CallId == "fs_123");
        Assert.NotNull(fileSearchToolCall);
        Assert.Null(fileSearchToolCall.RawRepresentation);

        ToolResultContent? fileSearchToolResult = message.Contents.OfType<ToolResultContent>().FirstOrDefault(c => c.CallId == "fs_123");
        Assert.NotNull(fileSearchToolResult);
        Assert.Same(fileSearchItem, fileSearchToolResult.RawRepresentation);

        // 10. ComputerCallResponseItem -> ToolCallContent (result comes separately)
        ToolCallContent? computerToolCall = message.Contents.OfType<ToolCallContent>().FirstOrDefault(c => c.CallId == "call_456");
        Assert.NotNull(computerToolCall);
        Assert.Same(computerItem, computerToolCall.RawRepresentation);

        // 11. ComputerCallOutputResponseItem -> ToolResultContent
        ToolResultContent? computerToolResult = message.Contents.OfType<ToolResultContent>().FirstOrDefault(c => c.CallId == "call_456");
        Assert.NotNull(computerToolResult);
        Assert.Same(computerOutput, computerToolResult.RawRepresentation);

        // 12. ApplyPatchCallItem -> ToolCallContent
        ToolCallContent? applyPatchToolCall = message.Contents.OfType<ToolCallContent>().FirstOrDefault(c => c.CallId == "call_patch_789");
        Assert.NotNull(applyPatchToolCall);
        Assert.Same(applyPatchItem, applyPatchToolCall.RawRepresentation);

        // 13. ApplyPatchCallOutputItem -> ToolResultContent
        ToolResultContent? applyPatchToolResult = message.Contents.OfType<ToolResultContent>().FirstOrDefault(c => c.CallId == "call_patch_789");
        Assert.NotNull(applyPatchToolResult);
        Assert.Same(applyPatchOutputItem, applyPatchToolResult.RawRepresentation);
    }

    [Fact]
    public void AsChatMessages_McpToolCallApprovalResponseItem_WithoutCorrelatedRequest_FallsBackToAIContent()
    {
        // Create an approval response without a matching request in the batch.
        // This simulates receiving a response item when we don't have the original request.
        MessageResponseItem assistantItem = ResponseItem.CreateAssistantMessageItem("Hello");
        McpToolCallApprovalResponseItem mcpApprovalResponseItem = ResponseItem.CreateMcpApprovalResponseItem("unknown_request_id", approved: true);

        ResponseItem[] items = [assistantItem, mcpApprovalResponseItem];

        // Convert to ChatMessages
        ChatMessage[] messages = items.AsChatMessages().ToArray();

        Assert.Single(messages);
        ChatMessage message = messages[0];

        // Should NOT have a ToolApprovalResponseContent since there was no correlated request
        Assert.Empty(message.Contents.OfType<ToolApprovalResponseContent>());

        // Should have a generic AIContent with RawRepresentation set to the response item
        AIContent? genericContent = message.Contents.FirstOrDefault(c => c.RawRepresentation == mcpApprovalResponseItem);
        Assert.NotNull(genericContent);
        Assert.IsNotType<ToolApprovalResponseContent>(genericContent);
        Assert.Same(mcpApprovalResponseItem, genericContent.RawRepresentation);
    }

    [Fact]
    public void AsChatMessages_InputImageWithRelativeUri_ProducesAIContentWithRawRepresentation()
    {
        // Create a MessageResponseItem containing an input_image with a relative URI,
        // which fails Uri.TryCreate with UriKind.Absolute.
        MessageResponseItem messageItem = ModelReaderWriter.Read<MessageResponseItem>(BinaryData.FromString(
            """{"type":"message","id":"msg_001","status":"completed","role":"user","content":[{"type":"input_image","image_url":"images/photo.png"}]}"""))!;

        ChatMessage[] messages = new ResponseItem[] { messageItem }.AsChatMessages().ToArray();

        Assert.Single(messages);
        ChatMessage message = messages[0];
        Assert.Equal(ChatRole.User, message.Role);

        // Invalid URI should still produce an AIContent with RawRepresentation set
        AIContent content = Assert.Single(message.Contents);
        Assert.IsType<AIContent>(content, exactMatch: true);
        Assert.NotNull(content.RawRepresentation);
        Assert.IsAssignableFrom<ResponseContentPart>(content.RawRepresentation);
    }

    [Fact]
    public void AsChatMessages_InputImageWithNoProperties_ProducesAIContentWithRawRepresentation()
    {
        // Create a MessageResponseItem containing an input_image with no image_url or file_id.
        MessageResponseItem messageItem = ModelReaderWriter.Read<MessageResponseItem>(BinaryData.FromString(
            """{"type":"message","id":"msg_001","status":"completed","role":"user","content":[{"type":"input_image"}]}"""))!;

        ChatMessage[] messages = new ResponseItem[] { messageItem }.AsChatMessages().ToArray();

        Assert.Single(messages);
        ChatMessage message = messages[0];
        Assert.Equal(ChatRole.User, message.Role);

        // Empty input_image should still produce an AIContent with RawRepresentation set
        AIContent content = Assert.Single(message.Contents);
        Assert.IsType<AIContent>(content, exactMatch: true);
        Assert.NotNull(content.RawRepresentation);
        Assert.IsAssignableFrom<ResponseContentPart>(content.RawRepresentation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsOpenAIResponseItems_McpServerToolContents_RoundtripsToolOutputAndError(bool isError)
    {
        var mcpToolCall = new McpServerToolCallContent("call_123", "get_weather", "weather_server") { Arguments = new Dictionary<string, object?> { ["city"] = "Seattle" } };
        var mcpToolResult = new McpServerToolResultContent("call_123") { Outputs = isError ? [new ErrorContent("error")] : [new TextContent("sunny")] };

        var items = new ChatMessage[] { new(ChatRole.Assistant, [mcpToolCall, mcpToolResult]) }.AsOpenAIResponseItems().ToArray();

        McpToolCallItem mtci = Assert.IsType<McpToolCallItem>(Assert.Single(items));
        Assert.Equal("get_weather", mtci.ToolName);
        Assert.Equal("weather_server", mtci.ServerLabel);

        if (isError)
        {
            Assert.Contains("error", mtci.Error?.ToString());
            Assert.Null(mtci.ToolOutput);
        }
        else
        {
            Assert.Equal("sunny", mtci.ToolOutput);
            Assert.Null(mtci.Error);
        }
    }

    [Fact]
    public void AsOpenAIChatCompletion_WithNullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("response", () => ((ChatResponse)null!).AsOpenAIChatCompletion());
    }

    [Fact]
    public void AsOpenAIChatCompletion_WithMultipleContents_ProducesValidInstance()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new TextContent("Here's an image and some text."),
            new UriContent("https://example.com/image.jpg", "image/jpeg"),
            new DataContent(new byte[] { 1, 2, 3, 4 }, "application/octet-stream")
        ]))
        {
            ResponseId = "multi-content-response",
            ModelId = "gpt-4-vision",
            FinishReason = ChatFinishReason.Stop,
            CreatedAt = new DateTimeOffset(2025, 1, 3, 14, 30, 0, TimeSpan.Zero),
            Usage = new UsageDetails
            {
                InputTokenCount = 25,
                OutputTokenCount = 12,
                TotalTokenCount = 37
            }
        };

        ChatCompletion completion = chatResponse.AsOpenAIChatCompletion();

        Assert.Equal("multi-content-response", completion.Id);
        Assert.Equal("gpt-4-vision", completion.Model);
        Assert.Equal(OpenAI.Chat.ChatFinishReason.Stop, completion.FinishReason);
        Assert.Equal(ChatMessageRole.Assistant, completion.Role);
        Assert.Equal(new DateTimeOffset(2025, 1, 3, 14, 30, 0, TimeSpan.Zero), completion.CreatedAt);

        Assert.NotNull(completion.Usage);
        Assert.Equal(25, completion.Usage.InputTokenCount);
        Assert.Equal(12, completion.Usage.OutputTokenCount);
        Assert.Equal(37, completion.Usage.TotalTokenCount);

        Assert.NotEmpty(completion.Content);
        Assert.Contains(completion.Content, c => c.Text == "Here's an image and some text.");
    }

    [Fact]
    public void AsOpenAIChatCompletion_WithEmptyData_HandlesGracefully()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"));
        var completion = chatResponse.AsOpenAIChatCompletion();

        Assert.NotNull(completion);
        Assert.Equal(ChatMessageRole.Assistant, completion.Role);
        Assert.Equal("Hello", Assert.Single(completion.Content).Text);
        Assert.Empty(completion.ToolCalls);

        var emptyResponse = new ChatResponse([]);
        var emptyCompletion = emptyResponse.AsOpenAIChatCompletion();
        Assert.NotNull(emptyCompletion);
        Assert.Equal(ChatMessageRole.Assistant, emptyCompletion.Role);
    }

    [Fact]
    public void AsOpenAIChatCompletion_WithComplexFunctionCallArguments_SerializesCorrectly()
    {
        var complexArgs = new Dictionary<string, object?>
        {
            ["simpleString"] = "hello",
            ["number"] = 42,
            ["boolean"] = true,
            ["nullValue"] = null,
            ["nestedObject"] = new Dictionary<string, object?>
            {
                ["innerString"] = "world",
                ["innerArray"] = new[] { 1, 2, 3 }
            }
        };

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new TextContent("I'll process this complex data."),
            new FunctionCallContent("process_data", "ProcessComplexData", complexArgs)
        ]))
        {
            ResponseId = "complex-function-call",
            ModelId = "gpt-4",
            FinishReason = ChatFinishReason.ToolCalls
        };

        ChatCompletion completion = chatResponse.AsOpenAIChatCompletion();

        Assert.Equal("complex-function-call", completion.Id);
        Assert.Equal(OpenAI.Chat.ChatFinishReason.ToolCalls, completion.FinishReason);

        var toolCall = Assert.Single(completion.ToolCalls);
        Assert.Equal("process_data", toolCall.Id);
        Assert.Equal("ProcessComplexData", toolCall.FunctionName);

        var deserializedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(toolCall.FunctionArguments.ToMemory().Span);
        Assert.NotNull(deserializedArgs);
        Assert.Equal("hello", deserializedArgs["simpleString"]?.ToString());
        Assert.Equal(42, ((JsonElement)deserializedArgs["number"]!).GetInt32());
        Assert.True(((JsonElement)deserializedArgs["boolean"]!).GetBoolean());
        Assert.Null(deserializedArgs["nullValue"]);

        var nestedObj = (JsonElement)deserializedArgs["nestedObject"]!;
        Assert.Equal("world", nestedObj.GetProperty("innerString").GetString());
        Assert.Equal(3, nestedObj.GetProperty("innerArray").GetArrayLength());
    }

    [Fact]
    public void AsOpenAIChatCompletion_WithDifferentFinishReasons_MapsCorrectly()
    {
        var testCases = new[]
        {
            (ChatFinishReason.Stop, OpenAI.Chat.ChatFinishReason.Stop),
            (ChatFinishReason.Length, OpenAI.Chat.ChatFinishReason.Length),
            (ChatFinishReason.ContentFilter, OpenAI.Chat.ChatFinishReason.ContentFilter),
            (ChatFinishReason.ToolCalls, OpenAI.Chat.ChatFinishReason.ToolCalls)
        };

        foreach (var (inputFinishReason, expectedOpenAIFinishReason) in testCases)
        {
            var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test"))
            {
                FinishReason = inputFinishReason
            };

            var completion = chatResponse.AsOpenAIChatCompletion();
            Assert.Equal(expectedOpenAIFinishReason, completion.FinishReason);
        }
    }

    [Fact]
    public void AsOpenAIChatCompletion_WithDifferentRoles_MapsCorrectly()
    {
        var testCases = new[]
        {
            (ChatRole.Assistant, ChatMessageRole.Assistant),
            (ChatRole.User, ChatMessageRole.User),
            (ChatRole.System, ChatMessageRole.System),
            (ChatRole.Tool, ChatMessageRole.Tool)
        };

        foreach (var (inputRole, expectedOpenAIRole) in testCases)
        {
            var chatResponse = new ChatResponse(new ChatMessage(inputRole, "Test"));
            var completion = chatResponse.AsOpenAIChatCompletion();
            Assert.Equal(expectedOpenAIRole, completion.Role);
        }
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithNullArgument_ThrowsArgumentNullException()
    {
        var asyncEnumerable = ((IAsyncEnumerable<ChatResponseUpdate>)null!).AsOpenAIStreamingChatCompletionUpdatesAsync();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await asyncEnumerable.GetAsyncEnumerator().MoveNextAsync());
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithEmptyCollection_ReturnsEmptySequence()
    {
        var updates = new List<ChatResponseUpdate>();
        var result = new List<StreamingChatCompletionUpdate>();

        await foreach (var update in CreateAsyncEnumerable(updates).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Empty(result);
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithRawRepresentation_ReturnsOriginal()
    {
        var originalUpdate = OpenAIChatModelFactory.StreamingChatCompletionUpdate(
            "test-id",
            new ChatMessageContent(ChatMessageContentPart.CreateTextPart("Hello")),
            role: ChatMessageRole.Assistant,
            finishReason: OpenAI.Chat.ChatFinishReason.Stop,
            createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            model: "gpt-3.5-turbo");

        var responseUpdate = new ChatResponseUpdate(ChatRole.Assistant, "Hello")
        {
            RawRepresentation = originalUpdate
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Single(result);
        Assert.Same(originalUpdate, result[0]);
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithTextContent_CreatesValidUpdate()
    {
        var responseUpdate = new ChatResponseUpdate(ChatRole.Assistant, "Hello, world!")
        {
            ResponseId = "response-123",
            MessageId = "message-456",
            ModelId = "gpt-4",
            FinishReason = ChatFinishReason.Stop,
            CreatedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Single(result);
        var streamingUpdate = result[0];

        Assert.Equal("response-123", streamingUpdate.CompletionId);
        Assert.Equal("gpt-4", streamingUpdate.Model);
        Assert.Equal(OpenAI.Chat.ChatFinishReason.Stop, streamingUpdate.FinishReason);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero), streamingUpdate.CreatedAt);
        Assert.Equal(ChatMessageRole.Assistant, streamingUpdate.Role);
        Assert.Equal("Hello, world!", Assert.Single(streamingUpdate.ContentUpdate).Text);
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithUsageContent_CreatesUpdateWithUsage()
    {
        var responseUpdate = new ChatResponseUpdate
        {
            ResponseId = "response-123",
            Contents =
            [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = 10,
                    OutputTokenCount = 20,
                    TotalTokenCount = 30
                })
            ]
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Single(result);
        var streamingUpdate = result[0];

        Assert.Equal("response-123", streamingUpdate.CompletionId);
        Assert.NotNull(streamingUpdate.Usage);
        Assert.Equal(20, streamingUpdate.Usage.OutputTokenCount);
        Assert.Equal(10, streamingUpdate.Usage.InputTokenCount);
        Assert.Equal(30, streamingUpdate.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithFunctionCallContent_CreatesUpdateWithToolCalls()
    {
        var functionCallContent = new FunctionCallContent("call-123", "GetWeather", new Dictionary<string, object?>
        {
            ["location"] = "Seattle",
            ["units"] = "celsius"
        });

        var responseUpdate = new ChatResponseUpdate(ChatRole.Assistant, [functionCallContent])
        {
            ResponseId = "response-123"
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Single(result);
        var streamingUpdate = result[0];

        Assert.Equal("response-123", streamingUpdate.CompletionId);
        Assert.Single(streamingUpdate.ToolCallUpdates);

        var toolCallUpdate = streamingUpdate.ToolCallUpdates[0];
        Assert.Equal(0, toolCallUpdate.Index);
        Assert.Equal("call-123", toolCallUpdate.ToolCallId);
        Assert.Equal(ChatToolCallKind.Function, toolCallUpdate.Kind);
        Assert.Equal("GetWeather", toolCallUpdate.FunctionName);

        var deserializedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            toolCallUpdate.FunctionArgumentsUpdate.ToMemory().Span);
        Assert.Equal("Seattle", deserializedArgs?["location"]?.ToString());
        Assert.Equal("celsius", deserializedArgs?["units"]?.ToString());
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithMultipleFunctionCalls_CreatesCorrectIndexes()
    {
        var functionCall1 = new FunctionCallContent("call-1", "Function1", new Dictionary<string, object?> { ["param1"] = "value1" });
        var functionCall2 = new FunctionCallContent("call-2", "Function2", new Dictionary<string, object?> { ["param2"] = "value2" });

        var responseUpdate = new ChatResponseUpdate(ChatRole.Assistant, [functionCall1, functionCall2])
        {
            ResponseId = "response-123"
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Single(result);
        var streamingUpdate = result[0];

        Assert.Equal(2, streamingUpdate.ToolCallUpdates.Count);

        Assert.Equal(0, streamingUpdate.ToolCallUpdates[0].Index);
        Assert.Equal("call-1", streamingUpdate.ToolCallUpdates[0].ToolCallId);
        Assert.Equal("Function1", streamingUpdate.ToolCallUpdates[0].FunctionName);

        Assert.Equal(1, streamingUpdate.ToolCallUpdates[1].Index);
        Assert.Equal("call-2", streamingUpdate.ToolCallUpdates[1].ToolCallId);
        Assert.Equal("Function2", streamingUpdate.ToolCallUpdates[1].FunctionName);
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithMixedContent_IncludesAllContent()
    {
        var responseUpdate = new ChatResponseUpdate(ChatRole.Assistant,
        [
            new TextContent("Processing your request..."),
            new FunctionCallContent("call-123", "GetWeather", new Dictionary<string, object?> { ["location"] = "Seattle" }),
            new UsageContent(new UsageDetails { TotalTokenCount = 50 })
        ])
        {
            ResponseId = "response-123",
            ModelId = "gpt-4"
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Single(result);
        var streamingUpdate = result[0];

        Assert.Equal("response-123", streamingUpdate.CompletionId);
        Assert.Equal("gpt-4", streamingUpdate.Model);

        // Should have text content
        Assert.Contains(streamingUpdate.ContentUpdate, c => c.Text == "Processing your request...");

        // Should have tool call
        Assert.Single(streamingUpdate.ToolCallUpdates);
        Assert.Equal("call-123", streamingUpdate.ToolCallUpdates[0].ToolCallId);

        // Should have usage
        Assert.NotNull(streamingUpdate.Usage);
        Assert.Equal(50, streamingUpdate.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithDifferentRoles_MapsCorrectly()
    {
        var testCases = new[]
        {
            (ChatRole.Assistant, ChatMessageRole.Assistant),
            (ChatRole.User, ChatMessageRole.User),
            (ChatRole.System, ChatMessageRole.System),
            (ChatRole.Tool, ChatMessageRole.Tool)
        };

        foreach (var (inputRole, expectedOpenAIRole) in testCases)
        {
            var responseUpdate = new ChatResponseUpdate(inputRole, "Test message");

            var result = new List<StreamingChatCompletionUpdate>();
            await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
            {
                result.Add(update);
            }

            Assert.Single(result);
            Assert.Equal(expectedOpenAIRole, result[0].Role);
        }
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithDifferentFinishReasons_MapsCorrectly()
    {
        var testCases = new[]
        {
            (ChatFinishReason.Stop, OpenAI.Chat.ChatFinishReason.Stop),
            (ChatFinishReason.Length, OpenAI.Chat.ChatFinishReason.Length),
            (ChatFinishReason.ContentFilter, OpenAI.Chat.ChatFinishReason.ContentFilter),
            (ChatFinishReason.ToolCalls, OpenAI.Chat.ChatFinishReason.ToolCalls)
        };

        foreach (var (inputFinishReason, expectedOpenAIFinishReason) in testCases)
        {
            var responseUpdate = new ChatResponseUpdate(ChatRole.Assistant, "Test")
            {
                FinishReason = inputFinishReason
            };

            var result = new List<StreamingChatCompletionUpdate>();
            await foreach (var update in CreateAsyncEnumerable(new[] { responseUpdate }).AsOpenAIStreamingChatCompletionUpdatesAsync())
            {
                result.Add(update);
            }

            Assert.Single(result);
            Assert.Equal(expectedOpenAIFinishReason, result[0].FinishReason);
        }
    }

    [Fact]
    public async Task AsOpenAIStreamingChatCompletionUpdatesAsync_WithMultipleUpdates_ProcessesAllCorrectly()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "Hello, ")
            {
                ResponseId = "response-123",
                MessageId = "message-1"

                // No FinishReason set - null
            },
            new ChatResponseUpdate(ChatRole.Assistant, "world!")
            {
                ResponseId = "response-123",
                MessageId = "message-1",
                FinishReason = ChatFinishReason.Stop
            }
        };

        var result = new List<StreamingChatCompletionUpdate>();
        await foreach (var update in CreateAsyncEnumerable(updates).AsOpenAIStreamingChatCompletionUpdatesAsync())
        {
            result.Add(update);
        }

        Assert.Equal(2, result.Count);

        Assert.Equal("response-123", result[0].CompletionId);
        Assert.Equal("Hello, ", Assert.Single(result[0].ContentUpdate).Text);

        // The ToChatFinishReason method defaults null to Stop
        Assert.Equal(OpenAI.Chat.ChatFinishReason.Stop, result[0].FinishReason);

        Assert.Equal("response-123", result[1].CompletionId);
        Assert.Equal("world!", Assert.Single(result[1].ContentUpdate).Text);
        Assert.Equal(OpenAI.Chat.ChatFinishReason.Stop, result[1].FinishReason);
    }

    [Fact]
    public void AsOpenAIResponse_WithNullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("response", () => ((ChatResponse)null!).AsOpenAIResponseResult());
    }

    [Fact]
    public void AsOpenAIResponse_WithRawRepresentation_ReturnsOriginal()
    {
        ResponseResult originalOpenAIResponse = new()
        {
            Id = "original-response-id",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Status = ResponseStatus.Completed,
            MaxOutputTokenCount = 100,
            ParallelToolCallsEnabled = false,
            Model = "gpt-4",
            Temperature = 0.7f,
            TopP = 0.9f,
            PreviousResponseId = "prev-id",
        };

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test"))
        {
            RawRepresentation = originalOpenAIResponse
        };

        var result = chatResponse.AsOpenAIResponseResult();

        Assert.Same(originalOpenAIResponse, result);
    }

    [Fact]
    public void AsOpenAIResponse_WithBasicChatResponse_CreatesValidOpenAIResponse()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello, world!"))
        {
            ResponseId = "test-response-id",
            ModelId = "gpt-4-turbo",
            CreatedAt = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero),
            FinishReason = ChatFinishReason.Stop
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult();

        Assert.NotNull(openAIResponse);
        Assert.Equal("test-response-id", openAIResponse.Id);
        Assert.Equal("gpt-4-turbo", openAIResponse.Model);
        Assert.Equal(new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero), openAIResponse.CreatedAt);
        Assert.Equal(ResponseStatus.Completed, openAIResponse.Status);
        Assert.NotNull(openAIResponse.OutputItems);
        Assert.Single(openAIResponse.OutputItems);

        var outputItem = Assert.IsAssignableFrom<MessageResponseItem>(openAIResponse.OutputItems.First());
        Assert.Equal("Hello, world!", Assert.Single(outputItem.Content).Text);
    }

    [Fact]
    public void AsOpenAIResponse_WithChatOptions_IncludesOptionsInResponse()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test message"))
        {
            ConversationId = "conv_123",
            ResponseId = "options-test",
            ModelId = "gpt-3.5-turbo"
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 500,
            AllowMultipleToolCalls = true,
            Instructions = "You are a helpful assistant.",
            Temperature = 0.8f,
            TopP = 0.95f,
            ModelId = "override-model"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult(options);

        Assert.Equal("options-test", openAIResponse.Id);
        Assert.Equal("gpt-3.5-turbo", openAIResponse.Model);
        Assert.Equal(500, openAIResponse.MaxOutputTokenCount);
        Assert.True(openAIResponse.ParallelToolCallsEnabled);
        Assert.Equal("conv_123", openAIResponse.ConversationOptions?.ConversationId);
        Assert.Equal("You are a helpful assistant.", Assert.IsAssignableFrom<MessageResponseItem>(openAIResponse.Instructions.Single()).Content.Single().Text);
        Assert.Equal(0.8f, openAIResponse.Temperature);
        Assert.Equal(0.95f, openAIResponse.TopP);
    }

    [Fact]
    public void AsOpenAIResponse_WithEmptyMessages_CreatesResponseWithEmptyOutputItems()
    {
        var chatResponse = new ChatResponse([])
        {
            ResponseId = "empty-response",
            ModelId = "gpt-4"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult();

        Assert.Equal("empty-response", openAIResponse.Id);
        Assert.Equal("gpt-4", openAIResponse.Model);
        Assert.Empty(openAIResponse.OutputItems);
    }

    [Fact]
    public void AsOpenAIResponse_WithMultipleMessages_ConvertsAllMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "First message"),
            new(ChatRole.Assistant, "Second message"),
            new(ChatRole.Assistant,
            [
                new TextContent("Third message with function call"),
                new FunctionCallContent("call-123", "TestFunction", new Dictionary<string, object?> { ["param"] = "value" })
            ])
        };

        var chatResponse = new ChatResponse(messages)
        {
            ResponseId = "multi-message-response"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult();

        Assert.Equal(4, openAIResponse.OutputItems.Count);

        var messageItems = openAIResponse.OutputItems.OfType<MessageResponseItem>().ToArray();
        var functionCallItems = openAIResponse.OutputItems.OfType<FunctionCallResponseItem>().ToArray();

        Assert.Equal(3, messageItems.Length);
        Assert.Single(functionCallItems);

        Assert.Equal("First message", Assert.Single(messageItems[0].Content).Text);
        Assert.Equal("Second message", Assert.Single(messageItems[1].Content).Text);
        Assert.Equal("Third message with function call", Assert.Single(messageItems[2].Content).Text);

        Assert.Equal("call-123", functionCallItems[0].CallId);
        Assert.Equal("TestFunction", functionCallItems[0].FunctionName);
    }

    [Fact]
    public void AsOpenAIResponse_WithToolMessages_ConvertsCorrectly()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant,
            [
                new TextContent("I'll call a function"),
                new FunctionCallContent("call-456", "GetWeather", new Dictionary<string, object?> { ["location"] = "Seattle" })
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-456", "The weather is sunny")]),
            new(ChatRole.Assistant, "The weather in Seattle is sunny!")
        };

        var chatResponse = new ChatResponse(messages)
        {
            ResponseId = "tool-message-test"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult();

        var outputItems = openAIResponse.OutputItems.ToArray();
        Assert.Equal(4, outputItems.Length);

        // Should have message, function call, function output, and final message
        Assert.IsType<MessageResponseItem>(outputItems[0], exactMatch: false);
        Assert.IsType<FunctionCallResponseItem>(outputItems[1], exactMatch: false);
        Assert.IsType<FunctionCallOutputResponseItem>(outputItems[2], exactMatch: false);
        Assert.IsType<MessageResponseItem>(outputItems[3], exactMatch: false);

        var functionCallOutput = (FunctionCallOutputResponseItem)outputItems[2];
        Assert.Equal("call-456", functionCallOutput.CallId);
        Assert.Equal("The weather is sunny", functionCallOutput.FunctionOutput);
    }

    [Fact]
    public void AsOpenAIResponse_WithSystemAndUserMessages_ConvertsCorrectly()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Hello, how are you?"),
            new(ChatRole.Assistant, "I'm doing well, thank you for asking!")
        };

        var chatResponse = new ChatResponse(messages)
        {
            ResponseId = "system-user-test"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult();

        var outputItems = openAIResponse.OutputItems.ToArray();
        Assert.Equal(3, outputItems.Length);

        var systemMessage = Assert.IsType<MessageResponseItem>(outputItems[0], exactMatch: false);
        var userMessage = Assert.IsType<MessageResponseItem>(outputItems[1], exactMatch: false);
        var assistantMessage = Assert.IsType<MessageResponseItem>(outputItems[2], exactMatch: false);

        Assert.Equal("You are a helpful assistant.", Assert.Single(systemMessage.Content).Text);
        Assert.Equal("Hello, how are you?", Assert.Single(userMessage.Content).Text);
        Assert.Equal("I'm doing well, thank you for asking!", Assert.Single(assistantMessage.Content).Text);
    }

    [Fact]
    public void AsOpenAIResponse_WithDefaultValues_UsesExpectedDefaults()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Default test"));

        var openAIResponse = chatResponse.AsOpenAIResponseResult();

        Assert.NotNull(openAIResponse);
        Assert.Equal(ResponseStatus.Completed, openAIResponse.Status);
        Assert.True(openAIResponse.ParallelToolCallsEnabled);
        Assert.Null(openAIResponse.MaxOutputTokenCount);
        Assert.Null(openAIResponse.Temperature);
        Assert.Null(openAIResponse.TopP);
        Assert.Null(openAIResponse.ConversationOptions);
        Assert.Empty(openAIResponse.Instructions);
        Assert.NotNull(openAIResponse.OutputItems);
    }

    [Fact]
    public void AsOpenAIResponse_WithOptionsButNoModelId_UsesOptionsModelId()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Model test"));

        var options = new ChatOptions
        {
            ModelId = "options-model-id"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult(options);

        Assert.Equal("options-model-id", openAIResponse.Model);
    }

    [Fact]
    public void AsOpenAIResponse_WithBothModelIds_PrefersChatResponseModelId()
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Model priority test"))
        {
            ModelId = "response-model-id"
        };

        var options = new ChatOptions
        {
            ModelId = "options-model-id"
        };

        var openAIResponse = chatResponse.AsOpenAIResponseResult(options);

        Assert.Equal("response-model-id", openAIResponse.Model);
    }

    [Fact]
    public void ListAddResponseTool_AddsToolCorrectly()
    {
        Assert.Throws<ArgumentNullException>("tools", () => ((IList<AITool>)null!).Add(ResponseTool.CreateWebSearchTool()));
        Assert.Throws<ArgumentNullException>("tool", () => new List<AITool>().Add((ResponseTool)null!));

        Assert.Throws<ArgumentNullException>("tool", () => ((ResponseTool)null!).AsAITool());

        ChatOptions options;

        options = new()
        {
            Tools = new List<AITool> { ResponseTool.CreateWebSearchTool() },
        };
        Assert.Single(options.Tools);
        Assert.NotNull(options.Tools[0]);

        var rawSearchTool = ResponseTool.CreateWebSearchTool();
        options = new()
        {
            Tools = [rawSearchTool.AsAITool()],
        };
        Assert.Single(options.Tools);
        Assert.NotNull(options.Tools[0]);

        Assert.Same(rawSearchTool, options.Tools[0].GetService<ResponseTool>());
        Assert.Same(rawSearchTool, options.Tools[0].GetService<WebSearchTool>());
        Assert.Null(options.Tools[0].GetService<ResponseTool>("key"));
    }

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static string RemoveWhitespace(string input) => Regex.Replace(input, @"\s+", "");

    /// <summary>Helper class for testing unknown tool types.</summary>
    private sealed class UnknownAITool : AITool
    {
        public override string Name => "unknown_tool";
    }

    /// <summary>Helper class for testing WebSearchTool with additional properties.</summary>
    [Fact]
    public void AsChatMessages_WebSearchCallResponseItem_QueriesArrayPreferredOverDeprecatedQuery()
    {
        // Both "queries" (array) and deprecated "query" (string) are present.
        // The array form should be preferred.
        WebSearchCallResponseItem wsItem = ModelReaderWriter.Read<WebSearchCallResponseItem>(BinaryData.FromString(
            """{"type":"web_search_call","id":"ws_1","status":"completed","action":{"type":"search","queries":[".NET 10 release","dotnet latest"],"query":".NET 10 release"}}"""))!;

        ChatMessage[] messages = new ResponseItem[] { wsItem }.AsChatMessages().ToArray();
        Assert.Single(messages);

        WebSearchToolCallContent wsCall = Assert.Single(messages[0].Contents.OfType<WebSearchToolCallContent>());
        Assert.Equal("ws_1", wsCall.CallId);
        Assert.NotNull(wsCall.Queries);
        Assert.Equal(new[] { ".NET 10 release", "dotnet latest" }, wsCall.Queries);
    }

    [Fact]
    public void AsChatMessages_WebSearchCallResponseItem_FallsBackToDeprecatedQueryWhenNoQueriesArray()
    {
        // Only the deprecated "query" field is present (no "queries" array).
        WebSearchCallResponseItem wsItem = ModelReaderWriter.Read<WebSearchCallResponseItem>(BinaryData.FromString(
            """{"type":"web_search_call","id":"ws_2","status":"completed","action":{"type":"search","query":"what is .NET"}}"""))!;

        ChatMessage[] messages = new ResponseItem[] { wsItem }.AsChatMessages().ToArray();
        Assert.Single(messages);

        WebSearchToolCallContent wsCall = Assert.Single(messages[0].Contents.OfType<WebSearchToolCallContent>());
        Assert.Equal("ws_2", wsCall.CallId);
        Assert.NotNull(wsCall.Queries);
        Assert.Equal(new[] { "what is .NET" }, wsCall.Queries);
    }

    [Fact]
    public void AsChatMessages_WebSearchCallResponseItem_NullQueriesWhenNeitherFieldPresent()
    {
        // Neither "queries" nor "query" is present in the action.
        WebSearchCallResponseItem wsItem = ModelReaderWriter.Read<WebSearchCallResponseItem>(BinaryData.FromString(
            """{"type":"web_search_call","id":"ws_3","status":"completed","action":{"type":"search"}}"""))!;

        ChatMessage[] messages = new ResponseItem[] { wsItem }.AsChatMessages().ToArray();
        Assert.Single(messages);

        WebSearchToolCallContent wsCall = Assert.Single(messages[0].Contents.OfType<WebSearchToolCallContent>());
        Assert.Equal("ws_3", wsCall.CallId);
        Assert.Null(wsCall.Queries);
    }

    private sealed class HostedWebSearchToolWithProperties : HostedWebSearchTool
    {
        private readonly Dictionary<string, object?> _additionalProperties;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties;

        public HostedWebSearchToolWithProperties(Dictionary<string, object?> additionalProperties)
        {
            _additionalProperties = additionalProperties;
        }
    }
}
