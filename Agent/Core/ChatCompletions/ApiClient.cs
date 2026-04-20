using System.Text;

namespace Core.ChatCompletions
{
    public class ApiClient
    {
        private static readonly HttpClient defaultHttpClient = new HttpClient();
        private readonly HttpClient _httpClient;

        public ApiClient() : this(defaultHttpClient)
        {
        }

        public ApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<Response> SendMessageAsync(Session session, Request req, CancellationToken cancellationToken)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));

            using HttpRequestMessage httpReq = new HttpRequestMessage(HttpMethod.Post, session.ChatCompletionsUrl);

            // Assume unit testing if there is no API key in the environment
            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "test-api-key";

            httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            string reqBody = req.ToJson();
            httpReq.Content = new StringContent(reqBody, Encoding.UTF8, "application/json");
            HookRegistry.RunRawRequestReadyHooks(session, reqBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(httpReq, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            HookRegistry.RunRawResponseReceivedHooks(session, responseBody);

            if (response.IsSuccessStatusCode)
            {
                return Response.Parse(responseBody);
            }
            else
            {
                throw new InvalidOperationException($"Chat Completions API returned error: {response.StatusCode}, body: {responseBody}");
            }
        }
    }
}
