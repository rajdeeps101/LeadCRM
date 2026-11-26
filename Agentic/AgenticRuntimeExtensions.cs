public static class AgenticRuntimeExtensions
{
    public static IServiceCollection AddAgenticRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AgentRuntimeOptions>(configuration.GetSection("AgentRuntime"));
        services.Configure<PdfValidationOptions>(
            configuration.GetSection("AgentRuntime:PdfValidation"));
        services.Configure<GoogleGeminiPdfValidationOptions>(
            configuration.GetSection("AgentRuntime:PdfValidation:GoogleGemini"));
        services.PostConfigure<GoogleGeminiPdfValidationOptions>(options =>
            options.ApiKey = configuration["GOOGLE_GEMINI_API_KEY"] ?? options.ApiKey);
        services.AddHttpClient(GoogleGeminiFullNameVerificationAgent.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(45));
        services.AddSingleton<GoogleGeminiFullNameVerificationAgent>();
        services.AddSingleton<ICrmAgent, LeadReadinessAgent>();
        services.AddSingleton<ICrmAgent, DocumentReviewAssistant>();
        services.AddSingleton<PdfValidationAgent>();
        services.AddSingleton<AgentOrchestrator>();

        return services;
    }

    public static WebApplication MapAgenticRuntime(this WebApplication app)
    {
        app.MapGet("/api/agents", (AgentOrchestrator orchestrator) =>
            Results.Ok(new
            {
                provider = orchestrator.Provider,
                tasks = orchestrator.AvailableTasks,
                documentUploadAgents = new[] { orchestrator.PdfValidationAgentName }
            }));
        app.MapPost("/api/agents/run", RunAgentTask);

        return app;
    }

    private static async Task<IResult> RunAgentTask(
        AgentTaskRequest request,
        AgentOrchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(request.Task) ||
            !orchestrator.AvailableTasks.Any(
                task => task.Task.Equals(request.Task.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["task"] = [$"Choose one of: {string.Join(", ", orchestrator.AvailableTasks.Select(task => task.Task))}."]
            });
        }

        var result = await orchestrator.RunAsync(request);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
