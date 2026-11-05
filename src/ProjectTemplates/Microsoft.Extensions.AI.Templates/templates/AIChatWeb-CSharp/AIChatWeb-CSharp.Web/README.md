# AI Chat with Custom Data

This project is an AI chat application that demonstrates how to chat with custom data using an AI language model. Please note that this template is currently in an early preview stage. If you have feedback, please take a [brief survey](https://aka.ms/dotnet-chat-templatePreview2-survey).

>[!NOTE]
> Before running this project you need to configure the API keys or endpoints for the providers you have chosen. See below for details specific to your choices.

#### ---#if (IsAzure)
### Prerequisites
To use Azure OpenAI or Azure AI Search, you need an Azure account. If you don't already have one, [create an Azure account](https://azure.microsoft.com/free/).

#### ---#endif
#### ---#if (IsOllama)
### Known Issues

#### Errors running Ollama or Docker

A recent incompatibility was found between Ollama and Docker Desktop. This issue results in runtime errors when connecting to Ollama, and the workaround for that can lead to Docker not working for Aspire projects.

This incompatibility can be addressed by upgrading to Docker Desktop 4.41.1. See [ollama/ollama#9509](https://github.com/ollama/ollama/issues/9509#issuecomment-2842461831) for more information and a link to install the version of Docker Desktop with the fix.

#### ---#endif
# Configure the AI Model Provider
#### ---#if (IsOpenAI)
## Using OpenAI

To call the OpenAI REST API, you will need an API key. To obtain one, first [create a new OpenAI account](https://platform.openai.com/signup) or [log in](https://platform.openai.com/login). Next, navigate to the API key page and select "Create new secret key", optionally naming the key. Make sure to save your API key somewhere safe and do not share it with anyone.

#### ---#if (hostIdentifier == "vs")
Configure your API key for this project, using .NET User Secrets:

1. In Visual Studio, right-click on your project in the Solution Explorer and select "Manage User Secrets".
2. This will open a secrets.json file where you can store your API key without them being tracked in source control. Add the following key and value to the file:

   ```json
   {
     "OpenAI:Key": "YOUR-API-KEY"
   }
   ```

#### ---#else
From the command line, configure your API key for this project using .NET User Secrets by running the following commands:

```sh
cd <<your-project-directory>>
dotnet user-secrets set OpenAI:Key YOUR-API-KEY
```

#### ---#endif
#### ---#endif
#### ---#if (IsOllama)
## Setting up a local environment using Ollama
This project is configured to use Ollama, an application that allows you to run AI models locally on your workstation. Note: Ollama is an excellent open source product, but it is not maintained by Microsoft.

### 1. Install Ollama
First, download and install Ollama from their [official website](https://www.ollama.com). Follow the installation instructions specific to your operating system.

### 2. Choose and Install Models
This project uses the `llama3.2` and `all-minilm` language models. To install these models, use the following commands in your terminal once Ollama has been installed:

```sh
ollama pull llama3.2
ollama pull all-minilm
```

### 3. Learn more about Ollama
Once the models are installed, you can start using them in your application. Refer to the [Ollama documentation](https://github.com/ollama/ollama/blob/main/docs/README.md) for detailed instructions on how to explore models locally.

#### ---#endif
#### ---#if (IsAzureOpenAI)
## Using Azure OpenAI

To use Azure OpenAI, you will need an Azure account and an Azure OpenAI Service resource. For detailed instructions, see the [Azure OpenAI Service documentation](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource).

### 1. Create an Azure OpenAI Service Resource
[Create an Azure OpenAI Service resource](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource?pivots=web-portal).

### 2. Deploy the Models
Deploy the `gpt-4o-mini` and `text-embedding-3-small` models to your Azure OpenAI Service resource. When creating those deployments, give them the same names as the models (`gpt-4o-mini` and `text-embedding-3-small`). See the Azure OpenAI documentation to learn how to [Deploy a model](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource?pivots=web-portal#deploy-a-model).

#### ---#if (IsManagedIdentity)
### 3. Configure Azure OpenAI for Keyless Authentication
This template is configured to use keyless authentication (also known as Managed Identity, with Entra ID). In the Azure Portal, when viewing the Azure OpenAI resource you just created, view access control settings and assign yourself the `Azure AI Developer` role. [Learn more about configuring authentication for local development](https://learn.microsoft.com/azure/developer/ai/keyless-connections?tabs=csharp%2Cazure-cli#authenticate-for-local-development).

### 4. Configure Azure OpenAI Endpoint
Configure your Azure OpenAI endpoint for this project, using .NET User Secrets:
   1. In the Azure Portal, navigate to your Azure OpenAI resource.
   2. Copy the "Endpoint" URL from the "Keys and Endpoint" section.
#### ---#if (hostIdentifier == "vs")
   3. In Visual Studio, right-click on your project in the Solution Explorer and select "Manage User Secrets".
   4. This will open a `secrets.json` file where you can store your Azure OpenAI endpoint without it being tracked in source control. Add the following key and value to the file:

      ```json
      {
        "AzureOpenAI:Endpoint": "YOUR-AZURE-OPENAI-ENDPOINT"
      }
      ```
#### ---#else
   3. From the command line, configure your Azure OpenAI endpoint for this project using .NET User Secrets by running the following commands:

      ```sh
      cd <<your-project-directory>>
      dotnet user-secrets set AzureOpenAI:Endpoint YOUR-AZURE-OPENAI-ENDPOINT
      ```
#### ---#endif

Make sure to replace `YOUR-AZURE-OPENAI-ENDPOINT` with your actual Azure OpenAI endpoint, formatted like https://YOUR-DEPLOYMENT-NAME.openai.azure.com/ (do not include any path after .openai.azure.com/).
#### ---#else
### 3. Configure API Key and Endpoint
Configure your Azure OpenAI API key and endpoint for this project, using .NET User Secrets:
   1. In the Azure Portal, navigate to your Azure OpenAI resource.
   2. Copy the "Endpoint" URL and "Key 1" from the "Keys and Endpoint" section.
#### ---#if (hostIdentifier == "vs")
   3. In Visual Studio, right-click on your project in the Solution Explorer and select "Manage User Secrets".
   4. This will open a secrets.json file where you can store your API key and endpoint without it being tracked in source control. Add the following keys & values to the file:

      ```json
      {
        "AzureOpenAI:Key": "YOUR-AZURE-OPENAI-KEY",
        "AzureOpenAI:Endpoint": "YOUR-AZURE-OPENAI-ENDPOINT"
      }
      ```
#### ---#else
   3. From the command line, configure your API key and endpoint for this project using .NET User Secrets by running the following commands:

      ```sh
      cd <<your-project-directory>>
      dotnet user-secrets set AzureOpenAI:Key YOUR-AZURE-OPENAI-KEY
      dotnet user-secrets set AzureOpenAI:Endpoint YOUR-AZURE-OPENAI-ENDPOINT
      ```
#### ---#endif

Make sure to replace `YOUR-AZURE-OPENAI-KEY` and `YOUR-AZURE-OPENAI-ENDPOINT` with your actual Azure OpenAI key and endpoint. Make sure your endpoint URL is formatted like https://YOUR-DEPLOYMENT-NAME.openai.azure.com/ (do not include any path after .openai.azure.com/).

#### ---#endif
#### ---#endif
#### ---#if (IsAzureAISearch)
## Configure Azure AI Search

To use Azure AI Search, you will need an Azure account and an Azure AI Search resource. For detailed instructions, see the [Azure AI Search documentation](https://learn.microsoft.com/azure/search/search-create-service-portal).

### 1. Create an Azure AI Search Resource
Follow the instructions in the [Azure portal](https://portal.azure.com/) to create an Azure AI Search resource. Note that there is a free tier for the service but it is not currently the default setting on the portal.

Note that if you previously used the same Azure AI Search resource with different model using this project name, you may need to delete your `data-AIChatWeb-CSharp.Web-chunks` and `data-AIChatWeb-CSharp.Web-documents` indexes using the [Azure portal](https://portal.azure.com/) first before continuing; otherwise, data ingestion may fail due to a vector dimension mismatch.

#### ---#if (IsManagedIdentity)
### 2. Configure Azure AI Search for Keyless Authentication
This template is configured to use keyless authentication (also known as Managed Identity, with Entra ID). Before continuing, you'll need to configure your Azure AI Search resource to support this. [Learn more](https://learn.microsoft.com/azure/search/keyless-connections).  After creation, ensure that you have selected Role-Based Access Control (RBAC) under Settings > Keys, as this is not the default. Assign yourself the roles called out for local development. [Learn more](https://learn.microsoft.com/azure/search/keyless-connections#roles-for-local-development).

### 3. Set the Azure AI Search Endpoint for this app
   Configure your Azure AI Search endpoint for this project, using .NET User Secrets:
   1. In the Azure Portal, navigate to your Azure AI Search resource.
   2. Copy the "URL" from the "Overview" section.
#### ---#if (hostIdentifier == "vs")
   3. In Visual Studio, right-click on your project in the Solution Explorer and select "Manage User Secrets".
   4. This will open a `secrets.json` file where you can store your Azure AI Search endpoint securely. Add the following key & value to the file:

      ```json
      {
        "AzureAISearch:Endpoint": "YOUR-AZURE-AI-SEARCH-ENDPOINT"
      }
      ```
#### ---#else
   3. From the command line, configure your Azure AI Search endpoint for this project using .NET User Secrets by running the following commands:

      ```sh
      cd <<your-project-directory>>
      dotnet user-secrets set AzureAISearch:Endpoint YOUR-AZURE-AI-SEARCH-ENDPOINT
      ```
#### ---#endif

Make sure to replace `YOUR-AZURE-AI-SEARCH-ENDPOINT` with your actual Azure AI Search endpoint.

#### ---#else
### 3. Configure API Key and Endpoint
   Configure your Azure AI Search API key and endpoint for this project, using .NET User Secrets:
   1. In the Azure Portal, navigate to your Azure AI Search resource.
   2. Copy the "Endpoint" URL and "Primary admin key" from the "Keys" section.
#### ---#if (hostIdentifier == "vs")
   3. In Visual Studio, right-click on your project in the Solution Explorer and select "Manage User Secrets".
   4. This will open a `secrets.json` file where you can store your API key and endpoint without them being tracked in source control. Add the following keys and values to the file:

      ```json
      {
        "AzureAISearch:Key": "YOUR-AZURE-AI-SEARCH-KEY",
        "AzureAISearch:Endpoint": "YOUR-AZURE-AI-SEARCH-ENDPOINT"
      }
      ```
#### ---#else
   3. From the command line, configure your API key and endpoint for this project using .NET User Secrets by running the following commands:

      ```sh
      cd <<your-project-directory>>
      dotnet user-secrets set AzureAISearch:Key YOUR-AZURE-AI-SEARCH-KEY
      dotnet user-secrets set AzureAISearch:Endpoint YOUR-AZURE-AI-SEARCH-ENDPOINT
      ```
#### ---#endif
Make sure to replace `YOUR-AZURE-AI-SEARCH-KEY` and `YOUR-AZURE-AI-SEARCH-ENDPOINT` with your actual Azure AI Search key and endpoint.

#### ---#endif

# Running the application

## Using Visual Studio

1. Open the `.csproj` file in Visual Studio.
2. Press `Ctrl+F5` or click the "Start" button in the toolbar to run the project.

## Using Visual Studio Code

1. Open the project folder in Visual Studio Code.
2. Install the [C# Dev Kit extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) for Visual Studio Code.
3. Once installed, Open the `Program.cs` file.
4. Run the project by clicking the "Run" button in the Debug view.

# Updating JavaScript dependencies

This template leverages JavaScript libraries to provide essential functionality. These libraries are located in the wwwroot/lib folder. For instructions on updating each dependency, please refer to the README.md file in each respective folder.

# Learn More
To learn more about development with .NET and AI, check out the following links:

* [AI for .NET Developers](https://learn.microsoft.com/dotnet/ai/)
