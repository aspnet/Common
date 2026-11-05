# Enriched Capabilities

This repository contains a suite of libraries that provide facilities commonly needed when creating production-ready applications. Initially developed to support high-scale and high-availability services within Microsoft, such as Microsoft Teams, these libraries deliver functionality that can help make applications more efficient, more robust, and more manageable.

The major functional areas this repo addresses are:
- AI: Abstractions and middlewares for working with generative AI models and services.
- Compliance: Mechanisms to help manage application data according to privacy regulations and policies, which includes a data annotation framework, audit report generation, and telemetry redaction.
- Diagnostics: Provides a set of APIs that can be used to gather and report diagnostic information about the health of a service.
- Contextual Options: Extends the .NET Options model to enable experimentations in production.
- Resilience: Builds on top of the popular Polly library to provide sophisticated resilience pipelines to make applications robust to transient errors.
- Telemetry: Sophisticated telemetry facilities provide enhanced logging, metering, tracing, and latency measuring functionality.
- AspNetCore extensions: Provides different middlewares and extensions that can be used to build high-performance and high-availability ASP.NET Core services.
- Static Analysis: Curated static analysis settings to help improve your code.
- Testing: Dramatically simplifies testing around common .NET abstractions such as ILogger and the TimeProvider.

[![Build Status](https://dev.azure.com/dnceng-public/public/_apis/build/status%2Fdotnet%2Fextensions%2Fextensions-ci?branchName=main)](https://dev.azure.com/dnceng-public/public/_build/latest?definitionId=16&branchName=main)
[![Help Wanted](https://img.shields.io/github/issues/dotnet/extensions/help%20wanted?style=flat-square&color=%232EA043&label=help%20wanted)](https://github.com/dotnet/extensions/labels/help%20wanted)
[![Discord](https://img.shields.io/discord/732297728826277939?style=flat-square&label=Discord&logo=discord&logoColor=white&color=7289DA)](https://aka.ms/dotnet-discord)

## How can I contribute?

We welcome contributions! Many people all over the world have helped make this project better.

* [Contributing](CONTRIBUTING.md) explains what kinds of contributions we welcome
* [Build instructions](docs/building.md) explains how to build and test

## Reporting security issues and security bugs

Security issues and bugs should be reported privately, via email, to the Microsoft Security Response Center (MSRC) <secure@microsoft.com>. You should receive a response within 24 hours. If for some reason you do not, please follow up via email to ensure we received your original message. Further information, including the MSRC PGP key, can be found in the [Security TechCenter](https://www.microsoft.com/msrc/faqs-report-an-issue). You can also find these instructions in this repo's [Security doc](SECURITY.md).

Also see info about related [Microsoft .NET Core and ASP.NET Core Bug Bounty Program](https://www.microsoft.com/msrc/bounty-dot-net-core).

## Useful Links

* [.NET Core source index](https://source.dot.net) / [.NET Framework source index](https://referencesource.microsoft.com)
* [API Reference docs](https://docs.microsoft.com/dotnet/api)
* [.NET API Catalog](https://apisof.net) (incl. APIs from daily builds and API usage info)
* [API docs writing guidelines](https://github.com/dotnet/dotnet-api-docs/wiki) - useful when writing /// comments
* [.NET Discord Server](https://aka.ms/dotnet-discord) - a place to discuss the development of .NET and its ecosystem

## .NET Foundation

This project is a [.NET Foundation](https://www.dotnetfoundation.org/projects) project.

There are many .NET related projects on GitHub.

* [.NET home repo](https://github.com/Microsoft/dotnet) - links to 100s of .NET projects, from Microsoft and the community.
* [ASP.NET Core home](https://docs.microsoft.com/aspnet/core) - the best place to start learning about ASP.NET Core.

This project has adopted the code of conduct defined by the [Contributor Covenant](https://contributor-covenant.org) to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](https://www.dotnetfoundation.org/code-of-conduct).

General .NET OSS discussions: [.NET Foundation Discussions](https://github.com/dotnet-foundation/Home/discussions)

## License

.NET (including the runtime repo) is licensed under the [MIT](LICENSE) license.