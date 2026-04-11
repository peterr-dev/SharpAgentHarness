# SharpAgentHarness

SharpAgentHarness is a C#/.NET project that implements a minimal, general-purpose agent harness on top of the OpenAI Responses API with no external library dependencies beyond .NET itself. It demonstrates session management, tool calling, event tracing, and a clean REST API for experimenting with agentic concepts.

## What This Project Demonstrates

- Designing a small foundational agent harness in C#/.NET, using only built-in .NET libraries so the core mechanics are easy to inspect.
- Building a typed wrapper around a pragmatic subset of the OpenAI Responses API, to keep the integration explicit and focused only on the API capabilities the harness uses.
- Exposing agent functionality through a clean ASP.NET REST API, supporting flexible use via alternative UIs, command line tools or schedulers.

## Features

- A clean C#/.NET implementation of a minimal agent harness which doesn't have any dependencies outside of the .NET Framework.
- Strongly typed models of key agent concepts, including a pragmatic subset of the OpenAI Responses API.
- A REST API for interacting with the agent; creating sessions, sending messages, and inspecting sessions and event traces.
- Event tracing for visibility of session activity.
- Function tools which can be organised into toolkits for specific use cases.
- A lightweight Web UI for interacting with the agent's API.

![web UI screenshot](./web%20UI.png)

## Project Structure

- `Agent` - Agent harness exposing a REST API (and lightweight Web UI).
- `Tests` - A small set of integration-style tests that validate core aspects of harness behaviour.

## Architecture

The harness is exposed through a REST API hosted by the `Agent` project, which also hosts a basic Web UI for exercising the API endpoints.

Internally, the main elements of the agent are:

- `Harness` - entry point used by the API to send messages to the agent.
- `Session` - manages the state of a single conversation with the agent.
- `Turn` - orchestrates a single agent turn within a `Session`, starting from a user message, handling any resulting tool calls and tool results, and returning the final output message.
- `LlmRequest` and `LlmResponse` - model a strongly typed, pragmatic subset of the OpenAI Responses API.

The repo also contains a `Tests` project with a small set of integration-style tests for specific test cases such as verifying prompt caching behaviour.  

## Design Choices

The harness takes an intentionally opinionated approach:

- Only OpenAI's Responses API is currently supported.
- `previous_response_id` is used to simplify conversational state handling.
- `Tool`s are organised into named `Toolkit`s.
- Each `Session` selects one `Toolkit` up front, and those tools are provided to the LLM on each turn.
- `strict` mode is always used for function tools, in line with OpenAI guidance.
- `prompt_cache_key` usage is structured to improve the likelihood of prompt caching.
- Sessions and events are persisted in memory only.
- Streaming responses are not currently supported.

These decisions were made to keep the harness small, focused, and easy to reason about while exploring agentic concepts.

## Running the Project

These instructions assume you have opened the repository in VS Code.

To run the harness locally you need:

- .NET 9 SDK.
- An OpenAI API key stored in the `OPENAI_API_KEY` environment variable.

In Visual Studio Code, Run > Start Debugging (or F5) should start the ASP.NET application that serves both the agent's REST API and the lightweight Web UI. When running, use these local URLs:

- API health check: `http://localhost:5205/api` or `https://localhost:7000/api`
- Web UI: `http://localhost:5205/ui.html` or `https://localhost:7000/ui.html`

The UI defaults its base API URL to `http://localhost:5205/api`, so if you use the HTTPS endpoint instead, update that field in the page before sending requests.

## Example Flow

**TBC**.

## API

Interaction with the agent is via a REST API served by the `Agent` application.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api` | Basic API health check. |
| `POST` | `/api/sessions` | Create a new session. |
| `GET` | `/api/sessions/{sessionId}` | Get the current state of a session. |
| `GET` | `/api/sessions/{sessionId}/events` | Get the event trace for a session. |
| `POST` | `/api/sessions/{sessionId}/messages` | Send a message to a session. |

### Endpoint Details

#### `GET /api`

Returns a simple success message that can be used to confirm the API is reachable.

Example response:

```json
"Hello from the SharpAgentHarness API!"
```

#### `POST /api/sessions`

Creates a new session. The request body is optional; omitted fields fall back to these defaults:

* `model`: `gpt-5-nano`
* `instructions`: `You are a helpful assistant.`
* `promptCacheKey`: `SharpAgentHarness`
* `tier`: `Auto`
* `reasoning`: `Medium`
* `toolkit`: `default`

Example request body:

```json
{
	"model": "gpt-5-nano",
	"instructions": "You are a helpful assistant.",
	"promptCacheKey": "SharpAgentHarness",
	"tier": "Auto",
	"reasoning": "Medium",
	"toolkit": "default"
}
```

Example response body:

```json
{
	"id": "8c6d4e4f-3f64-4dbe-a474-f0df2a87c1d2",
	"model": "gpt-5-nano",
	"tier": "Auto",
	"promptCacheKey": "SharpAgentHarness",
	"reasoning": "Medium",
	"previousResponseId": null,
	"instructions": "You are a helpful assistant.",
	"toolkitName": "default",
	"usageTotals": {
		"inputTokens": 0,
		"cachedInputTokens": 0,
		"outputTokens": 0,
		"reasoningOutputTokens": 0
	}
}
```

If the requested toolkit does not exist, the API returns `400 Bad Request`.

#### `GET /api/sessions/{sessionId}`

Returns the current session state for the supplied session ID using the same shape as the create-session response.
The response includes `usageTotals`, which are accumulated from all successful model responses in that session.

If the session does not exist, the API returns `404 Not Found`.

#### `GET /api/sessions/{sessionId}/events`

Returns the list of recorded session events currently held in memory for the supplied session ID.

If the session does not exist, the API returns `404 Not Found`.

#### `POST /api/sessions/{sessionId}/messages`

Sends a user message into an existing session.

Example request body:

```json
{
	"message": "What time is it?"
}
```

Example response body:

```json
{
	"response": "The current time is ..."
}
```

If the session does not exist, the API returns `404 Not Found`.

## Current Limitations

This project is intentionally narrow in scope:

- Sessions and events are stored in memory only.
- Streaming responses are not supported.
- Only OpenAI's Responses API is supported.
- Tool selection happens once per session rather than dynamically per turn.
- Tests are minimal and focused on core aspects of harness behaviour.
- `Agent` hosts the Web UI.

The project is designed as an experimental agent harness only, and is not suitable for production use.

## Possible Next Steps

Potential future explorations and improvements include:

- Experimental implementations of agentic concepts such as memory, subagents, skills and Recursive Language Models (RLMs).
- Persistent storage for sessions and events.
- Streaming response support.
- Support for Chat Completions-compatible APIs.
- API authentication and rate limiting.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

This is a personal experimental learning project provided "as is", without warranty of any kind.

## Contributions and Pull Requests

This repository is public for portfolio purposes only. Sorry, I'm not accepting contributions or pull requests at the moment.
