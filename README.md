# Hydra.WebApi

**Hydra.WebApi** is a core component of the **HydraFramework**, specifically designed to provide reusable infrastructure for Web API projects. It is built as a **Class Library** containing shared Controllers, Middleware, and Extensions, streamlining the development of consistent and robust Web APIs.

## Overview
`Hydra.WebApi` is not a standalone application. It is defined as a Class Library to be referenced by specific implementation projects (e.g., `Tentacle.WebApi`, `Phoenix.WebApi`). It hosts:
*   **Base Controllers**: Generic `MainController<T>` for standard CRUD operations.
*   **System Controllers**: `SystemUserController` for authentication and management.
*   **Middleware**: `SessionMiddleware` for specialized session handling.
*   **Extensions**: Dependency injection helpers like `AddHydraDependencies`.

## Integration Guide

### Prerequisites
*   Your project must be an **ASP.NET Core Web API** project.
*   Reference the `Hydra.WebApi` project in your solution.

### 1. Add Project Reference
Add the reference to your project's `.csproj` file:
```xml
<ProjectReference Include="..\..\Hydra.WebApi\Hydra.WebApi.csproj" />
```

### 2. Configure Program.cs
You must explicitly register `Hydra.WebApi`'s components in your `Program.cs`.

#### A. Register Controllers (ApplicationPart)
Use `AddApplicationPart` to expose the shared controllers:

```csharp
// Register Controllers from Hydra.WebApi
builder.Services.AddControllers()
    .AddApplicationPart(typeof(MainController<>).Assembly);
```

#### B. Register Dependencies
Ensure Hydra dependencies are registered. Typically, your project will have its own `Add[Project]Dependencies` extension (e.g., `AddTentacleDependencies`), but it must internally call:

```csharp
builder.Services.AddHydraDependencies(builder.Configuration);
```

#### C. Register Middleware
Add the `SessionMiddleware` to the pipeline to handle Hydra-specific session logic:

```csharp
app.UseMiddleware<SessionMiddleware>();
```

### Example Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers & Application Part
builder.Services.AddControllers()
    .AddApplicationPart(typeof(MainController<>).Assembly);

// 2. Add Dependencies
builder.Services.AddHydraDependencies(builder.Configuration);

var app = builder.Build();

// 3. Use Middleware
app.UseMiddleware<SessionMiddleware>();

app.MapControllers();
app.Run();
```
