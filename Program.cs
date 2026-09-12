using RagApi.Cache;
using RagApi.Services;
using RagApi.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers + Swagger ──────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "RAG API",
        Version     = "v1",
        Description = "Azure OpenAI + Azure AI Search RAG pipeline.\n\n" +
                      "**Flow:** POST /api/chat/ask → embed question → vector search → " +
                      "score >= threshold → GPT-4o (RAG) | score < threshold → GPT-4o-mini (fallback).\n\n" +
                      "**Ingestion:** POST /api/ingestion/upload → Document Intelligence → chunk → embed → index.\n\n" +
                      "**Tip:** Use GET /api/search/debug to inspect chunk scores and tune scoreThreshold."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);

    c.OrderActionsBy(api => api.RelativePath);
});

// ── IMemoryCache (free, no Redis needed) ──────────────────────────────────────
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100 * 1024 * 1024; // 100MB cap
});
builder.Services.AddSingleton<CacheService>();

// ── Phase 1: Ingestion services ────────────────────────────────────────────────
builder.Services.AddSingleton<IndexInitializer>();
builder.Services.AddSingleton<IDocumentExtractor, DocumentIntelligenceExtractor>();
builder.Services.AddSingleton<IChunkingService, ChunkingService>();
builder.Services.AddScoped<IIngestionService, IngestionService>();

// ── Phase 2: Query services ────────────────────────────────────────────────────
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<IVectorSearchService, VectorSearchService>();
builder.Services.AddScoped<IRagService, RagService>();

// ── CORS ───────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ── Logging ────────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.ConfigureKestrel(o => o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5));
var app = builder.Build();

// ── Ensure AI Search index exists on startup ───────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IndexInitializer>();
    await initializer.EnsureIndexExistsAsync();
}

// ── Swagger — all environments ─────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "RAG API v1");
    c.RoutePrefix   = "swagger";
    c.DocumentTitle = "RAG API — Swagger UI";
    c.DefaultModelsExpandDepth(2);
    c.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Example);
    c.DisplayRequestDuration();
    c.EnableDeepLinking();
    c.EnableFilter();
    c.EnableTryItOutByDefault();
});

// Redirect root → Swagger
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.UseCors();
app.UseAuthorization();

app.MapControllers();

app.Run();
