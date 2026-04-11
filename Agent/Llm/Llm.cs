using System.Text;

namespace Agent.Llm
{
    public class LlmClient
    {
        private static readonly HttpClient Http = new HttpClient();

        private const string OpenAiResponsesUrl = "https://api.openai.com/v1/responses";

        public Task<Response> SendMessageAsync(Session session, Request req)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            return SendMessageAsync(session, req, CancellationToken.None);
        }

        public virtual async Task<Response> SendMessageAsync(Session session, Request req, CancellationToken cancellationToken)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            return await SendMessageCoreAsync(session, req, cancellationToken);
        }

        private async Task<Response> SendMessageCoreAsync(Session? session, Request req, CancellationToken cancellationToken)
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, OpenAiResponsesUrl);

            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("Environment variable 'OPENAI_API_KEY' is not set.");
            httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            string reqBody = req.ToOpenAiResponsesBody();
            if (session is not null)
            {
                EventTraces.Publish(new LlmRawRequestSent(session, reqBody));
            }

            httpReq.Content = new StringContent(reqBody, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(httpReq, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            Response parsed = LlmResponseJsonExtractor.ParseResponse(responseBody);
            if (!response.IsSuccessStatusCode && parsed is not ErrorResponse)
            {
                return new ErrorResponse(responseBody)
                {
                    Message = $"The OpenAI Responses request failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
                    Type = "http_error",
                    Code = response.StatusCode.ToString()
                };
            }

            return parsed;
        }
    }
}
