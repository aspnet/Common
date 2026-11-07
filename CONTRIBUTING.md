# Contributing Guide

> :warning: Please note, this document is a subset of [Contributing to .NET Runtime][net-contributing], make sure to read it first.

* [Reporting Issues](#reporting-issues)
  + [Identify Where to Report](#identify-where-to-report)
  + [Finding Existing Issues](#finding-existing-issues)
  + [Writing a Good API Proposal](#writing-a-good-api-proposal)
  + [Writing a Good Bug Report](#writing-a-good-bug-report)
    - [Why are Minimal Reproductions Important?](#why-are-minimal-reproductions-important)
    - [Are Minimal Reproductions Required?](#are-minimal-reproductions-required)
    - [How to Create a Minimal Reproduction](#how-to-create-a-minimal-reproduction)
* [Contributing Changes](#contributing-changes)
  + [DOs and DON'Ts](#dos-and-donts)
  + [Breaking Changes](#breaking-changes)
  + [Suggested Workflow](#suggested-workflow)
  + [Commit Messages](#commit-messages)
  + [PR Feedback](#pr-feedback)
  + [Help Wanted (Up for Grabs)](#help-wanted-up-for-grabs)
  + [Contributor License Agreement](#contributor-license-agreement)

You can contribute with issues, pull-requests, and general reviews of both issues and pull-requests. Simply filing issues for problems you encounter is a great way to contribute. Contributing implementations is greatly appreciated.

## Reporting Issues

We always welcome bug reports, API proposals and overall feedback. Here are a few tips on how you can make reporting your issue as effective as possible.

### Identify Where to Report

The .NET codebase is distributed across multiple repositories in the [.NET organization](https://github.com/dotnet). Depending on the feedback you might want to file the issue on a different repo. Here are a few common repos:

* [dotnet/runtime](https://github.com/dotnet/runtime) .NET runtime, libraries and shared host installers.
* [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) ASP.NET Core.
* [azure/dotnet-extensions-experimental](https://github.com/azure/dotnet-extensions-experimental) Extensions for Azure.

### Finding Existing Issues

Before filing a new issue, please search our [open issues](https://github.com/dotnet/extensions/issues) to check if it already exists.

If you do find an existing issue, please include your own feedback in the discussion. Do consider upvoting (👍 reaction) the original post, as this helps us prioritize popular issues in our backlog.

### Writing a Good API Proposal

File an API proposal and discuss new public APIs with the area owners before implementation. Use the [API Suggestion issue template](https://github.com/dotnet/extensions/issues/new?assignees=&labels=api-suggestion&template=02_api_proposal.yml&title=%5BAPI+Proposal%5D%3A+) and include the motivation, representative usage, and an API sketch. Formal API Review is a separate step from this owner discussion: it is required for the higher-impact API designs below, but is not a gate for every public API addition. The [API review process](https://github.com/dotnet/runtime/blob/main/docs/project/api-review-process.md) describes the proposal and review-board workflow used by .NET repositories.

Request formal API Review when an API establishes a broadly reusable contract, introduces a substantial new concept, overlaps another .NET product area, or is likely to be used transitively by many products. The review should normally be approved before that API becomes stable. A small additive API that follows an established pattern and is confined to one library may be approved by its area owners without a board review. Area owners can request review for any API when an outside perspective would help.

#### Library-specific API Review guidance

These are defaults for formal board review, not substitutes for the API proposal and area-owner discussion. Apply the higher bar when a change affects a shared contract or reaches beyond the package's usual audience. NuGet.org download totals and dependency relationships inform these recommendations, but are signals of reach rather than fixed thresholds. A trailing `*` denotes all packages in that family.

| Library or package family | Formal API Review guidance |
| --- | --- |
| `Microsoft.Extensions.Compliance*`, `Microsoft.Extensions.Telemetry*`, `Microsoft.Extensions.Resilience*`, `Microsoft.Extensions.Http.Diagnostics`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Diagnostics.ExceptionSummarization`, `Microsoft.Extensions.DependencyInjection.AutoActivation`, `Microsoft.Extensions.AmbientMetadata.Application`, `Microsoft.Extensions.ServiceDiscovery*`, `Microsoft.Extensions.Caching.Hybrid` | Use formal review for substantive public contracts and changes with cross-package or transitive impact. These packages have broad downstream reach; a narrowly scoped addition following an established pattern may be handled by area owners. |
| `Microsoft.Extensions.AI*` | Review selectively. The area owners should seek an outside perspective for large new concepts, foundational abstractions, or designs that may overlap other product areas. Routine feature APIs within an established AI pattern do not each require board review. The high adoption and transitive use of `Microsoft.Extensions.AI.Abstractions` raise the bar for changes to its shared contracts. |
| `Microsoft.Extensions.VectorData*`, `Microsoft.Extensions.DataIngestion*`, `Microsoft.Extensions.Documents*`, `Microsoft.Extensions.DocumentExtraction*` | Seek formal review for general-purpose models, abstractions, and contracts intended to be shared across providers or product areas. The shared Documents model and extraction APIs are examples where overlap and broad applicability make review valuable. Provider-specific adapters can usually follow area-owner review. |
| Other `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` libraries, including testing, integration, and provider packages | Area-owner review is normally sufficient for routine, library-specific APIs. Request formal review when a design becomes a general contract, crosses package boundaries, or is likely to have substantial downstream impact. |

#### Using `[Experimental]`

Use `[Experimental]` for a public API whose design or behavior is still being validated and may change. It is appropriate to ship an API as experimental in an otherwise stable package; it does not require a preview SDK or target framework. Apply the attribute before the API is first released, use the library's established diagnostic ID for the feature, and keep the diagnostic-ID documentation and API baseline in sync. See [.NET's `ExperimentalAttribute` documentation](https://learn.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.experimentalattribute) and this repository's [experimental diagnostic IDs](docs/list-of-diagnostics.md#experiments).

Because these libraries generally ship monthly, experimental APIs can gather feedback and iterate in later releases rather than waiting for an annual platform release. Use that cadence to validate genuinely evolving designs, not to label a settled API experimental or bypass review where it is expected.

An API does not have to wait for formal review approval before it can be implemented or released as experimental when early implementation and user feedback will help validate the design. This is appropriate only with area-owner agreement, an API proposal describing the design and its open questions, and a clear commitment to complete any expected review before removing `[Experimental]` or shipping the API as stable. For broadly adopted or cross-product contracts, seek review before the first experimental release when practical; an experimental release is not a substitute for review.

`[Experimental]` signals that consumers receive no compatibility guarantee for that API; it is not a signal that the API is low quality or that consumers cannot depend on it. Changes or removal are possible, but assess and communicate their impact. Use particular care with experimental APIs in highly adopted packages and with APIs consumed transitively by other products. Coordinate breaking changes with known downstream consumers and provide migration guidance where practical. Do not remove `[Experimental]` until the API is ready to join the stable public contract and any required formal review is complete.

### Writing a Good Bug Report

Good bug reports make it easier for maintainers to verify and root cause the underlying problem. The better a bug report, the faster the problem will be resolved. Ideally, a bug report should contain the following information:

* A high-level description of the problem.
* A _minimal reproduction_, i.e. the smallest size of code/configuration required to reproduce the wrong behavior.
* A description of the _expected behavior_, contrasted with the _actual behavior_ observed.
* Information on the environment: OS/distro, CPU arch, SDK version, etc.
* Additional information, e.g. is it a regression from previous versions? are there any known workarounds?

When ready to submit a bug report, please use the [Bug Report issue template](https://github.com/dotnet/extensions/issues/new?assignees=&labels=&template=01_bug_report.yml).

#### Why are Minimal Reproductions Important?

A reproduction lets maintainers verify the presence of a bug, and diagnose the issue using a debugger. A _minimal_ reproduction is the smallest possible console application demonstrating that bug. Minimal reproductions are generally preferable since they:

1. Focus debugging efforts on a simple code snippet,
2. Ensure that the problem is not caused by unrelated dependencies/configuration,
3. Avoid the need to share production codebases.

#### Are Minimal Reproductions Required?

In certain cases, creating a minimal reproduction might not be practical (e.g. due to nondeterministic factors, external dependencies). In such cases you would be asked to provide as much information as possible, for example by sharing a memory dump of the failing application. If maintainers are unable to root cause the problem, they might still close the issue as not actionable. While not required, minimal reproductions are strongly encouraged and will significantly improve the chances of your issue being prioritized and fixed by the maintainers.

#### How to Create a Minimal Reproduction

The best way to create a minimal reproduction is gradually removing code and dependencies from a reproducing app, until the problem no longer occurs. A good minimal reproduction:

* Excludes all unnecessary types, methods, code blocks, source files, nuget dependencies and project configurations.
* Contains documentation or code comments illustrating expected vs actual behavior.
* If possible, avoids performing any unneeded IO or system calls. For example, can the ASP.NET based reproduction be converted to a plain old console app?

## Contributing Changes

Project maintainers will merge changes that improve the product significantly.

The [Pull Request Guide][pr-guide] and [Copyright][copyright-guide] docs define additional guidance.

### DOs and DON'Ts

Please do:

* **DO** follow our [coding style][coding-style] (C# code-specific)<br/>
  We strive to wrap the lines around 120 mark, and it's acceptable to stretch to no more than 150 chars (with some exceptions being URLs). [EditorGuidelines VS extension](https://marketplace.visualstudio.com/items?itemName=PaulHarrington.EditorGuidelines) makes it easier to visualise (see https://github.com/dotnet/winforms/pull/4836).
* **DO** give priority to the current style of the project or file you're changing even if it diverges from the general guidelines.
* **DO** include tests when adding new features. When fixing bugs, start with
  adding a test that highlights how the current behavior is broken.
* **DO** keep the discussions focused. When a new or related topic comes up
  it's often better to create new issue than to side track the discussion.
* **DO** blog and tweet (or whatever) about your contributions, frequently!

Please do not:

* **DON'T** make PRs for style changes.
* **DON'T** surprise us with big pull requests. Instead, file an issue and start
  a discussion so we can agree on a direction before you invest a large amount
  of time.
* **DON'T** commit code that you didn't write. If you find code that you think is a good fit to add to .NET Core, file an issue and start a discussion before proceeding.
* **DON'T** submit PRs that alter licensing related files or headers. If you believe there's a problem with them, file an issue and we'll be happy to discuss it.
* **DON'T** add API additions without filing an issue and discussing with us first. Follow the [API proposal and formal review policy](#writing-a-good-api-proposal).

### Breaking Changes

Stable APIs must maintain [API signature][breaking-changes-public-contract] and behavioral compatibility. Contributions that break stable APIs will be rejected unless an exception is approved through the repository's compatibility process. Breaking changes to experimental APIs are possible, but are not automatic: discuss them with area owners, assess downstream impact, and follow the guidance in [Using `[Experimental]`](#using-experimental).

### Suggested Workflow

We use and recommend the following workflow:

1. Create an issue for your work.
    - You can skip this step for trivial changes.
    - Reuse an existing issue on the topic, if there is one.
    - Get agreement from the team and the community that your proposed change is a good one.
    - If your change adds a new API, follow the [API proposal and formal review policy](#writing-a-good-api-proposal).
    - Clearly state that you are going to take on implementing it, if that's the case. You can request that the issue be assigned to you. Note: The issue filer and the implementer don't have to be the same person.
2. Create a personal fork of the repository on GitHub (if you don't already have one).
3. In your fork, create a branch off of main (`git checkout -b mybranch`).
    - Name the branch so that it clearly communicates your intentions, such as issue-123 or githubhandle-issue.
    - Branches are useful since they isolate your changes from incoming changes from upstream. They also enable you to create multiple PRs from the same fork.
4. Make and commit your changes to your branch.
    - [Workflow Instructions](docs/building.md) explains how to build and test.
    - Please follow our [Commit Messages](#commit-messages) guidance.
5. Add new tests corresponding to your change, if applicable.
6. Build the repository with your changes.
    - Make sure that the builds are clean.
    - Make sure that the tests are all passing, including your new tests.
7. Create a pull request (PR) against the dotnet/extensions repository's **main** branch.
    - State in the description what issue or improvement your change is addressing.
    - Check if all the Continuous Integration checks are passing.
8. Wait for feedback or approval of your changes from the area owners.
    - Details about the pull request [review procedure](docs/pr-guide.md).
9. When area owners have signed off, and all checks are green, your PR will be merged.
    - The next official build will automatically include your change.
    - You can delete the branch you used for making the change.

### Commit Messages

Please format commit messages as follows (based on [A Note About Git Commit Messages][note-about-git-commit-messages]). Also, use the [GitHub keywords][github-keywords]:

    ```
    Summarize change in 50 characters or less

    Fixes #42

    Provide more detail after the first line. Leave one blank line below the
    summary and wrap all lines at 72 characters or less.

    If the change fixes an issue, leave another blank line after the final
    paragraph and indicate which issue is fixed in the specific format
    below.
    ```

Also do your best to factor commits appropriately, not too large with unrelated things in the same commit, and not too small with the same small change applied N times in N different commits.

### PR Feedback

Project maintainers and community members will provide feedback on your change. Community feedback is highly valued. You will often see the absence of team feedback if the community has already provided good review feedback.

One or more project maintainers members will review every PR prior to merge. They will often reply with "LGTM, modulo comments". That means that the PR will be merged once the feedback is resolved. "LGTM" == "looks good to me".

There are lots of thoughts and [approaches](https://github.com/antlr/antlr4-cpp/blob/master/CONTRIBUTING.md#emoji) for how to efficiently discuss changes. It is best to be clear and explicit with your feedback. Please be patient with people who might not understand the finer details about your approach to feedback.

### Help Wanted (Up for Grabs)

The team marks the most straightforward issues as [help wanted](https://github.com/dotnet/extensions/labels/help%20wanted). This set of issues is the place to start if you are interested in contributing but new to the codebase.

### Contributor License Agreement

You must sign a [.NET Foundation Contribution License Agreement (CLA)](https://cla.dotnetfoundation.org) before your PR will be merged. This is a one-time requirement for projects in the .NET Foundation. You can read more about [Contribution License Agreements (CLA)](http://en.wikipedia.org/wiki/Contributor_License_Agreement) on Wikipedia.

The agreement: [net-foundation-contribution-license-agreement.pdf](https://github.com/dotnet/home/blob/master/guidance/net-foundation-contribution-license-agreement.pdf)

You don't have to do this up-front. You can simply clone, fork, and submit your pull-request as usual. When your pull-request is created, it is classified by a CLA bot. If the change is trivial (for example, you just fixed a typo), then the PR is labelled with `cla-not-required`. Otherwise it's classified as `cla-required`. Once you signed a CLA, the current and all future pull-requests will be labelled as `cla-signed`.


[comment]: <> (URI Links)

[api-review-process]: https://github.com/dotnet/runtime/blob/main/docs/project/api-review-process.md
[breaking-changes]: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-changes.md
[breaking-changes-public-contract]: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-changes.md#bucket-1-public-contract
[coding-style]: https://github.com/dotnet/runtime/blob/master/docs/coding-guidelines/coding-style.md
[copyright-guide]: https://github.com/dotnet/runtime/blob/main/docs/project/copyright.md
[github-keywords]: https://docs.github.com/get-started/writing-on-github/working-with-advanced-formatting/using-keywords-in-issues-and-pull-requests#linking-a-pull-request-to-an-issue
[net-contributing]: https://github.com/dotnet/runtime/blob/main/CONTRIBUTING.md
[note-about-git-commit-messages]: http://tbaggery.com/2008/04/19/a-note-about-git-commit-messages.html
[pr-guide]: https://github.com/dotnet/runtime/blob/main/docs/workflow/ci/pr-guide.md
