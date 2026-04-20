using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Text;

namespace Tests;

/// <summary>
/// Tiny in-process fake server that returns deterministic chat completions JSON.
/// </summary>
public sealed class FakeApiClientServer : IAsyncDisposable
{
    private const string DefaultSuccessResponseBody = """
    {
      "id": "chatcmpl_test_123",
      "object": "chat.completion",
      "created": 1710000000,
      "model": "gpt-5-nano",
      "choices": [
        {
          "index": 0,
          "finish_reason": "stop",
          "message": {
            "role": "assistant",
            "content": "Hello from fake local server.",
            "refusal": ""
          }
        }
      ],
      "usage": {
        "prompt_tokens": 12,
        "completion_tokens": 7,
        "total_tokens": 19,
        "prompt_tokens_details": {
          "cached_tokens": 5
        },
        "completion_tokens_details": {
          "reasoning_tokens": 3
        }
      }
    }
    """;

    private readonly WebApplication _app;

    private FakeApiClientServer(WebApplication app, HttpClient client, Uri chatCompletionsUri)
    {
        _app = app;
        Client = client;
        ChatCompletionsUri = chatCompletionsUri;
    }

    public HttpClient Client { get; }

    public Uri ChatCompletionsUri { get; }

    public static async Task<FakeApiClientServer> StartAsync(
        IReadOnlyDictionary<string, string>? responseBodiesByRequestBody = null,
        CancellationToken cancellationToken = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        // Use TestServer so calls stay in-process and do not require loopback networking.
        builder.WebHost.UseTestServer();

        WebApplication app = builder.Build();

        app.MapPost("/v1/chat/completions", async context =>
        {
            using StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            string requestBody = await reader.ReadToEndAsync();

            string? responseBody = null;
            if (responseBodiesByRequestBody is not null)
            {
                responseBodiesByRequestBody.TryGetValue(requestBody, out responseBody);
            }

            if (responseBodiesByRequestBody is not null && responseBody is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"error":"Unexpected request body."}""");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody ?? DefaultSuccessResponseBody);
        });

        await app.StartAsync(cancellationToken);

        HttpClient client = app.GetTestClient();
        Uri chatCompletionsUri = new Uri("http://localhost/v1/chat/completions");

        return new FakeApiClientServer(app, client, chatCompletionsUri);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
