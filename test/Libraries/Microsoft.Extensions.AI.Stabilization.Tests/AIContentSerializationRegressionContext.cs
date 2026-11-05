// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

// Regression guard for https://github.com/dotnet/extensions/issues/7658.
//
// A consumer that source-generates a JsonSerializerContext containing List<AIContent> must not be
// forced to suppress MEAI001. This project treats MEAI001 as an error and applies no experimental
// suppression (see the csproj), so if any [Experimental] member - such as
// ToolApprovalRequestContent.RequiresConfirmation - leaks into the source generator's metadata for
// AIContent's polymorphic graph, this file fails to compile and the build breaks.
[JsonSerializable(typeof(List<AIContent>))]
internal sealed partial class AIContentSerializationRegressionContext : JsonSerializerContext;
