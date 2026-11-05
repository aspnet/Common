# AI Chat with Custom Data

This project is an AI chat application that demonstrates how to chat with custom data using an AI language model. Please note that this template is currently in an early preview stage. If you have feedback, please take a [brief survey](https://aka.ms/dotnet-chat-templatePreview2-survey).

>[!NOTE]
> Before running this project you need to configure the API keys or endpoints for the providers you have chosen. See below for details specific to your choices.

#### ---#if (IsAzure)
### Prerequisites
To use Azure OpenAI or Azure AI Search, you need an Azure account. If you don't already have one, [create an Azure account](https://azure.microsoft.com/free/).

#### ---#endif
### Known Issues

#### Errors running Ollama or Docker

A recent incompatibility was found between Ollama and Docker Desktop. This issue results in runtime errors when connecting to Ollama, and the workaround for that can lead to Docker not working for Aspire projects.

This incompatibility can be addressed by upgrading to Docker Desktop 4.41.1. See [ollama/ollama#9509](https://github.com/ollama/ollama/issues/9509#issuecomment-2842461831) for more information and a link to install the version of Docker Desktop with the fix.

# Configure the AI Model Provider

#### ---#if (IsOpenAI)
## Using OpenAI

To call the OpenAI REST API, you will need an API key. To obtain one, first [create a new OpenAI account](https://platform.openai.com/signup) or [log in](https://platform.openai.com/login). Next, navigate to the API key page and select "Create new secret key", optionally naming the key. Make sure to save your API key somewhere safe and do not share it with anyone.

#### ---#if (hostIdentifier == "vs")
Configure your API key for this project, using .NET User Secrets:

1. In Visual Studio, right-click on the AIChatWeb-CSharp.AppHost project in the Solution Explorer and select "Manage User Secrets".
2. This will open a secrets.json file where you can store your API key without them being tracked in source control. Add the following key and value to the file:

   ```json
   {
     "ConnectionStrings:openai": "Key=YOUR-API-KEY"
   }
   ```

#### ---#else
From the command line, configure your API key for this project using .NET User Secrets by running the following commands:

```sh
cd AIChatWeb-CSharp.AppHost
dotnet user-secrets set ConnectionStrings:openai "Key=YOUR-API-KEY"
```
#### ---#endif
#### ---#endif
#### ---#if (IsOllama)
## Setting up a local environment for Ollama
This project is configured to run Ollama in a Docker container. Docker Desktop must be installed and running for the project to run successfully. An Ollama container will automatically start when running the application.

Download, install, and run Docker Desktop from the [official website](https://www.docker.com/). Follow the installation instructions specific to your operating system.

Note: Ollama and Docker are excellent open source products, but are not maintained by Microsoft.

#### ---#endif
#### ---#if (IsAzureOpenAI || IsAzureAISearch)
## Using Azure Provisioning

The project is set up to automatically provision Azure resources. When running the app for the first time, you will be prompted to provide Azure configuration values. For detailed instructions, see the [Local Provisioning documentation](https://learn.microsoft.com/dotnet/aspire/azure/local-provisioning#configuration).

#### ---#endif
#### ---#if (IsQdrant)

## Setting up a local environment for Qdrant
This project is configured to run Qdrant in a Docker container. Docker Desktop must be installed and running for the project to run successfully. A Qdrant container will automatically start when running the application.

Download, install, and run Docker Desktop from the [official website](https://www.docker.com/). Follow the installation instructions specific to your operating system.

Note: Qdrant and Docker are excellent open source products, but are not maintained by Microsoft.
#### ---#endif

# Running the application

## Using Visual Studio

1. Open the `.sln` file in Visual Studio.
2. Press `Ctrl+F5` or click the "Start" button in the toolbar to run the project.

## Using Visual Studio Code

1. Open the project folder in Visual Studio Code.
2. Install the [C# Dev Kit extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) for Visual Studio Code.
3. Once installed, Open the `Program.cs` file in the AIChatWeb-CSharp.AppHost project.
4. Run the project by clicking the "Run" button in the Debug view.

## Trust the localhost certificate

Several Aspire templates include ASP.NET Core projects that are configured to use HTTPS by default. If this is the first time you're running the project, an exception might occur when loading the Aspire dashboard. This error can be resolved by trusting the self-signed development certificate with the .NET CLI.

See [Troubleshoot untrusted localhost certificate in Aspire](https://learn.microsoft.com/dotnet/aspire/troubleshooting/untrusted-localhost-certificate) for more information.

# Updating JavaScript dependencies

This template leverages JavaScript libraries to provide essential functionality. These libraries are located in the wwwroot/lib folder of the AIChatWeb-CSharp.Web project. For instructions on updating each dependency, please refer to the README.md file in each respective folder.

# Learn More
To learn more about development with .NET and AI, check out the following links:

* [AI for .NET Developers](https://learn.microsoft.com/dotnet/ai/)
