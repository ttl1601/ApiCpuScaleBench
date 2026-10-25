using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 1_048_576);
var app = builder.Build();
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
app.MapGet("/health", () => Results.Text("ok"));
app.MapPost("/parse", async (HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body, new UTF8Encoding(false, true), false, 4096, true);
        var text = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        var document = JsonSerializer.Deserialize<Payload>(text, jsonOptions);
        if (document is null || document.Name is null || document.Text is null || document.Tags is null ||
            document.Tags.Any(tag => tag is null) || document.Items is null || document.Items.Any(item => item is null) ||
            document.Attributes is null || document.Attributes.Values.Any(value => value is null))
            return Results.BadRequest();
        return Results.Json(document, jsonOptions);
    }
    catch (JsonException) { return Results.BadRequest(); }
    catch (DecoderFallbackException) { return Results.BadRequest(); }
    catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { return Results.StatusCode(413); }
});
Console.WriteLine("RUNTIME " + JsonSerializer.Serialize(new
{
    Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ProcessorCount = Environment.ProcessorCount,
    IsServerGC = System.Runtime.GCSettings.IsServerGC,
    TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
}));
app.Run();

internal sealed class Payload
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required bool Active { get; init; }
    public required double Score { get; init; }
    public required string Text { get; init; }
    public required string[] Tags { get; init; }
    public required Item[] Items { get; init; }
    public required Dictionary<string, string> Attributes { get; init; }
}

internal sealed class Item
{
    public required int Id { get; init; }
    public required double Price { get; init; }
    public required int Quantity { get; init; }
}
