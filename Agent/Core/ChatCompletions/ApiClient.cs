using System.Text;

namespace Core.ChatCompletions
{
    public class ApiClient
    {
        private readonly HttpClient _httpClient;

        public ApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<Response> SendMessageAsync(Session session, Request request, Uri chatCompletionsUri, CancellationToken cancellationToken)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            using HttpRequestMessage httpReq = new HttpRequestMessage(HttpMethod.Post, chatCompletionsUri);

            string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(apiKey))
                httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            string reqBody = request.ToJson();
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
                throw new InvalidOperationException($"Chat Completions API returned an error: {response.StatusCode}, body: {responseBody}");
            }
        }
    }
}
