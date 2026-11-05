// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class FunctionInvokingChatClientApprovalsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllFunctionCallsReplacedWithApprovalsWhenAllRequireApprovalAsync(bool useAdditionalTools)
    {
        AITool[] tools =
        [
            new ApprovalRequiredAIFunction(
                AIFunctionFactory.Create(() => "Result 1", "Func1")),
            new ApprovalRequiredAIFunction(
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2")),
        ];

        var options = new ChatOptions
        {
            Tools = useAdditionalTools ? null : tools
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput, additionalTools: useAdditionalTools ? tools : null);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput, additionalTools: useAdditionalTools ? tools : null);
    }

    [Fact]
    public async Task AllFunctionCallsReplacedWithApprovalsWhenAnyRequireApprovalAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
                {
                    RequiresConfirmation = false,
                }
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllFunctionCallsReplacedWithApprovalsWhenAnyRequestOrAdditionalRequireApprovalAsync(bool additionalToolsRequireApproval)
    {
        AIFunction func1 = AIFunctionFactory.Create(() => "Result 1", "Func1");
        AIFunction func2 = AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2");
        AITool[] additionalTools =
        [
            additionalToolsRequireApproval ? new ApprovalRequiredAIFunction(func1) : func1,
        ];

        var options = new ChatOptions
        {
            Tools =
            [
                additionalToolsRequireApproval ? func2 : new ApprovalRequiredAIFunction(func2),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
        ];

        // When additionalToolsRequireApproval is true: Func1 (additional tools) requires approval and Func2 (options.Tools) does not.
        // When false: Func2 (options.Tools) requires approval and Func1 (additional tools) does not.
        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1"))
                {
                    RequiresConfirmation = additionalToolsRequireApproval,
                },
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
                {
                    RequiresConfirmation = !additionalToolsRequireApproval,
                }
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput, additionalTools: additionalTools);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput, additionalTools: additionalTools);
    }

    private sealed class PassThroughDelegatingAIFunction(AIFunction inner) : DelegatingAIFunction(inner);

    [Fact]
    public async Task RequiresConfirmation_IsTrueForApprovalRequiredFunctionNestedInDelegatingWrapperAsync()
    {
        // Wrap the ApprovalRequiredAIFunction in another DelegatingAIFunction (e.g. a telemetry decorator).
        // FICC must still classify the call as approval-required (RequiresConfirmation = true, the default)
        // by walking the delegation chain via GetService<ApprovalRequiredAIFunction>().
        AITool[] tools =
        [
            new PassThroughDelegatingAIFunction(
                new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(() => "Result 1", "Func1"))),
            AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
        ];

        var options = new ChatOptions { Tools = tools };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("callId1", "Func1"),
                new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })
            ]),
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
                {
                    RequiresConfirmation = false,
                }
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput);
    }

    [Fact]
    public async Task RequiresConfirmation_IsFalseForFunctionCallWithNoMatchingToolWhenPeerRequiresApprovalAsync()
    {
        // The downstream client emits an FCC referencing a tool name that is not in the tools list.
        // Because a peer call (Func1) requires approval, FICC still wraps the unknown call.
        // Since no matching tool is found (and therefore no ApprovalRequiredAIFunction is detected),
        // the resulting approval request must carry RequiresConfirmation = false.
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("callId1", "Func1"),
                new FunctionCallContent("callId2", "Unknown"),
            ]),
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Unknown"))
                {
                    RequiresConfirmation = false,
                }
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput);
    }

    [Fact]
    public async Task ApprovedApprovalResponsesAreExecutedAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedApprovalResponsesAreGroupedWhenMessageIdIsNullAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2")),
            ]
        };

        // Key difference from other tests: MessageId is NOT set on the assistant message
        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]), // Note: No MessageId set - this is the bug trigger
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        // Both FCCs should be in a SINGLE assistant message, not split across multiple messages
        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedApprovalResponsesFromSeparateFCCMessagesAreExecutedAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp2" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
            ]),
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]) { MessageId = "resp2" },
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]) { MessageId = "resp2" },
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task RejectedApprovalResponsesAreFailedAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", false, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected."),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected.")
            ]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected."),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected.")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task MixedApprovedAndRejectedApprovalResponsesAreExecutedAndFailedAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Tool call invocation rejected.")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> nonStreamingOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Tool call invocation rejected.")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> streamingOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected."),
                new FunctionResultContent("callId2", result: "Result 2: 42")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, nonStreamingOutput, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, streamingOutput, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task RejectedApprovalResponsesWithCustomReasonAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, new FunctionCallContent("callId1", "Func1"))
                {
                    Reason = "User denied permission for this operation"
                },
                new ToolApprovalResponseContent("ficc_callId2", false, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
                {
                    Reason = "Function Func2 is not allowed at this time"
                }
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected. User denied permission for this operation"),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected. Function Func2 is not allowed at this time")
            ]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected. User denied permission for this operation"),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected. Function Func2 is not allowed at this time")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task MixedApprovalResponsesWithCustomAndDefaultReasonsAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
                AIFunctionFactory.Create((string s) => $"Result 3: {s}", "Func3"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })),
                new ToolApprovalRequestContent("ficc_callId3", new FunctionCallContent("callId3", "Func3", arguments: new Dictionary<string, object?> { { "s", "test" } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, new FunctionCallContent("callId1", "Func1")) { Reason = "Custom rejection for Func1" },
                new ToolApprovalResponseContent("ficc_callId2", false, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })),
                new ToolApprovalResponseContent("ficc_callId3", true, new FunctionCallContent("callId3", "Func3", arguments: new Dictionary<string, object?> { { "s", "test" } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func1"),
                new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }),
                new FunctionCallContent("callId3", "Func3", arguments: new Dictionary<string, object?> { { "s", "test" } })
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected. Custom rejection for Func1"),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected.")
            ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Result 3: test")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> nonStreamingOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func1"),
                new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }),
                new FunctionCallContent("callId3", "Func3", arguments: new Dictionary<string, object?> { { "s", "test" } })
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected. Custom rejection for Func1"),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected.")
            ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Result 3: test")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> streamingOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func1"),
                new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }),
                new FunctionCallContent("callId3", "Func3", arguments: new Dictionary<string, object?> { { "s", "test" } })
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected. Custom rejection for Func1"),
                new FunctionResultContent("callId2", result: "Tool call invocation rejected."),
                new FunctionResultContent("callId3", result: "Result 3: test")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, nonStreamingOutput, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, streamingOutput, expectedDownstreamClientInput);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectedApprovalResponsesWithEmptyOrWhitespaceReasonUsesDefaultMessageAsync(string? reason)
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, new FunctionCallContent("callId1", "Func1"))
                {
                    Reason = reason
                },
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected.")
            ]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Tool call invocation rejected.")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedInputsAreExecutedAndFunctionResultsAreConvertedAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2")),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 3 } })]),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 3 } }))
            ]),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task AlreadyExecutedApprovalsAreIgnoredAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2")),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId3", new FunctionCallContent("callId3", "Func1")),
            ]) { MessageId = "resp2" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId3", true, new FunctionCallContent("callId3", "Func1")),
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Result 1")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "World"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "World"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    /// <summary>
    /// After serialization/deserialization, the TARC and TAResp may contain separate FCC object instances
    /// for the same call. When a rejection is processed, GenerateRejectedFunctionResults must set
    /// InformationalOnly=true on BOTH the TAResp's FCC and the TARC's FCC to ensure consistency
    /// across serialization boundaries. This test verifies that both FCC instances are correctly
    /// marked after rejection processing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectionSetsInformationalOnlyOnBothRequestAndResponseFccInstancesAsync(bool streaming)
    {
        // Create two separate FCC objects for the same call — simulating deserialization
        // where TARC and TAResp hold different FCC instances with the same CallId.
        var requestFcc = new FunctionCallContent("callId1", "Func1");
        var responseFcc = new FunctionCallContent("callId1", "Func1");

        Assert.False(requestFcc.InformationalOnly);
        Assert.False(responseFcc.InformationalOnly);

        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", requestFcc),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, responseFcc),
            ]),
        ];

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
                Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "world")])),
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
                YieldAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "world")]).ToChatResponseUpdates()),
        };

        IChatClient service = innerClient.AsBuilder()
            .Use(s => new FunctionInvokingChatClient(s))
            .Build();

        if (streaming)
        {
            await service.GetStreamingResponseAsync(input, options).ToChatResponseAsync();
        }
        else
        {
            await service.GetResponseAsync(input, options);
        }

        // The fix ensures both FCC instances are marked InformationalOnly=true,
        // even when they are separate objects (as happens after serialization).
        Assert.True(requestFcc.InformationalOnly);
        Assert.True(responseFcc.InformationalOnly);
    }

    /// <summary>
    /// After serialization/deserialization, the TARC and TAResp may contain separate FCC object instances
    /// for the same call. When a rejection is processed, GenerateRejectedFunctionResults must set
    /// InformationalOnly=true on BOTH the TAResp's FCC and the TARC's FCC to ensure consistency
    /// across serialization boundaries. Previously, this was not always happening, so adding
    /// a test to ensure that this case does not throw.
    /// See https://github.com/dotnet/extensions/pull/7468.
    /// Workaround: Use a middleware to normalize InformationalOnly flags on deserialized sessions.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedInformationalOnlyWorkaroundWithMiddlewareAsync(bool streaming)
    {
        // Create two separate FCC objects for the same call — simulating deserialization
        // where TARC and TAResp hold different FCC instances with the same CallId.
        var request1Fcc = new FunctionCallContent("callId1", "Func1");
        var response1Fcc = new FunctionCallContent("callId1", "Func1") { InformationalOnly = true };

        var request2Fcc = new FunctionCallContent("callId2", "Func1");

        Assert.False(request1Fcc.InformationalOnly);
        Assert.True(response1Fcc.InformationalOnly);

        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", request1Fcc),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, response1Fcc),
            ]),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId2", request2Fcc),
            ]) { MessageId = "resp2" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId2", false, request2Fcc),
            ]),
        ];

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
                Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "world")])),
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
                YieldAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "world")]).ToChatResponseUpdates()),
        };

        // Use a middleware to normalize InformationalOnly flags before FICC processes the messages.
        IChatClient service = innerClient.AsBuilder()
            .Use(s => new ApprovalHistoryNormalizingChatClient(s))
            .Use(s => new FunctionInvokingChatClient(s))
            .Build();

        if (streaming)
        {
            await service.GetStreamingResponseAsync(input, options).ToChatResponseAsync();
        }
        else
        {
            await service.GetResponseAsync(input, options);
        }
    }

    /// <summary>
    /// This verifies the following scenario:
    /// 1. We are streaming (also including non-streaming in the test for completeness).
    /// 2. There is one function that requires approval and one that does not.
    /// 3. We only get back FCC for the function that does not require approval.
    /// 4. This means that once we receive this FCC, we need to buffer all updates until the end, because we might receive more FCCs and some may require approval.
    /// 5. We then need to verify that we will still stream all updates once we reach the end, including the buffered FCC.
    /// </summary>
    [Fact]
    public async Task MixedApprovalRequiredToolsWithNonApprovalRequiringFunctionCallAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        Func<Queue<List<ChatMessage>>> expectedDownstreamClientInput = () => new Queue<List<ChatMessage>>(
        [
            new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "hello"),
            },
            new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "hello"),
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")])
            }
        ]);

        Func<Queue<List<ChatMessage>>> downstreamClientOutput = () => new Queue<List<ChatMessage>>(
        [
            new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            },
            new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, "World again"),
            }
        ]);

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "World again"),
        ];

        await InvokeAndAssertMultiRoundAsync(options, input, downstreamClientOutput(), output, expectedDownstreamClientInput());

        await InvokeAndAssertStreamingMultiRoundAsync(options, input, downstreamClientOutput(), output, expectedDownstreamClientInput());
    }

    [Fact]
    public async Task ApprovalRequestWithoutApprovalResponseThrowsAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
        ];

        var invokeException = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await InvokeAndAssertAsync(options, input, [], [], []));
        Assert.Equal("ToolApprovalRequestContent found with FunctionCall.CallId(s) 'callId1' that have no matching ToolApprovalResponseContent.", invokeException.Message);

        var invokeStreamingException = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await InvokeAndAssertStreamingAsync(options, input, [], [], []));
        Assert.Equal("ToolApprovalRequestContent found with FunctionCall.CallId(s) 'callId1' that have no matching ToolApprovalResponseContent.", invokeStreamingException.Message);
    }

    [Fact]
    public async Task ApprovedApprovalResponsesWithoutApprovalRequestAreExecutedAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task FunctionCallContentIsNotPassedToDownstreamServiceWithServiceThreadsAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ],
            ConversationId = "test-conversation",
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new ToolApprovalResponseContent("ficc_callId2", true, new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } }))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1"), new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedResponsesStayAdjacentToToolCallWhenTrailingMessagesPresentWithServiceThreadsAsync()
    {
        // Service-managed history: only the new messages are passed and a ConversationId is set. The service
        // holds the assistant tool-call, so the reconstructed tool result must be positioned ahead of any
        // trailing caller-supplied message, otherwise the effective ordering seen by the provider becomes
        // assistant(tool_calls) -> user(trailing) -> tool(result), which breaks tool_calls->tool adjacency.
        var options = new ChatOptions
        {
            Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"))],
            ConversationId = "test-conversation",
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
            ]),

            // A caller-supplied message after the approval response.
            new ChatMessage(ChatRole.User, "By the way, please keep the answer concise."),
        ];

        // The tool result must come before the trailing caller message, and the assistant tool-call is not
        // re-sent because the service already holds it.
        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.User, "By the way, please keep the answer concise."),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedResponseStaysAdjacentToToolCallWhenApprovalMessageHasOtherContentWithServiceThreadsAsync()
    {
        // Regression: the approval response shares a message with other caller content. Extraction removes the
        // approval response but leaves the other content ("please proceed") in place. In service-managed mode the
        // service holds the assistant tool-call, so the reconstructed tool result must still be placed ahead of that
        // residual content, otherwise the effective ordering becomes
        // assistant(tool_calls) -> user("please proceed") -> tool(result), which breaks tool_calls->tool adjacency.
        var options = new ChatOptions
        {
            Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"))],
            ConversationId = "test-conversation",
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new TextContent("please proceed"),
            ]),
        ];

        // The tool result is positioned at the front, ahead of the residual "please proceed" content that was left
        // behind in the approval message after the approval response was extracted.
        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.User, "please proceed"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedResponsesStayAdjacentToToolCallWhenTrailingMessagesPresentWithClientHistoryAsync()
    {
        // Client-managed history: the full history is passed and there is no ConversationId. The reconstructed
        // assistant tool-call and tool result are inserted at the approval anchor, so trailing caller-supplied
        // messages end up after the tool result while adjacency is preserved.
        var options = new ChatOptions
        {
            Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"))],
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
            ]),

            // A caller-supplied message after the approval response.
            new ChatMessage(ChatRole.User, "By the way, please keep the answer concise."),
        ];

        // The reconstructed assistant tool-call and tool result are inserted at the approval anchor, so they
        // remain adjacent and the trailing caller message follows the tool result.
        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.User, "By the way, please keep the answer concise."),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedResponseStaysAdjacentToToolCallWhenApprovalMessageHasOtherContentWithClientHistoryAsync()
    {
        // Client-managed history where the approval response shares a message with other caller content. The
        // reconstructed tool-call/result block is inserted just before that approval message, so the residual
        // content ("please proceed") that survives extraction ends up after the reconstructed assistant tool-call
        // and tool result rather than wedged before them.
        var options = new ChatOptions
        {
            Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"))],
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
                new TextContent("please proceed"),
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.User, "please proceed"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);

        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput);
    }

    [Fact]
    public async Task ApprovedResponseThatRequestsTerminationStopsBeforeCallingInnerClientAsync()
    {
        // An approved function requests termination of the processing loop. Approval responses are handled before
        // the main loop, so processing must stop right after the approval block - yielding the reconstructed
        // tool-call and tool result but never calling the inner client. This covers the streaming yield break and
        // non-streaming return that fire even though the approval block produced messages.
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() =>
                {
                    FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                    return "Result 1";
                }, "Func1")),
            ],
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
            ]),
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
        ];

        using (var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                throw new InvalidOperationException("The inner client must not be called after the approved function requests termination."),
        })
        {
            using var service = new FunctionInvokingChatClient(innerClient);
            var result = await service.GetResponseAsync(CloneInput(input), options);
            AssertExtensions.EqualMessageLists(expectedOutput, result.Messages.ToList());
        }

        using (var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) =>
                throw new InvalidOperationException("The inner client must not be called after the approved function requests termination."),
        })
        {
            using var service = new FunctionInvokingChatClient(innerClient);
            var result = await service.GetStreamingResponseAsync(CloneInput(input), options).ToChatResponseAsync();
            AssertExtensions.EqualMessageLists(expectedOutput, result.Messages.ToList());
        }
    }

    [Fact]
    public async Task ApprovedResponseThatRequestsTerminationAndThrowsStopsBeforeCallingInnerClientAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(string () =>
                {
                    FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                    throw new InvalidOperationException("Oh no!");
                }, "Func1")),
            ],
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1")),
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1")),
            ]),
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Error: Function failed.")]),
        ];

        using (var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                throw new InvalidOperationException("The inner client must not be called after the approved function requests termination."),
        })
        {
            using var service = new FunctionInvokingChatClient(innerClient);
            var result = await service.GetResponseAsync(CloneInput(input), options);
            AssertExtensions.EqualMessageLists(expectedOutput, result.Messages.ToList());
        }

        using (var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) =>
                throw new InvalidOperationException("The inner client must not be called after the approved function requests termination."),
        })
        {
            using var service = new FunctionInvokingChatClient(innerClient);
            var result = await service.GetStreamingResponseAsync(CloneInput(input), options).ToChatResponseAsync();
            AssertExtensions.EqualMessageLists(expectedOutput, result.Messages.ToList());
        }
    }

    /// for matching FunctionResultContent from server-handled function calls and mark those FCCs as
    /// InformationalOnly. This means FCCs are not yielded immediately, even when no approval is required.
    /// </summary>
    [Fact]
    public async Task FunctionCallContentIsBufferedUntilEndOfStreamWhenStreamingAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> input = [new ChatMessage(ChatRole.User, "hello")];

        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline = b => b.Use(s => new FunctionInvokingChatClient(s));
        using CancellationTokenSource cts = new();

        var updateYieldCount = 0;

        async IAsyncEnumerable<ChatResponseUpdate> YieldInnerClientUpdates(
            IEnumerable<ChatMessage> contents, ChatOptions? actualOptions, [EnumeratorCancellation] CancellationToken actualCancellationToken)
        {
            Assert.Equal(cts.Token, actualCancellationToken);
            await Task.Yield();
            var messageId = Guid.NewGuid().ToString("N");

            updateYieldCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]) { MessageId = messageId };
            updateYieldCount++;
            yield return
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })
                    ])
                { MessageId = messageId };
        }

        using var innerClient = new TestChatClient { GetStreamingResponseAsyncCallback = YieldInnerClientUpdates };
        IChatClient service = configurePipeline(innerClient.AsBuilder()).Build();

        var updates = service.GetStreamingResponseAsync(new EnumeratedOnceEnumerable<ChatMessage>(input), options, cts.Token);

        var updateCount = 0;
        await foreach (var update in updates)
        {
            if (updateCount < 2)
            {
                var functionCall = update.Contents.OfType<FunctionCallContent>().First();
                if (functionCall.CallId == "callId1")
                {
                    Assert.Equal("Func1", functionCall.Name);

                    // FCCs are now buffered until the end of the stream to check for
                    // matching FunctionResultContent from server-handled function calls.
                    Assert.Equal(2, updateYieldCount);
                }
                else if (functionCall.CallId == "callId2")
                {
                    Assert.Equal("Func2", functionCall.Name);
                    Assert.Equal(2, updateYieldCount);
                }
            }

            updateCount++;
        }
    }

    /// <summary>
    /// Since we do not have a way of supporting both functions that require approval and those that do not
    /// in one invocation, we always require all function calls to be approved if any require approval.
    /// If we are therefore unsure as to whether we will encounter a function call that requires approval,
    /// we have to wait until we find one before yielding any function call content.
    /// We can however, yield any other content until we encounter the first function call.
    /// </summary>
    [Fact]
    public async Task FunctionCalsAreBufferedUntilApprovalRequirementEncounteredWhenStreamingAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2")),
                AIFunctionFactory.Create(() => "Result 3", "Func3"),
            ]
        };

        List<ChatMessage> input = [new ChatMessage(ChatRole.User, "hello")];

        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline = b => b.Use(s => new FunctionInvokingChatClient(s));
        using CancellationTokenSource cts = new();

        var updateYieldCount = 0;

        async IAsyncEnumerable<ChatResponseUpdate> YieldInnerClientUpdates(
            IEnumerable<ChatMessage> contents, ChatOptions? actualOptions, [EnumeratorCancellation] CancellationToken actualCancellationToken)
        {
            Assert.Equal(cts.Token, actualCancellationToken);
            await Task.Yield();
            var messageId = Guid.NewGuid().ToString("N");

            updateYieldCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Text 1")]) { MessageId = messageId };
            updateYieldCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Text 2")]) { MessageId = messageId };
            updateYieldCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]) { MessageId = messageId };
            updateYieldCount++;
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })
                ])
            { MessageId = messageId };
            updateYieldCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func3")]) { MessageId = messageId };
        }

        using var innerClient = new TestChatClient { GetStreamingResponseAsyncCallback = YieldInnerClientUpdates };
        IChatClient service = configurePipeline(innerClient.AsBuilder()).Build();

        var updates = service.GetStreamingResponseAsync(new EnumeratedOnceEnumerable<ChatMessage>(input), options, cts.Token);

        var updateCount = 0;
        await foreach (var update in updates)
        {
            switch (updateCount)
            {
                case 0:
                    Assert.Equal("Text 1", update.Contents.OfType<TextContent>().First().Text);

                    // First content should be yielded immedately, since we don't have any function calls yet.
                    Assert.Equal(1, updateYieldCount);
                    break;
                case 1:
                    Assert.Equal("Text 2", update.Contents.OfType<TextContent>().First().Text);

                    // Second content should be yielded immedately, since we don't have any function calls yet.
                    Assert.Equal(2, updateYieldCount);
                    break;
                case 2:
                    var approvalRequest1 = update.Contents.OfType<ToolApprovalRequestContent>().First();
                    Assert.Equal("callId1", approvalRequest1.ToolCall.CallId);
                    Assert.Equal("Func1", ((FunctionCallContent)approvalRequest1.ToolCall).Name);

                    // Third content should have been buffered, since we have not yet encountered a function call that requires approval.
                    Assert.Equal(4, updateYieldCount);
                    break;
                case 3:
                    var approvalRequest2 = update.Contents.OfType<ToolApprovalRequestContent>().First();
                    Assert.Equal("callId2", approvalRequest2.ToolCall.CallId);
                    Assert.Equal("Func2", ((FunctionCallContent)approvalRequest2.ToolCall).Name);

                    // Fourth content can be yielded immediately, since it is the first function call that requires approval.
                    Assert.Equal(4, updateYieldCount);
                    break;
                case 4:
                    var approvalRequest3 = update.Contents.OfType<ToolApprovalRequestContent>().First();
                    Assert.Equal("callId1", approvalRequest3.ToolCall.CallId);
                    Assert.Equal("Func3", ((FunctionCallContent)approvalRequest3.ToolCall).Name);

                    // Fifth content can be yielded immediately, since we previously encountered a function call that requires approval.
                    Assert.Equal(5, updateYieldCount);
                    break;
            }

            updateCount++;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionCallsWithInformationalOnlyTrueAreNotReplacedWithApprovalsAsync(bool streaming)
    {
        var functionInvokedCount = 0;
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(() => { functionInvokedCount++; return "Result 1"; }, "Func1")),
            ]
        };

        List<ChatMessage> input = [new ChatMessage(ChatRole.User, "hello")];

        // FunctionCallContent with InformationalOnly = true should pass through unchanged
        var alreadyProcessedFunctionCall = new FunctionCallContent("callId1", "Func1") { InformationalOnly = true };
        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [alreadyProcessedFunctionCall]),
        ];

        // Expected output should contain the same FunctionCallContent, not a ToolApprovalRequestContent
        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant, [alreadyProcessedFunctionCall]),
        ];

        if (streaming)
        {
            await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput);
        }
        else
        {
            await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput);
        }

        // The function should NOT have been invoked since InformationalOnly was true
        Assert.Equal(0, functionInvokedCount);
    }

    [Fact]
    public async Task ApprovalResponsePreservesOriginalRequestMessageMetadata()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1")),
            ]
        };

        const string OriginalMessageId = "original-message-id";

        // Create input with approval request containing a known MessageId on the containing message
        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("approval-request-id", new FunctionCallContent("function-call-id", "Func1"))
            ]) { MessageId = OriginalMessageId }, // This MessageId should be preserved
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("approval-request-id", true, new FunctionCallContent("function-call-id", "Func1"))
            ]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        // The reconstructed function call message should preserve the original MessageId
        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("function-call-id", "Func1")]) { MessageId = OriginalMessageId },
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("function-call-id", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        var actualOutput = await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput);

        // Verify that the reconstructed function call message has the original MessageId, not a synthetic one
        Assert.Equal(OriginalMessageId, actualOutput[0].MessageId);

        actualOutput = await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput);
        Assert.Equal(OriginalMessageId, actualOutput[0].MessageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionCallReplacedWithApproval_MixedWithMcpApprovalAsync(bool useAdditionalTools)
    {
        AITool[] tools =
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func")),
            new HostedMcpServerTool("myServer", "https://localhost/mcp")
        ];

        var options = new ChatOptions
        {
            Tools = useAdditionalTools ? null : tools
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func"),
                new ToolApprovalRequestContent("callId2", new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ])
        ];

        List<ChatMessage> expectedOutput =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func")),
                new ToolApprovalRequestContent("callId2", new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, expectedOutput, additionalTools: useAdditionalTools ? tools : null);
        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, expectedOutput, additionalTools: useAdditionalTools ? tools : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedApprovalResponseIsExecuted_MixedWithMcpApprovalAsync(bool useAdditionalTools)
    {
        AITool[] tools =
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func")),
            new HostedMcpServerTool("myServer", "https://localhost/mcp")
        ];

        var options = new ChatOptions
        {
            Tools = useAdditionalTools ? null : tools
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func")),
                new ToolApprovalRequestContent("callId2", new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func")),
                new ToolApprovalResponseContent("callId2", true, new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("callId2", new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ]),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func")
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Result 1")
            ]),
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("callId2", true, new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ]),
        ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [
                new McpServerToolResultContent("callId2") { Outputs = [new TextContent("Result 2")] },
                new TextContent("world")
            ])
        ];

        List<ChatMessage> output =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func")
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Result 1")
            ]),
            new ChatMessage(ChatRole.Assistant, [
                new McpServerToolResultContent("callId2") { Outputs = [new TextContent("Result 2")] },
                new TextContent("world")
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput, additionalTools: useAdditionalTools ? tools : null);
        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput, additionalTools: useAdditionalTools ? tools : null);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task RejectedApprovalResponses_MixedWithMcpApprovalAsync(bool useAdditionalTools, bool approveFuncCall, bool approveMcpCall)
    {
        Assert.NotEqual(approveFuncCall, approveMcpCall);

        AITool[] tools =
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func")),
            new HostedMcpServerTool("myServer", "https://localhost/mcp")
        ];

        var options = new ChatOptions
        {
            Tools = useAdditionalTools ? null : tools
        };

        List<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func")),
                new ToolApprovalRequestContent("callId2", new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ]) { MessageId = "resp1" },
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", approveFuncCall, new FunctionCallContent("callId1", "Func")),
                new ToolApprovalResponseContent("callId2", approveMcpCall, new McpServerToolCallContent("callId2", "McpCall", "myServer"))
            ]),
        ];

        List<ChatMessage> expectedDownstreamClientInput = [
                new ChatMessage(ChatRole.User, "hello"),
                new ChatMessage(ChatRole.Assistant,
                [
                    new ToolApprovalRequestContent("callId2", new McpServerToolCallContent("callId2", "McpCall", "myServer"))
                ]),
                new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("callId1", "Func")
                ]),
                new ChatMessage(ChatRole.Tool,
                [
                    approveFuncCall ?
                        new FunctionResultContent("callId1", result: "Result 1") :
                        new FunctionResultContent("callId1", result: "Tool call invocation rejected.")
                ]),
                new ChatMessage(ChatRole.User,
                [
                    new ToolApprovalResponseContent("callId2", approveMcpCall, new McpServerToolCallContent("callId2", "McpCall", "myServer"))
                ]),
            ];

        List<ChatMessage> downstreamClientOutput =
        [
            new ChatMessage(ChatRole.Assistant, [
                new TextContent("world"),
                .. approveMcpCall ?
                    [new McpServerToolResultContent("callId2") { Outputs = [new TextContent("Result 2")] }] :
                    Array.Empty<AIContent>()
            ])
        ];

        List<ChatMessage> output = [
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func"),
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                approveFuncCall ?
                    new FunctionResultContent("callId1", result: "Result 1") :
                    new FunctionResultContent("callId1", result: "Tool call invocation rejected.")
            ]),
            new ChatMessage(ChatRole.Assistant, [
                new TextContent("world"),
                .. approveMcpCall ?
                    [new McpServerToolResultContent("callId2") { Outputs = [new TextContent("Result 2")] }] :
                    Array.Empty<AIContent>()
            ])
        ];

        await InvokeAndAssertAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput, additionalTools: useAdditionalTools ? tools : null);
        await InvokeAndAssertStreamingAsync(options, input, downstreamClientOutput, output, expectedDownstreamClientInput, additionalTools: useAdditionalTools ? tools : null);
    }

    private static Task<List<ChatMessage>> InvokeAndAssertAsync(
        ChatOptions? options,
        List<ChatMessage> input,
        List<ChatMessage> downstreamClientOutput,
        List<ChatMessage> expectedOutput,
        List<ChatMessage>? expectedDownstreamClientInput = null,
        Func<ChatClientBuilder, ChatClientBuilder>? configurePipeline = null,
        AITool[]? additionalTools = null)
        => InvokeAndAssertMultiRoundAsync(
            options,
            input,
            new Queue<List<ChatMessage>>(new[] { downstreamClientOutput }),
            expectedOutput,
            expectedDownstreamClientInput is null ? null : new Queue<List<ChatMessage>>(new[] { expectedDownstreamClientInput }),
            configurePipeline,
            additionalTools);

    private static async Task<List<ChatMessage>> InvokeAndAssertMultiRoundAsync(
        ChatOptions? options,
        List<ChatMessage> input,
        Queue<List<ChatMessage>> downstreamClientOutput,
        List<ChatMessage> expectedOutput,
        Queue<List<ChatMessage>>? expectedDownstreamClientInput = null,
        Func<ChatClientBuilder, ChatClientBuilder>? configurePipeline = null,
        AITool[]? additionalTools = null)
    {
        Assert.NotEmpty(input);

        configurePipeline ??= b => b.Use(s => new FunctionInvokingChatClient(s) { AdditionalTools = additionalTools });

        using CancellationTokenSource cts = new();
        long expectedTotalTokenCounts = 0;

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                Assert.Equal(cts.Token, actualCancellationToken);
                if (expectedDownstreamClientInput is not null)
                {
                    AssertExtensions.EqualMessageLists(expectedDownstreamClientInput.Dequeue(), contents.ToList());
                }

                await Task.Yield();

                var usage = CreateRandomUsage();
                expectedTotalTokenCounts += usage.InputTokenCount!.Value;

                var output = downstreamClientOutput.Dequeue();
                output.ForEach(m => m.MessageId = Guid.NewGuid().ToString("N"));
                return new ChatResponse(output) { Usage = usage };
            }
        };

        IChatClient service = configurePipeline(innerClient.AsBuilder()).Build();

        var result = await service.GetResponseAsync(new EnumeratedOnceEnumerable<ChatMessage>(CloneInput(input)), options, cts.Token);
        Assert.NotNull(result);

        var actualOutput = result.Messages as List<ChatMessage> ?? result.Messages.ToList();
        AssertExtensions.EqualMessageLists(expectedOutput, actualOutput);

        // Usage should be aggregated over all responses, including AdditionalUsage
        var actualUsage = result.Usage!;
        Assert.Equal(expectedTotalTokenCounts, actualUsage.InputTokenCount);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.OutputTokenCount);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.TotalTokenCount);
        Assert.Equal(2, actualUsage.AdditionalCounts!.Count);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.AdditionalCounts["firstValue"]);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.AdditionalCounts["secondValue"]);

        return actualOutput;
    }

    private static UsageDetails CreateRandomUsage()
    {
        // We'll set the same random number on all the properties so that, when determining the
        // correct sum in tests, we only have to total the values once
        var value = new Random().Next(100);
        return new UsageDetails
        {
            InputTokenCount = value,
            OutputTokenCount = value,
            TotalTokenCount = value,
            AdditionalCounts = new() { ["firstValue"] = value, ["secondValue"] = value },
        };
    }

    private static Task<List<ChatMessage>> InvokeAndAssertStreamingAsync(
        ChatOptions? options,
        List<ChatMessage> input,
        List<ChatMessage> downstreamClientOutput,
        List<ChatMessage> expectedOutput,
        List<ChatMessage>? expectedDownstreamClientInput = null,
        Func<ChatClientBuilder, ChatClientBuilder>? configurePipeline = null,
        AITool[]? additionalTools = null)
        => InvokeAndAssertStreamingMultiRoundAsync(
            options,
            input,
            new Queue<List<ChatMessage>>(new[] { downstreamClientOutput }),
            expectedOutput,
            expectedDownstreamClientInput is null ? null : new Queue<List<ChatMessage>>(new[] { expectedDownstreamClientInput }),
            configurePipeline,
            additionalTools);

    private static async Task<List<ChatMessage>> InvokeAndAssertStreamingMultiRoundAsync(
        ChatOptions? options,
        List<ChatMessage> input,
        Queue<List<ChatMessage>> downstreamClientOutput,
        List<ChatMessage> expectedOutput,
        Queue<List<ChatMessage>>? expectedDownstreamClientInput = null,
        Func<ChatClientBuilder, ChatClientBuilder>? configurePipeline = null,
        AITool[]? additionalTools = null)
    {
        Assert.NotEmpty(input);

        configurePipeline ??= b => b.Use(s => new FunctionInvokingChatClient(s) { AdditionalTools = additionalTools });

        using CancellationTokenSource cts = new();

        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                Assert.Equal(cts.Token, actualCancellationToken);
                if (expectedDownstreamClientInput is not null)
                {
                    AssertExtensions.EqualMessageLists(expectedDownstreamClientInput.Dequeue(), contents.ToList());
                }

                var output = downstreamClientOutput.Dequeue();
                output.ForEach(m => m.MessageId = Guid.NewGuid().ToString("N"));
                return YieldAsync(new ChatResponse(output).ToChatResponseUpdates());
            }
        };

        IChatClient service = configurePipeline(innerClient.AsBuilder()).Build();

        var result = await service.GetStreamingResponseAsync(new EnumeratedOnceEnumerable<ChatMessage>(CloneInput(input)), options, cts.Token).ToChatResponseAsync();
        Assert.NotNull(result);

        var actualOutput = result.Messages as List<ChatMessage> ?? result.Messages.ToList();

        expectedOutput ??= input;
        AssertExtensions.EqualMessageLists(expectedOutput, actualOutput);

        return actualOutput;
    }

    private static async IAsyncEnumerable<T> YieldAsync<T>(params T[] items)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private static List<ChatMessage> CloneInput(List<ChatMessage> input) =>
        input.Select(m => new ChatMessage(m.Role, m.Contents.Select(CloneFcc).ToList()) { MessageId = m.MessageId }).ToList();

    private static AIContent CloneFcc(AIContent c) => c switch
    {
        McpServerToolCallContent mstcc => new McpServerToolCallContent(mstcc.CallId, mstcc.Name, mstcc.ServerName)
        {
            Arguments = mstcc.Arguments,
        },
        FunctionCallContent fcc => new FunctionCallContent(fcc.CallId, fcc.Name, fcc.Arguments)
        {
            InformationalOnly = fcc.InformationalOnly
        },
        ToolApprovalRequestContent tarc =>
            new ToolApprovalRequestContent(tarc.RequestId, (ToolCallContent)CloneFcc(tarc.ToolCall))
            {
                RequiresConfirmation = tarc.RequiresConfirmation,
            },
        ToolApprovalResponseContent tarc =>
            new ToolApprovalResponseContent(tarc.RequestId, tarc.Approved, (ToolCallContent)CloneFcc(tarc.ToolCall))
            {
                Reason = tarc.Reason
            },
        _ => c
    };

    /// <summary>
    /// Workaround middleware for sessions serialized before
    /// https://github.com/dotnet/extensions/pull/7468.
    /// Normalizes InformationalOnly flags so TARC/TAResp pairs stay consistent.
    /// </summary>
#pragma warning disable SA1402 // File may only contain a single type
    private sealed class ApprovalHistoryNormalizingChatClient(IChatClient inner) : DelegatingChatClient(inner)
#pragma warning restore SA1402
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            NormalizeApprovalFlags(messages);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            NormalizeApprovalFlags(messages);
            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        private static void NormalizeApprovalFlags(IEnumerable<ChatMessage> messages)
        {
            var allContents = messages.SelectMany(m => m.Contents);

            var processedCallIds = new HashSet<string>(
                allContents
                    .OfType<ToolApprovalResponseContent>()
                    .Where(t => t.ToolCall is FunctionCallContent { InformationalOnly: true })
                    .Select(t => t.ToolCall.CallId));

            if (processedCallIds.Count == 0)
            {
                return;
            }

            foreach (var fcc in allContents
                .OfType<ToolApprovalRequestContent>()
                .Select(t => t.ToolCall)
                .OfType<FunctionCallContent>()
                .Where(fcc => !fcc.InformationalOnly && processedCallIds.Contains(fcc.CallId)))
            {
                fcc.InformationalOnly = true;
            }
        }
    }
}

