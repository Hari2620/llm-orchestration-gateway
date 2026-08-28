using System.Text.Json;

namespace Gateway.Services;

/// <summary>
/// System.Net.Http.Json's PostAsJsonAsync/ReadFromJsonAsync default to
/// PascalCase with no naming policy, but the FastAPI sidecar (and ASP.NET
/// Core's own minimal-API endpoints, for that matter) speak camelCase.
/// Every call across the gateway-to-sidecar boundary uses this explicitly —
/// forgetting it is a silent 422 on the Python side, not a compile error,
/// which is exactly the kind of bug worth naming in code.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);
}
