using AIOrchestrator.ChatServer;

var builder = WebApplication.CreateBuilder(args);
string adminKey = builder.Configuration["Chat:AdminKey"]
    ?? throw new InvalidOperationException("Set Chat__AdminKey to a long, random secret before starting the chat server.");
if (adminKey.Length < 32)
    throw new InvalidOperationException("Chat__AdminKey must contain at least 32 characters.");

string dataPath = builder.Configuration["Chat:DataPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "chat.json");
builder.Services.AddSingleton(new ChatStore(Path.GetFullPath(dataPath), adminKey));

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/admin/access-codes", async (
    CreateAccessCodeRequest request,
    HttpRequest httpRequest,
    ChatStore store,
    CancellationToken ct) =>
{
    if (!store.IsAdminKeyValid(httpRequest.Headers["X-VinhAI-Admin-Key"].FirstOrDefault()))
        return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["displayName"] = ["Display name is required and must not exceed 100 characters."]
        });

    return Results.Ok(await store.CreateAccessCodeAsync(request.DisplayName, ct));
});

app.MapPost("/api/sessions", async (CreateSessionRequest request, ChatStore store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.AccessCode) || request.AccessCode.Length > 200)
        return Results.Unauthorized();
    UserSession? session = await store.CreateSessionAsync(request.AccessCode, ct);
    return session == null ? Results.Unauthorized() : Results.Ok(session);
});

app.MapGet("/api/messages", async (HttpRequest request, ChatStore store, CancellationToken ct) =>
{
    UserSession? session = GetSession(request, store);
    return session == null
        ? Results.Unauthorized()
        : Results.Ok(await store.GetUserMessagesAsync(session.ThreadId, ct));
});

app.MapPost("/api/messages", async (
    CreateMessageRequest request,
    HttpRequest httpRequest,
    ChatStore store,
    CancellationToken ct) =>
{
    UserSession? session = GetSession(httpRequest, store);
    if (session == null) return Results.Unauthorized();
    if (!IsValidMessage(request.Content))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["content"] = ["Message is required and must not exceed 8000 characters."]
        });

    return Results.Ok(await store.AddUserMessageAsync(session.ThreadId, request.Content, ct));
});

app.MapGet("/admin/conversations", async (
    bool? unreadOnly,
    HttpRequest request,
    ChatStore store,
    CancellationToken ct) =>
{
    if (!IsAdmin(request, store)) return Results.Unauthorized();
    return Results.Ok(await store.ListThreadsAsync(unreadOnly == true, ct));
});

app.MapGet("/admin/conversations/{threadId}/messages", async (
    string threadId,
    HttpRequest request,
    ChatStore store,
    CancellationToken ct) =>
{
    if (!IsAdmin(request, store)) return Results.Unauthorized();
    IReadOnlyList<ChatMessageRecord>? messages = await store.GetAdminMessagesAsync(threadId, ct);
    return messages == null ? Results.NotFound() : Results.Ok(messages);
});

app.MapPost("/admin/conversations/{threadId}/messages", async (
    string threadId,
    CreateMessageRequest request,
    HttpRequest httpRequest,
    ChatStore store,
    CancellationToken ct) =>
{
    if (!IsAdmin(httpRequest, store)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Trim().Length > 8000)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["content"] = ["Message is required and must not exceed 8000 characters."]
        });

    try
    {
        return Results.Ok(await store.AddAdminMessageAsync(
            threadId,
            string.IsNullOrWhiteSpace(request.SenderName) ? "Quản trị viên" : request.SenderName,
            request.Content,
            ct));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
});

app.Run();

static UserSession? GetSession(HttpRequest request, ChatStore store)
{
    string? authorization = request.Headers.Authorization.FirstOrDefault();
    if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;
    return store.FindSession(authorization["Bearer ".Length..].Trim());
}

static bool IsAdmin(HttpRequest request, ChatStore store) =>
    store.IsAdminKeyValid(request.Headers["X-VinhAI-Admin-Key"].FirstOrDefault());

static bool IsValidMessage(string? content) =>
    !string.IsNullOrWhiteSpace(content) && content.Trim().Length <= 8000;

public sealed record CreateAccessCodeRequest(string DisplayName);
public sealed record CreateSessionRequest(string AccessCode);
public sealed record CreateMessageRequest(string Content, string? SenderName = null);
