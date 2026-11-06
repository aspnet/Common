// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

namespace Microsoft.Extensions.AI;

/// <summary>Applies <see cref="ToolAdditionContent"/> and <see cref="ToolRemovalContent"/> in a chat history to the tools sent with a request.</summary>
internal static class ToolChanges
{
    /// <summary>
    /// For a provider that accepts tool changes at their position in the conversation: removes from <see cref="ChatOptions.Tools"/>
    /// every tool whose first change in <paramref name="messages"/> is a <see cref="ToolAdditionContent"/>, since the history
    /// introduces those tools where they were added.
    /// </summary>
    /// <remarks>
    /// <paramref name="messages"/> is replaced with a list when it has to be scanned, so that the caller can enumerate it again.
    /// </remarks>
    /// <returns><paramref name="options"/> itself when no tool needs removing; otherwise a clone with the remaining tools.</returns>
    public static ChatOptions? WithoutIntroducedTools(ref IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
        {
            return options;
        }

        messages = AsList(messages);

        HashSet<string>? seen = null;
        HashSet<string>? introduced = null;
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                switch (content)
                {
                    case ToolAdditionContent addition:
                        if ((seen ??= new(StringComparer.Ordinal)).Add(addition.Tool.Name))
                        {
                            _ = (introduced ??= new(StringComparer.Ordinal)).Add(addition.Tool.Name);
                        }

                        break;

                    case ToolRemovalContent removal:
                        _ = (seen ??= new(StringComparer.Ordinal)).Add(removal.ToolName);
                        break;
                }
            }
        }

        if (introduced is null || !tools.Any(t => introduced.Contains(t.Name)))
        {
            return options;
        }

        ChatOptions clone = options.Clone();
        clone.Tools = [.. tools.Where(t => !introduced.Contains(t.Name))];
        return clone;
    }

    /// <summary>
    /// For a provider with no form for tool changes at a position in the conversation: applies the changes in
    /// <paramref name="messages"/> to <see cref="ChatOptions.Tools"/> and removes them from the history.
    /// </summary>
    /// <remarks>
    /// A tool whose last change is a removal is left out of the tools. A tool whose last change is an addition is kept, or added
    /// from its declaration when <see cref="ChatOptions.Tools"/> doesn't have a tool of that name. A message that held only tool
    /// changes is left out of the history. <paramref name="messages"/> is replaced with a list, so that the caller can enumerate
    /// it again.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ChatOptions.ToolMode"/> requires a function whose last change in <paramref name="messages"/> is a removal.
    /// </exception>
    public static void ApplyToTools(ref IEnumerable<ChatMessage> messages, ref ChatOptions? options)
    {
        messages = AsList(messages);

        Dictionary<string, AIFunctionDeclaration?>? finalState = null;
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                switch (content)
                {
                    case ToolAdditionContent addition:
                        (finalState ??= new(StringComparer.Ordinal))[addition.Tool.Name] = addition.Tool;
                        break;

                    case ToolRemovalContent removal:
                        (finalState ??= new(StringComparer.Ordinal))[removal.ToolName] = null;
                        break;
                }
            }
        }

        if (finalState is null)
        {
            return;
        }

        if (options?.ToolMode is RequiredChatToolMode { RequiredFunctionName: { } requiredName } &&
            finalState.TryGetValue(requiredName, out AIFunctionDeclaration? requiredState) && requiredState is null)
        {
            throw new InvalidOperationException(
                $"The tool mode requires the function '{requiredName}', but a {nameof(ToolRemovalContent)} in the history removes it.");
        }

        List<AITool> tools = [];
        HashSet<string> present = new(StringComparer.Ordinal);
        foreach (AITool tool in options?.Tools ?? [])
        {
            if (!finalState.TryGetValue(tool.Name, out AIFunctionDeclaration? state) || state is not null)
            {
                tools.Add(tool);
                _ = present.Add(tool.Name);
            }
        }

        foreach (KeyValuePair<string, AIFunctionDeclaration?> entry in finalState)
        {
            if (entry.Value is { } declaration && present.Add(entry.Key))
            {
                tools.Add(declaration);
            }
        }

        options = options?.Clone() ?? new();
        options.Tools = tools;

        List<ChatMessage> stripped = [];
        foreach (ChatMessage message in messages)
        {
            if (!message.Contents.Any(static c => c is ToolAdditionContent or ToolRemovalContent))
            {
                stripped.Add(message);
                continue;
            }

            List<AIContent> remaining = [.. message.Contents.Where(static c => c is not (ToolAdditionContent or ToolRemovalContent))];
            if (remaining.Count > 0)
            {
                ChatMessage clone = message.Clone();
                clone.Contents = remaining;
                stripped.Add(clone);
            }
        }

        messages = stripped;
    }

    /// <summary>Gets <paramref name="messages"/> as a list, so that a sequence that can only be enumerated once is enumerated once.</summary>
    private static IList<ChatMessage> AsList(IEnumerable<ChatMessage> messages) =>
        messages as IList<ChatMessage> ?? [.. messages];
}
