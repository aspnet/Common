# AI Agent Web API

This is an AI Agent Web API application created from the `aiagent-webapi` template. This template is currently in a preview stage. If you have feedback, please take a [brief survey](https://aka.ms/dotnet/aiagent-webapi/preview1/survey).

## Prerequisites

<!--#if (IsOpenAI) -->
- An OpenAI API key
<!--#elif (IsAzureOpenAI) -->
- An Azure OpenAI service deployment
<!--#elif (IsOllama) -->
- Ollama installed locally with the llama3.2 model
<!--#endif -->

## Getting Started

### 1. Configure Your AI Service

<!--#if (IsOpenAI) -->
#### OpenAI Configuration

This application uses the OpenAI Platform (model: gpt-4o-mini). You'll need to configure your OpenAI API key:

**Using User Secrets (Recommended for Development)**

```bash
dotnet user-secrets set "OPENAI_KEY" "your-openai-api-key-here"
```

**Using Environment Variables**

Set the `OPENAI_KEY` environment variable:

- **Windows (PowerShell)**:
  ```powershell
  $env:OPENAI_KEY = "your-openai-api-key-here"
  ```

- **Linux/macOS**:
  ```bash
  export OPENAI_KEY="your-openai-api-key-here"
  ```

#### Get an OpenAI API Key

1. Visit [OpenAI Platform](https://platform.openai.com)
2. Sign in or create an account
3. Navigate to API Keys
4. Create a new API key
5. Copy your API key

<!--#elif (IsAzureOpenAI) -->
#### Azure OpenAI Configuration

This application uses Azure OpenAI service (model: gpt-4o-mini). You'll need to configure your Azure OpenAI endpoint<!--#if (!IsManagedIdentity) --> and API key<!--#endif -->:

**Using User Secrets (Recommended for Development)**

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR-DEPLOYMENT-NAME.openai.azure.com"
<!--#if (!IsManagedIdentity) -->
dotnet user-secrets set "AzureOpenAI:Key" "your-azure-openai-key-here"
<!--#endif -->
```

**Using Environment Variables**

- **Windows (PowerShell)**:
  ```powershell
  $env:AzureOpenAI__Endpoint = "https://YOUR-DEPLOYMENT-NAME.openai.azure.com"
<!--#if (!IsManagedIdentity) -->
  $env:AzureOpenAI__Key = "your-azure-openai-key-here"
<!--#endif -->
  ```

- **Linux/macOS**:
  ```bash
  export AzureOpenAI__Endpoint="https://YOUR-DEPLOYMENT-NAME.openai.azure.com"
<!--#if (!IsManagedIdentity) -->
  export AzureOpenAI__Key="your-azure-openai-key-here"
<!--#endif -->
  ```

<!--#if (IsManagedIdentity) -->
#### Managed Identity Authentication

This application is configured to use Azure Managed Identity for authentication. When deploying to Azure:

1. Ensure your Azure resource (App Service, Container Apps, etc.) has a managed identity enabled
2. Grant the managed identity access to your Azure OpenAI resource with the "Cognitive Services OpenAI User" role
3. No API key configuration is needed

For local development, ensure you're signed in to Azure CLI or have configured DefaultAzureCredential appropriately.

<!--#endif -->
#### Set Up Azure OpenAI

1. Visit [Azure Portal](https://portal.azure.com)
2. Create an Azure OpenAI resource
3. Deploy a model (e.g., gpt-4o-mini)
4. Copy your endpoint<!--#if (!IsManagedIdentity) --> and API key<!--#endif -->

<!--#elif (IsOllama) -->
#### Ollama Configuration

This application uses Ollama running locally (model: llama3.2). You'll need to have Ollama installed and the llama3.2 model downloaded:

1. Visit [Ollama](https://ollama.com) and follow the installation instructions for your platform
2. Once installed, download the llama3.2 model:
   ```bash
   ollama pull llama3.2
   ```
3. Ensure Ollama is running (it starts automatically after installation)

The application is configured to connect to Ollama at `http://localhost:11434`.

<!--#endif -->

### 2. Run the Application

```bash
dotnet run -lp https
```

The application will start and listen on:
- HTTP: `http://localhost:9991`
- HTTPS: `https://localhost:9992`

### 3. Test the Application

The application exposes OpenAI-compatible API endpoints. You can interact with the AI agents using any OpenAI-compatible client or tools.

In development environments, a `/devui/` route is mapped to the Agent Framework development UI (DevUI), and when the app is launched through an IDE a browser will open to this URL. DevUI provides a web-based UI for interacting with agents and workflows. DevUI operates as an OpenAI-compatible client using the Responses and Conversations endpoints.

## How It Works

This application demonstrates Agent Framework with:

1. **Writer Agent**: Writes short stories (300 words or less) about specified topics
2. **Editor Agent**: Edits stories to improve grammar and style, ensuring they stay under 300 words
3. **Publisher Workflow Agent**: A sequential workflow agent that combines the writer and editor agents

The agents are exposed through OpenAI-compatible API endpoints, making them easy to integrate with existing tools and applications.

## Template Parameters

When creating a new project, you can customize it using template parameters:

```bash
# Specify AI service provider
dotnet new aiagent-webapi --provider azureopenai

# Specify a custom chat model
dotnet new aiagent-webapi --chat-model gpt-4o

# Use API key authentication for Azure OpenAI
dotnet new aiagent-webapi --provider azureopenai --managed-identity false

# Use Ollama with a different model
dotnet new aiagent-webapi --provider ollama --chat-model llama3.1
```

### Available Parameters

- **`--provider`**: Choose the AI service provider
  - `azureopenai` - Azure OpenAI
  - `openai` - OpenAI Platform
  - `ollama` - Ollama (local development)

- **`--chat-model`**: Specify the chat model/deployment name
  - Default for OpenAI/Azure OpenAI: `gpt-4o-mini`
  - Default for Ollama: `llama3.2`

- **`--managed-identity`**: Use managed identity for Azure services (default: `true`)
  - Only applicable when `--provider azureopenai`

- **`--framework`**: Target framework (default: `net11.0`)
  - Options: `net11.0`, `net10.0`, `net9.0`, `net8.0`

## Project Structure

- `Program.cs` - Application entry point and configuration
- `appsettings.json` - Application configuration
- `Properties/launchSettings.json` - Launch profiles for development

## Learn More

- [AI apps for .NET developers](https://learn.microsoft.com/dotnet/ai)
- [Microsoft Agent Framework Documentation](https://aka.ms/dotnet/agent-framework/docs)
<!--#if (IsOpenAI) -->
- [OpenAI Platform](https://platform.openai.com)
<!--#elif (IsAzureOpenAI) -->
- [Azure OpenAI Service](https://azure.microsoft.com/products/ai-services/openai-service)
<!--#elif (IsOllama) -->
- [Ollama](https://ollama.com)
<!--#endif -->

## Troubleshooting

<!--#if (IsOpenAI) -->
**Problem**: Application fails with "Missing configuration: OPENAI_KEY"

**Solution**: Make sure you've configured your OpenAI API key using one of the methods described above.

**Problem**: API requests fail with authentication errors

**Solution**: Verify your OpenAI API key is valid. Check your usage limits and billing status on the OpenAI Platform.

<!--#elif (IsAzureOpenAI) -->
<!--#if (!IsManagedIdentity) -->
**Problem**: Application fails with "Missing configuration: AzureOpenAI:Endpoint" or "Missing configuration: AzureOpenAI:Key"

**Solution**: Make sure you've configured your Azure OpenAI endpoint and API key using one of the methods described above.

**Problem**: API requests fail with authentication errors

**Solution**: Verify your Azure OpenAI endpoint is correct and your API key is valid.

<!--#else -->
**Problem**: Application fails with "Missing configuration: AzureOpenAI:Endpoint"

**Solution**: Make sure you've configured your Azure OpenAI endpoint using one of the methods described above.

**Problem**: Managed identity authentication fails

**Solution**:
- Ensure your Azure resource has a system-assigned or user-assigned managed identity enabled
- Verify the managed identity has been granted the "Cognitive Services OpenAI User" role on your Azure OpenAI resource
- For local development, ensure you're signed in to Azure CLI: `az login`

**Problem**: API requests fail with authentication errors

**Solution**: Verify your Azure OpenAI endpoint is correct and your managed identity has the correct permissions.

<!--#endif -->

<!--#elif (IsOllama) -->
**Problem**: Application fails to connect to Ollama

**Solution**:
- Ensure Ollama is running. On macOS/Linux, check with `pgrep ollama`. On Windows, check Task Manager.
- Verify Ollama is accessible at `http://localhost:11434`
- Make sure you've downloaded the llama3.2 model: `ollama pull llama3.2`

**Problem**: Model responses are slow or time out

**Solution**: Ollama runs locally and performance depends on your hardware. Consider using a smaller model or ensuring your system has adequate resources (RAM, GPU if available).
<!--#endif -->
