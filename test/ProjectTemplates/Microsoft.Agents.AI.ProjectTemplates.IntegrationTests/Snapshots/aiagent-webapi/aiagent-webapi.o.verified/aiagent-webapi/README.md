# AI Agent Web API

This is an AI Agent Web API application created from the `aiagent-webapi` template. This template is currently in a preview stage. If you have feedback, please take a [brief survey](https://aka.ms/dotnet/aiagent-webapi/preview1/survey).

## Prerequisites

- Ollama installed locally with the llama3.2 model

## Getting Started

### 1. Configure Your AI Service

#### Ollama Configuration

This application uses Ollama running locally (model: llama3.2). You'll need to have Ollama installed and the llama3.2 model downloaded:

1. Visit [Ollama](https://ollama.com) and follow the installation instructions for your platform
2. Once installed, download the llama3.2 model:
   ```bash
   ollama pull llama3.2
   ```
3. Ensure Ollama is running (it starts automatically after installation)

The application is configured to connect to Ollama at `http://localhost:9996`.


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
- [Ollama](https://ollama.com)

## Troubleshooting

**Problem**: Application fails to connect to Ollama

**Solution**:
- Ensure Ollama is running. On macOS/Linux, check with `pgrep ollama`. On Windows, check Task Manager.
- Verify Ollama is accessible at `http://localhost:9996`
- Make sure you've downloaded the llama3.2 model: `ollama pull llama3.2`

**Problem**: Model responses are slow or time out

**Solution**: Ollama runs locally and performance depends on your hardware. Consider using a smaller model or ensuring your system has adequate resources (RAM, GPU if available).
