# AI Agent Web API

This is an AI Agent Web API application created from the `aiagent-webapi` template. This template is currently in a preview stage. If you have feedback, please take a [brief survey](https://aka.ms/dotnet/aiagent-webapi/preview1/survey).

## Prerequisites

- An Azure OpenAI service deployment

## Getting Started

### 1. Configure Your AI Service

#### Azure OpenAI Configuration


**Using User Secrets (Recommended for Development)**

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR-DEPLOYMENT-NAME.openai.azure.com"
dotnet user-secrets set "AzureOpenAI:Key" "your-azure-openai-key-here"
```

**Using Environment Variables**

- **Windows (PowerShell)**:
  ```powershell
  $env:AzureOpenAI__Endpoint = "https://YOUR-DEPLOYMENT-NAME.openai.azure.com"
  $env:AzureOpenAI__Key = "your-azure-openai-key-here"
  ```

- **Linux/macOS**:
  ```bash
  export AzureOpenAI__Endpoint="https://YOUR-DEPLOYMENT-NAME.openai.azure.com"
  export AzureOpenAI__Key="your-azure-openai-key-here"
  ```

#### Set Up Azure OpenAI

1. Visit [Azure Portal](https://portal.azure.com)
2. Create an Azure OpenAI resource
3. Deploy a model (e.g., gpt-4o-mini)


### 2. Run the Application

```bash
dotnet run -lp https
```

The application will start and listen on:
- HTTP: `http://localhost:9996`
- HTTPS: `https://localhost:9995`

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
- [Azure OpenAI Service](https://azure.microsoft.com/products/ai-services/openai-service)

## Troubleshooting

**Problem**: Application fails with "Missing configuration: AzureOpenAI:Endpoint" or "Missing configuration: AzureOpenAI:Key"

**Solution**: Make sure you've configured your Azure OpenAI endpoint and API key using one of the methods described above.

**Problem**: API requests fail with authentication errors

**Solution**: Verify your Azure OpenAI endpoint is correct and your API key is valid.


