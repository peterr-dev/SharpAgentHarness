namespace Core.ChatCompletions.Models
{
    public static class ModelRegistry
    {
        private static readonly IChatModel OpenAi = new OpenAiModel();
        private static readonly IChatModel GptOss = new GptOssModel();
        private static readonly IChatModel Qwen36 = new Qwen36Model();

        public static IChatModel Get(RequestModel model)
        {
            return model switch
            {
                RequestModel.OpenAi => OpenAi,
                RequestModel.GptOss => GptOss,
                RequestModel.Qwen36 => Qwen36,
                _ => throw new InvalidOperationException($"Unsupported model: {model}")
            };
        }
    }
}
