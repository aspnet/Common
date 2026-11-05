# The Microsoft.Extensions.AI.Evaluation libraries

`Microsoft.Extensions.AI.Evaluation` is a set of .NET libraries defined in the following NuGet packages that have been designed to work together to support building processes for evaluating the quality of AI software.

* [`Microsoft.Extensions.AI.Evaluation`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation) - Defines core abstractions and types for supporting evaluation.
* [`Microsoft.Extensions.AI.Evaluation.Quality`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Quality) - Contains evaluators that can be used to evaluate the quality of AI responses in your projects including Relevance, Truth, Completeness, Fluency, Coherence, Retrieval, Equivalence and Groundedness.
* [`Microsoft.Extensions.AI.Evaluation.Safety`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Safety) - Contains a set of evaluators that are built atop the Azure AI Foundry Evaluation service that can be used to evaluate the content safety of AI responses in your projects including Protected Material, Groundedness Pro, Ungrounded Attributes, Hate and Unfairness, Self Harm, Violence, Sexual, Code Vulnerability and Indirect Attack.
* [`Microsoft.Extensions.AI.Evaluation.NLP`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.NLP) - Contains a set of evaluators that implement common algorithms for evaluating machine translation and natural
language processing tasks. Evaluators currently include BLEU, GLEU and F1 scores.
* [`Microsoft.Extensions.AI.Evaluation.Reporting`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Reporting) - Contains support for caching LLM responses, storing the results of evaluations and generating reports from that data.
* [`Microsoft.Extensions.AI.Evaluation.Reporting.Azure`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Reporting.Azure) - Supports the `Microsoft.Extensions.AI.Evaluation.Reporting` library with an implementation for caching LLM responses and storing the evaluation results in an Azure Storage container.
* [`Microsoft.Extensions.AI.Evaluation.Console`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Console) - A command line dotnet tool for generating reports and managing evaluation data.

## Install the packages

From the command-line:

```console
dotnet add package Microsoft.Extensions.AI.Evaluation
dotnet add package Microsoft.Extensions.AI.Evaluation.Quality
dotnet add package Microsoft.Extensions.AI.Evaluation.Safety
dotnet add package Microsoft.Extensions.AI.Evaluation.Reporting
dotnet add package Microsoft.Extensions.AI.Evaluation.NLP
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.AI.Evaluation" Version="[CURRENTVERSION]" />
  <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Quality" Version="[CURRENTVERSION]" />
  <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Safety" Version="[CURRENTVERSION]" />
  <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" Version="[CURRENTVERSION]" />
  <PackageReference Include="Microsoft.Extensions.AI.Evaluation.NLP" Version="[CURRENTVERSION]" />
</ItemGroup>
```

You can optionally add the `Microsoft.Extensions.AI.Evaluation.Reporting.Azure` package in either of these places if you need Azure Storage support.

## Install the command line tool

```console
dotnet tool install Microsoft.Extensions.AI.Evaluation.Console --create-manifest-if-needed
```

## Usage examples

For `Microsoft.Extensions.AI.Evaluation` library usage examples, see the following tutorials:

* [Quickstart: Evaluate response quality](https://learn.microsoft.com/dotnet/ai/evaluation/evaluate-ai-response)
* [Tutorial: Evaluate response quality with caching and reporting](https://learn.microsoft.com/dotnet/ai/evaluation/evaluate-with-reporting)
* [Tutorial: Evaluate response safety with caching and reporting](https://learn.microsoft.com/dotnet/ai/evaluation/evaluate-safety)

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
