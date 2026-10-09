using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Inventory.Api.Infrastructure;

/// <summary>Adds the bearer-token scheme so Swagger UI shows an "Authorize" button.</summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "HelloCherie Warehousing API";
        document.Info.Description = "Inventory and dispatch management. All endpoints except /auth/login, /auth/refresh and /auth/logout require a bearer token.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from POST /api/v1/auth/login",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
