using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Llm
{
    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResponseStatus
    {
        Completed,
        Failed,
        Incomplete,
        InProgress,
        Queued,
        Cancelled
    }

    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResponseTextVerbosity
    {
        Low,
        Medium,
        High
    }

    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResponseTruncation
    {
        Auto,
        Disabled
    }

    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Phase
    {
        Commentary,
        FinalAnswer
    }

    /// <summary>
    /// Token usage summary
    /// </summary>
    public class ResponseUsage
    {
        /// <summary>
        /// Total number of input tokens
        /// </summary>
        public int? InputTokens { get; set; }

        /// <summary>
        /// Number of cached tokens from the input token details.
        /// </summary>
        public int? CachedInputTokens { get; set; }

        /// <summary>
        /// Total number of output tokens
        /// </summary>
        public int? OutputTokens { get; set; }

        /// <summary>
        /// Number of reasoning tokens from the output token details.
        /// </summary>
        public int? ReasoningOutputTokens { get; set; }
    }

    /// <summary>
    /// Base class for an annotation attached to generated output
    /// </summary>
    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseAnnotationFile), "file")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseAnnotationUrlCitation), "url_citation")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseAnnotationFilePath), "file_path")]
    public abstract class ResponseAnnotation { }

    /// <summary>
    /// A file citation annotation referencing a source file
    /// </summary>
    public class ResponseAnnotationFile : ResponseAnnotation
    {
        /// <summary>
        /// An identifier of the referenced file.
        /// </summary>
        public string? FileId { get; set; }

        /// <summary>
        /// A file name shown in the annotation.
        /// </summary>
        public string? Filename { get; set; }

        /// <summary>
        /// A zero-based annotation index within the content.
        /// </summary>
        public int? Index { get; set; }
    }

    /// <summary>
    /// A URL citation annotation referencing an external URL
    /// </summary>
    public class ResponseAnnotationUrlCitation : ResponseAnnotation
    {
        /// <summary>
        /// A start position of the annotated span.
        /// </summary>
        public int? StartIndex { get; set; }

        /// <summary>
        /// An end position of the annotated span.
        /// </summary>
        public int? EndIndex { get; set; }

        /// <summary>
        /// An annotation title.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// A URL associated with the annotation.
        /// </summary>
        public string? Url { get; set; }
    }

    /// <summary>
    /// A file path annotation referencing a file by its identifier
    /// </summary>
    public class ResponseAnnotationFilePath : ResponseAnnotation
    {
        /// <summary>
        /// An identifier of the referenced file.
        /// </summary>
        public string? FileId { get; set; }

        /// <summary>
        /// A zero-based annotation index within the content.
        /// </summary>
        public int? Index { get; set; }
    }

    /// <summary>
    /// Base class for a single content part within an output item
    /// </summary>
    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseContentPartText), "text")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseContentPartRefusal), "refusal")]
    public abstract class ResponseContentPart { }

    /// <summary>
    /// A text content part containing the model's generated output
    /// </summary>
    public class ResponseContentPartText : ResponseContentPart
    {
        /// <summary>
        /// The plain text content
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Annotations attached to this content part
        /// </summary>
        public List<ResponseAnnotation> Annotations { get; set; } = new();
    }

    /// <summary>
    /// A refusal content part returned when the model declines to answer
    /// </summary>
    public class ResponseContentPartRefusal : ResponseContentPart
    {
        /// <summary>
        /// The refusal message from the model
        /// </summary>
        public string? Refusal { get; set; }
    }

    /// <summary>
    /// A summary of the reasoning so far
    /// </summary>
    public class ResponseReasoningSummaryPart
    {
        /// <summary>
        /// A summary text.
        /// </summary>
        public string? Text { get; set; }
    }

    /// <summary>
    /// Base class for all output items in the response
    /// </summary>
    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseOutputItemMessage), "message")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseOutputItemFunctionCall), "function_call")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ResponseOutputItemReasoning), "reasoning")]
    public abstract class ResponseOutputItem
    {
        /// <summary>
        /// An output item identifier
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Processing status for the item
        /// </summary>
        public ResponseStatus? Status { get; set; }
    }

    /// <summary>
    /// A message output item containing text or refusal content parts
    /// </summary>
    public class ResponseOutputItemMessage : ResponseOutputItem
    {
        /// <summary>
        /// Execution phase for the message
        /// </summary>
        public Phase? Phase { get; set; }

        /// <summary>
        /// The content parts that make up the message
        /// </summary>
        public List<ResponseContentPart> Content { get; set; } = new();
    }

    /// <summary>
    /// A function call output item representing a tool invocation requested by the model
    /// </summary>
    public class ResponseOutputItemFunctionCall : ResponseOutputItem
    {
        /// <summary>
        /// A tool call identifier used to correlate this call with its result
        /// </summary>
        public string? CallId { get; set; }

        /// <summary>
        /// The name of the function to call
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// A raw JSON argument payload for the function
        /// </summary>
        public string? Arguments { get; set; }
    }

    /// <summary>
    /// A reasoning output item containing the model's chain-of-thought summary
    /// </summary>
    public class ResponseOutputItemReasoning : ResponseOutputItem
    {
        /// <summary>
        /// Reasoning summary fragments produced by the model
        /// </summary>
        public List<ResponseReasoningSummaryPart> Summary { get; set; } = new();
    }

    /// <summary>
    /// Base class for all LLM responses.
    /// Cast to <see cref="ErrorResponse"/> or <see cref="SuccessResponse"/> to access the full result.
    /// </summary>
    [System.Text.Json.Serialization.JsonPolymorphic]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(ErrorResponse))]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(SuccessResponse))]
    public abstract class Response
    {
        protected Response(string rawBody)
        {
            RawBody = rawBody;
        }

        /// <summary>
        /// Original unparsed JSON response body
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string RawBody { get; set; }

        // Serialiser options shared across all unparse calls: indented output with enum names as strings
        private static readonly JsonSerializerOptions UnparseOptions = new()
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Returns a pretty-printed JSON-style representation of the response,
        /// with all enum values rendered as their named strings rather than integers.
        /// </summary>
        public override string ToString() =>
            JsonSerializer.Serialize(this, UnparseOptions);
    }

    /// <summary>
    /// A failed LLM response
    /// </summary>
    public class ErrorResponse : Response
    {
        public ErrorResponse(string rawBody) : base(rawBody) { }

        /// <summary>
        /// Human-readable error message
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Provider-specific error type
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Request parameter associated with the error
        /// </summary>
        public string? Param { get; set; }

        /// <summary>
        /// Machine-readable error code
        /// </summary>
        public string? Code { get; set; }
    }

    /// <summary>
    /// A successful LLM response
    /// </summary>
    public class SuccessResponse : Response
    {
        public SuccessResponse(string rawBody) : base(rawBody) { }

        /// <summary>
        /// Response identifier
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Response creation time as Unix seconds
        /// </summary>
        public long? CreatedAtUnixSeconds { get; set; }

        /// <summary>
        /// Response creation time converted to UTC
        /// </summary>
        public DateTime? Created => CreatedAtUnixSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnixSeconds.Value).UtcDateTime
            : null;

        /// <summary>
        /// Model that produced the response
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// The reason an incomplete response did not finish
        /// </summary>
        public string? IncompleteDetails { get; set; }

        /// <summary>
        /// The structured output items returned by the LLM
        /// </summary>
        public List<ResponseOutputItem> Output { get; set; } = new();

        /// <summary>
        /// Completion time as Unix seconds
        /// </summary>
        public long? CompletedAtUnixSeconds { get; set; }

        /// <summary>
        /// Completion time converted to UTC
        /// </summary>
        public DateTime? CompletedAt => CompletedAtUnixSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(CompletedAtUnixSeconds.Value).UtcDateTime
            : null;

        /// <summary>
        /// Overall response status
        /// </summary>
        public ResponseStatus? Status { get; set; }

        /// <summary>
        /// Truncation policy applied to the response
        /// </summary>
        public ResponseTruncation? Truncation { get; set; }

        /// <summary>
        /// Token usage for the response.
        /// </summary>
        public ResponseUsage? Usage { get; set; }
    }

    #region JSON Helpers

    /// <summary>
    /// Parses raw JSON into an <see cref="Response"/>
    /// </summary>
    internal static class LlmResponseJsonExtractor
    {
        /// <summary>
        /// Parses a raw JSON body and returns either an <see cref="ErrorResponse"/>
        /// or an <see cref="SuccessResponse"/> depending on the content.
        /// </summary>
        public static Response ParseResponse(string body)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            // If an error property is present, return an error response
            if (TryGetProperty(root, "error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.Object)
            {
                return new ErrorResponse(body)
                {
                    Message = GetString(errorElement, "message"),
                    Type = GetString(errorElement, "type"),
                    Param = GetString(errorElement, "param"),
                    Code = GetString(errorElement, "code")
                };
            }

            return new SuccessResponse(body)
            {
                Id = GetString(root, "id"),
                CreatedAtUnixSeconds = GetInt64(root, "created_at"),
                Model = GetString(root, "model"),
                IncompleteDetails = GetObject(root, "incomplete_details", ParseIncompleteDetails),
                Output = GetArray(root, "output", ParseOutputItem),
                CompletedAtUnixSeconds = GetInt64(root, "completed_at"),
                Status = GetEnum<ResponseStatus>(root, "status"),
                Truncation = GetEnum<ResponseTruncation>(root, "truncation"),
                Usage = GetObject(root, "usage", ParseUsage)
            };
        }

        /// <summary>
        /// Parses incomplete response details from a JSON element
        /// </summary>
        private static string? ParseIncompleteDetails(JsonElement element)
        {
            return GetString(element, "reason");
        }

        /// <summary>
        /// Parses usage statistics from a JSON element
        /// </summary>
        private static ResponseUsage ParseUsage(JsonElement element)
        {
            return new ResponseUsage
            {
                InputTokens = GetInt32(element, "input_tokens"),
                CachedInputTokens = GetNestedInt32(element, "input_tokens_details", "cached_tokens"),
                OutputTokens = GetInt32(element, "output_tokens"),
                ReasoningOutputTokens = GetNestedInt32(element, "output_tokens_details", "reasoning_tokens")            };
        }

        /// <summary>
        /// Parses one output item from a JSON element, returning the appropriate subclass
        /// based on the "type" field in the JSON
        /// </summary>
        private static ResponseOutputItem ParseOutputItem(JsonElement element)
        {
            string? type = GetString(element, "type");
            string id = GetString(element, "id") ?? string.Empty;
            ResponseStatus? status = GetEnum<ResponseStatus>(element, "status");

            if (string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase))
            {
                return new ResponseOutputItemFunctionCall
                {
                    Id = id,
                    Status = status,
                    CallId = GetString(element, "call_id"),
                    Name = GetString(element, "name"),
                    Arguments = GetString(element, "arguments")
                };
            }

            if (string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
            {
                return new ResponseOutputItemReasoning
                {
                    Id = id,
                    Status = status,
                    Summary = GetArray(element, "summary", ParseSummaryPart)
                };
            }

            // Default to message for "message" type and any unknown types
            return new ResponseOutputItemMessage
            {
                Id = id,
                Status = status,
                Phase = GetEnum<Phase>(element, "phase"),
                Content = GetArray(element, "content", ParseContentPart)
            };
        }

        /// <summary>
        /// Parses one content part from a JSON element, returning the appropriate subclass
        /// based on the "type" field in the JSON
        /// </summary>
        private static ResponseContentPart ParseContentPart(JsonElement element)
        {
            string? type = GetString(element, "type");

            if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
            {
                return new ResponseContentPartRefusal
                {
                    Refusal = GetString(element, "refusal")
                };
            }

            // Default to text for "output_text" type and any unknown types
            return new ResponseContentPartText
            {
                Text = GetString(element, "text"),
                Annotations = GetArray(element, "annotations", ParseAnnotation)
            };
        }

        /// <summary>
        /// Parses an annotation from a JSON element, returning the appropriate subclass
        /// based on the "type" field in the JSON
        /// </summary>
        private static ResponseAnnotation ParseAnnotation(JsonElement element)
        {
            string? type = GetString(element, "type");

            if (string.Equals(type, "url_citation", StringComparison.OrdinalIgnoreCase))
            {
                return new ResponseAnnotationUrlCitation
                {
                    StartIndex = GetInt32(element, "start_index"),
                    EndIndex = GetInt32(element, "end_index"),
                    Title = GetString(element, "title"),
                    Url = GetString(element, "url")
                };
            }

            if (string.Equals(type, "file_path", StringComparison.OrdinalIgnoreCase))
            {
                return new ResponseAnnotationFilePath
                {
                    FileId = GetString(element, "file_id"),
                    Index = GetInt32(element, "index")
                };
            }

            // Default to file citation for "file_citation" type and any unknown types
            return new ResponseAnnotationFile
            {
                FileId = GetString(element, "file_id"),
                Filename = GetString(element, "filename"),
                Index = GetInt32(element, "index")
            };
        }

        /// <summary>
        /// Parses a summary fragment from a JSON element
        /// </summary>
        private static ResponseReasoningSummaryPart ParseSummaryPart(JsonElement element)
        {
            return new ResponseReasoningSummaryPart
            {
                Text = GetString(element, "text")
            };
        }

        /// <summary>
        /// Reads a property as a string, converting non-string JSON values with <c>ToString()</c>
        /// </summary>
        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        /// <summary>
        /// Reads a property as an enumeration value using tolerant name matching
        /// </summary>
        /// <typeparam name="TEnum">The enumeration type</typeparam>
        /// <param name="element">The JSON object to inspect</param>
        /// <param name="propertyName">The property name to read</param>
        /// <returns>The parsed enumeration value, or <see langword="null"/> when absent or unknown</returns>
        private static TEnum? GetEnum<TEnum>(JsonElement element, string propertyName)
            where TEnum : struct, Enum
        {
            string? value = GetString(element, propertyName);

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string candidate = CanonicaliseEnumValue(value);

            foreach (TEnum enumValue in Enum.GetValues<TEnum>())
            {
                if (string.Equals(CanonicaliseEnumValue(enumValue.ToString()), candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return enumValue;
                }
            }

            return null;
        }

        /// <summary>
        /// Normalises provider values for loose enumeration matching
        /// </summary>
        private static string CanonicaliseEnumValue(string value)
        {
            return new string(value
                .Where(character => character != '_' && character != '-' && !char.IsWhiteSpace(character))
                .ToArray());
        }

        /// <summary>
        /// Reads a property as a 32-bit integer
        /// </summary>
        private static int? GetInt32(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.TryGetInt32(out int parsed) ? parsed : null;
        }

        /// <summary>
        /// Reads a nested object property as a 32-bit integer
        /// </summary>
        private static int? GetNestedInt32(JsonElement element, string objectPropertyName, string valuePropertyName)
        {
            if (!TryGetProperty(element, objectPropertyName, out JsonElement nestedObject)
                || nestedObject.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return GetInt32(nestedObject, valuePropertyName);
        }

        /// <summary>
        /// Reads a property as a 64-bit integer.
        /// </summary>
        private static long? GetInt64(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.TryGetInt64(out long parsed) ? parsed : null;
        }

        /// <summary>
        /// Reads an object property and parses it into a strongly typed model
        /// </summary>
        /// <typeparam name="T">The target model type</typeparam>
        /// <param name="element">The JSON object to inspect</param>
        /// <param name="propertyName">The property name to read</param>
        /// <param name="parser">The parser used to create the target model</param>
        /// <returns>The parsed object, or <see langword="null"/> when the property is missing or not an object</returns>
        private static T? GetObject<T>(JsonElement element, string propertyName, Func<JsonElement, T> parser)
            where T : class?
        {
            if (!TryGetProperty(element, propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return parser(value);
        }

        /// <summary>
        /// Reads an array property and parses each element with the supplied parser
        /// </summary>
        /// <typeparam name="T">The item type produced by the parser</typeparam>
        /// <param name="element">The JSON object to inspect</param>
        /// <param name="propertyName">The property name to read</param>
        /// <param name="parser">The parser used to convert each array item</param>
        /// <returns>A list of parsed items, or an empty list when the property is missing or not an array</returns>
        private static List<T> GetArray<T>(JsonElement element, string propertyName, Func<JsonElement, T> parser)
        {
            if (!TryGetProperty(element, propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                return new List<T>();
            }

            List<T> items = new();

            foreach (JsonElement item in value.EnumerateArray())
            {
                items.Add(parser(item));
            }

            return items;
        }

        /// <summary>
        /// Attempts to read a named property from a JSON object
        /// </summary>
        /// <param name="element">The JSON element to inspect</param>
        /// <param name="propertyName">The property name to look up</param>
        /// <param name="value">When this method returns, contains the matching property value if found</param>
        /// <returns><see langword="true"/> when the property exists on an object; otherwise <see langword="false"/></returns>
        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
            {
                return true;
            }

            value = default;
            return false;
        }
    }

    #endregion
}
