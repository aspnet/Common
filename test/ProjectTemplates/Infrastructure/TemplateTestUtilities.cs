// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.Shared.ProjectTemplates.Tests;

public static class TemplateTestUtilities
{
    /// <summary>Create a sanitized and standardized project name from the supplied args</summary>
    /// <remarks>
    /// Framework name is shortened from "net11.0" to "net11", e.g.
    /// Boolean options explicitly set to "true" use the option name followed by '_T'.
    /// Boolean options explicitly set to "false" use the option name followed by '_F'.
    /// Options with names and values use only the option value.
    /// Options without values use the option name.
    /// Non-word characters are removed.
    /// Empty values are removed.
    ///
    /// Name parts are abbreviated to avoid path length limits
    /// - AzureOpenAI -> aoai
    /// - GitHubModels -> gh
    /// - Ollama -> o
    /// - OpenAI -> oai
    /// - AzureAISearch -> aais
    /// - Qdrant -> q
    /// - Local -> l
    /// - Aspire -> A
    /// - ManagedIdentity -> ID
    /// - aot -> AOT
    /// - SelfContained -> SC
    /// </remarks>
    public static string GetProjectNameForArgs(string[] args, string? prefix = null)
    {
        IEnumerable<string> nameParts = args
            .Select(arg => Regex.Replace(arg, @"-(?:f|-[Ff]ramework)=(net[0-9]+)\.0", "$1"))
            .Select(arg => Regex.Replace(arg, "--(.*?)=true", "$1_T"))
            .Select(arg => Regex.Replace(arg, "--(.*?)=false", "$1_F"))
            .Select(arg => Regex.Replace(arg, "--(.*?)=(.*)", "$2"))
            .Select(arg => Regex.Replace(arg, "--(.*)", "$1"))
            .Select(arg => Regex.Replace(arg, @"\W", ""))
            .Select(arg => arg
                .Replace("azureopenai", "aoai")
                .Replace("githubmodels", "gh")
                .Replace("ollama", "o")
                .Replace("openai", "oai")
                .Replace("azureaisearch", "aais")
                .Replace("qdrant", "q")
                .Replace("local", "l")
                .Replace("aspire", "A")
                .Replace("managedidentity", "ID")
                .Replace("aot", "AOT")
                .Replace("selfcontained", "SC"))
            .Where(arg => !string.IsNullOrEmpty(arg));

        return (nameParts.Any(), prefix is not null) switch
        {
            (false, false) => "_defaults",
            (false, true) => $"{prefix}_defaults",
            (true, false) => string.Join('_', nameParts),
            _ => string.Join('_', nameParts.Prepend(prefix)),
        };
    }

    /// <summary>Gets all combinations of options provided for a project template.</summary>
    public static IEnumerable<string[]> GetPossibleOptions(ReadOnlyMemory<(string name, string[] values)> options)
    {
        if (options.Length == 0)
        {
            yield return [];
            yield break;
        }

        var firstOption = options.Span[0];

        foreach (var value in firstOption.values)
        {
            foreach (var otherOptions in GetPossibleOptions(options[1..]))
            {
                yield return [$"{firstOption.name}={value}", .. otherOptions];
            }
        }
    }

    /// <summary>Checks for a specific option/value pair</summary>
    public static bool HasOption(string[] args, string option, string value) =>
        args.Contains($"{option}={value}");

    /// <summary>Checks for a boolean option to be specified</summary>
    public static bool HasOption(string[] args, string option) =>
        args.Contains(option) || args.Contains($"{option}=true");
}
