// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET8_0_OR_GREATER

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Testing;

namespace Microsoft.AspNetCore.Diagnostics.Logging.Test.Controllers;

[Route("[controller]")]
public class AttributeRoutingController : Controller
{
    [HttpGet("")]
    [HttpGet("all")]
    public IActionResult GetWithoutParams() => Ok();

    [HttpGet("get-1/{param:int:min(1)}")]
    public IActionResult GetWithConstraint([PrivateData] string param) => Ok(param);

    [HttpGet("get-2/{param?}")]
    public IActionResult GetWithNullableConstraint([PrivateData] int? param) => Ok(param);

    [HttpGet("get-3/{param=all}")]
    public IActionResult GetWithDefaultValue([NoDataClassification] string param) => Ok(param);

    [HttpGet("get-4/{param=all}")]
    public IActionResult GetWithUnclassifiedParam(string param) => Ok(param);

    [HttpGet("get-5/{id}{{x}}{param}")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "For testing")]
    public IActionResult GetWithEscapedBraces([NoDataClassification] string id, [PrivateData] string param) => Ok(param);

    [HttpGet("get-6/{{c}}/{param}/{id}")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "For testing")]
    public IActionResult GetWithEscapedBracesSegment([PrivateData] string param, [NoDataClassification] string id) => Ok(param);
}
#endif
