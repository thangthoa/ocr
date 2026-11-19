
# CLAUDE.md

# ASP.NET Core Modular Monolith - Project Guidelines

## 1. Project Overview

You are working on a production-oriented ASP.NET Core full-stack application.

The project must follow these architectural principles:

- Hexagonal Architecture (Ports and Adapters).
- Modular Monolith.
- Domain-Driven Design (DDD).
- CQRS (Command Query Responsibility Segregation).
- Clean dependency boundaries.
- Separation of concerns.
- Testability and maintainability.
- Security by design.

### Technology Stack

| Component | Technology |
|---|---|
| Framework | .NET 10 LTS |
| Language | C# |
| UI Framework | Blazor Web App |
| Rendering | Interactive Server |
| Database | PostgreSQL |
| ORM | Entity Framework Core |
| PostgreSQL Provider | Npgsql |
| Validation | FluentValidation |
| Authentication | ASP.NET Core Identity |
| Logging | Serilog |
| Unit Testing | xUnit |
| Component Testing | bUnit |
| Integration Testing | Testcontainers |
| Containerization | Docker |

Do not introduce additional technologies without a clear technical justification.

The application must remain a Modular Monolith. Do not split it into microservices unless explicitly requested.

---

# 2. Fundamental Architecture Rules

These rules are mandatory.

## 2.1 Dependency Rule

Dependencies must always point inward.

Allowed dependencies:

- Domain must not depend on Application, Infrastructure, Web, or external frameworks.
- Application may depend on Domain.
- Infrastructure may depend on Application and Domain.
- Web may depend on Application and Infrastructure.
- Domain and Application must never depend on Infrastructure.

The dependency direction must remain consistent throughout the project.

### Forbidden Dependencies

Domain must NOT reference:

- Entity Framework Core.
- ASP.NET Core.
- Npgsql.
- Redis.
- HTTP clients.
- External SDKs.
- Infrastructure implementations.

Application must NOT reference:

- Infrastructure.
- DbContext.
- Entity Framework Core.
- PostgreSQL implementation details.
- Blazor components.
- ASP.NET Core presentation components.

Blazor components must NOT:

- Access DbContext directly.
- Execute SQL queries.
- Access Infrastructure repositories directly.
- Contain business logic.
- Implement business rules.

---

# 3. Solution Architecture

Use separate .NET projects to establish clear architectural boundaries.

Recommended solution structure:

```text
MyApp/
│
├── MyApp.sln
├── CLAUDE.md
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
│
├── src/
│   │
│   ├── Hosts/
│   │   └── MyApp.Web/
│   │       ├── Components/
│   │       │   ├── Layout/
│   │       │   ├── Pages/
│   │       │   └── Shared/
│   │       │
│   │       ├── Features/
│   │       │   ├── Users/
│   │       │   ├── Catalog/
│   │       │   └── Orders/
│   │       │
│   │       ├── Authentication/
│   │       ├── Authorization/
│   │       ├── Middleware/
│   │       ├── Extensions/
│   │       ├── wwwroot/
│   │       ├── Program.cs
│   │       └── appsettings.json
│   │
│   ├── BuildingBlocks/
│   │   ├── MyApp.BuildingBlocks.Domain/
│   │   ├── MyApp.BuildingBlocks.Application/
│   │   └── MyApp.BuildingBlocks.Infrastructure/
│   │
│   └── Modules/
│       │
│       ├── Identity/
│       │   ├── MyApp.Modules.Identity.Domain/
│       │   ├── MyApp.Modules.Identity.Application/
│       │   └── MyApp.Modules.Identity.Infrastructure/
│       │
│       ├── Users/
│       │   ├── MyApp.Modules.Users.Domain/
│       │   ├── MyApp.Modules.Users.Application/
│       │   └── MyApp.Modules.Users.Infrastructure/
│       │
│       ├── Catalog/
│       │   ├── MyApp.Modules.Catalog.Domain/
│       │   ├── MyApp.Modules.Catalog.Application/
│       │   └── MyApp.Modules.Catalog.Infrastructure/
│       │
│       └── Orders/
│           ├── MyApp.Modules.Orders.Domain/
│           ├── MyApp.Modules.Orders.Application/
│           └── MyApp.Modules.Orders.Infrastructure/
│
├── tests/
│   ├── MyApp.ArchitectureTests/
│   ├── MyApp.BuildingBlocks.UnitTests/
│   │
│   ├── Modules/
│   │   ├── Identity/
│   │   ├── Users/
│   │   ├── Catalog/
│   │   └── Orders/
│   │
│   └── MyApp.Web.FunctionalTests/
│
├── docs/
│   ├── architecture/
│   ├── decisions/
│   ├── development/
│   └── deployment/
│
├── deploy/
│   ├── docker/
│   └── scripts/
│
└── docker-compose.yml
```

Important:

This structure is a reference architecture.

Do not create unnecessary modules just to match the example.

Only create modules required by actual business requirements.

---

# 4. Modular Monolith Rules

Each business module must own its:

- Domain model.
- Application use cases.
- Persistence implementation.
- Database mappings.
- Database schema.
- Module-specific business rules.
- Public contracts.

Example:

```text
Orders Module
    |
    ├── Domain
    ├── Application
    └── Infrastructure
```

Modules must remain logically independent.

## 4.1 Module Isolation

A module must never directly access another module's:

- DbContext.
- Repository implementation.
- Infrastructure services.
- Internal domain entities.
- Internal application handlers.

Cross-module communication must use:

1. Explicit public contracts.
2. Application-level ports.
3. Integration events when asynchronous communication is appropriate.

Never use another module's Infrastructure project as a dependency.

## 4.2 Module Registration

Each module must expose explicit dependency registration methods.

Example:

```csharp
services.AddOrdersApplication();

services.AddOrdersInfrastructure(configuration);
```

The Web host is responsible for registering all modules.

Avoid reflection-based automatic registration unless there is a concrete requirement.

---

# 5. Domain Layer

The Domain Layer contains the core business model.

It must remain independent of technical infrastructure.

Recommended structure:

```text
Domain/
│
├── Common/
│   ├── Entity.cs
│   ├── AggregateRoot.cs
│   ├── ValueObject.cs
│   ├── IDomainEvent.cs
│   └── DomainException.cs
│
├── Entities/
├── Aggregates/
├── ValueObjects/
├── Enums/
├── Events/
├── Services/
└── Exceptions/
```

## 5.1 Domain Responsibilities

Domain owns:

- Business invariants.
- Business state transitions.
- Entity behavior.
- Value object validation.
- Aggregate consistency.
- Domain events.
- Domain services.

Business rules must be implemented inside the Domain Layer whenever they represent domain behavior.

## 5.2 Entity Rules

Entities must:

- Have identity.
- Protect their state.
- Expose meaningful business methods.
- Enforce their invariants.
- Avoid public setters for important business state.

Avoid anemic entities that contain only properties.

Bad:

```csharp
public class Order
{
    public Guid Id { get; set; }

    public decimal Total { get; set; }

    public string Status { get; set; }
}
```

Preferred:

```csharp
public sealed class Order
{
    public Guid Id { get; private set; }

    public OrderStatus Status { get; private set; }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException(
                "Only pending orders can be confirmed.");
        }

        Status = OrderStatus.Confirmed;
    }
}
```

The example is illustrative. Implement actual constructors, invariants, and persistence configuration according to business requirements.

## 5.3 Value Objects

Use Value Objects for concepts defined by their values rather than identity.

Examples:

- Money.
- EmailAddress.
- PhoneNumber.
- Address.
- DateRange.

Value Objects should preferably be immutable.

## 5.4 Aggregate Rules

Aggregates must:

- Protect business consistency.
- Enforce invariants.
- Control state changes.
- Define clear transaction boundaries.

Keep aggregates small.

Do not load or modify large object graphs unnecessarily.

---

# 6. Application Layer

The Application Layer orchestrates business use cases.

It must not contain database implementation details.

Recommended structure:

```text
Application/
│
├── Common/
│   ├── Interfaces/
│   ├── Behaviors/
│   ├── Exceptions/
│   ├── Models/
│   └── Results/
│
├── Features/
│   │
│   ├── CreateOrder/
│   │   ├── CreateOrderCommand.cs
│   │   ├── CreateOrderHandler.cs
│   │   └── CreateOrderValidator.cs
│   │
│   ├── CancelOrder/
│   │   ├── CancelOrderCommand.cs
│   │   ├── CancelOrderHandler.cs
│   │   └── CancelOrderValidator.cs
│   │
│   └── GetOrderDetails/
│       ├── GetOrderDetailsQuery.cs
│       └── GetOrderDetailsHandler.cs
│
├── Ports/
├── Contracts/
└── DependencyInjection.cs
```

## 6.1 Use Case Rules

Every business operation must be represented by an explicit use case.

Examples:

- CreateUser.
- UpdateUser.
- DeleteUser.
- CreateOrder.
- ConfirmOrder.
- CancelOrder.
- GetOrderDetails.

A use case must have a clear responsibility.

Avoid large application services that contain unrelated operations.

## 6.2 CQRS

Separate Commands and Queries.

Commands:

- Change application state.
- Represent an explicit business intention.
- Return only necessary results.

Queries:

- Read application data.
- Must not modify state.
- Return DTOs or read models.

Do not force every query through aggregate repositories.

Use direct query projections when appropriate.

## 6.3 Ports and Interfaces

Application defines interfaces required by its use cases.

Example:

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task AddAsync(
        Order order,
        CancellationToken cancellationToken);
}
```

Infrastructure implements the interface.

Application must never know how the interface is implemented.

## 6.4 Validation

Use FluentValidation for application input validation.

Validation must be executed at the application boundary.

Do not rely exclusively on frontend validation.

Business invariants must still be enforced in Domain.

## 6.5 Transactions

Define transaction boundaries at the use-case level.

Avoid creating unnecessary transactions for simple queries.

Use optimistic concurrency when business requirements require it.

Do not introduce distributed transactions into the modular monolith without a strong justification.

---

# 7. Infrastructure Layer

Infrastructure implements technical adapters.

Recommended structure:

```text
Infrastructure/
│
├── Persistence/
│   ├── DbContext/
│   ├── Configurations/
│   ├── Repositories/
│   ├── Migrations/
│   └── UnitOfWork/
│
├── Identity/
├── Cache/
├── Storage/
├── Email/
├── ExternalServices/
│
└── DependencyInjection.cs
```

## 7.1 Entity Framework Core

Use:

- Entity Framework Core.
- Npgsql PostgreSQL provider.
- Fluent API configurations.
- Explicit database schema configuration.
- Database migrations.

Keep all EF Core configurations inside Infrastructure.

Do not put EF Core attributes into Domain entities.

Example:

```csharp
public sealed class OrderConfiguration
    : IEntityTypeConfiguration<Order>
{
    public void Configure(
        EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "orders");

        builder.HasKey(x => x.Id);
    }
}
```

## 7.2 PostgreSQL

Use PostgreSQL as the primary database.

Recommended schema ownership:

```text
PostgreSQL
│
├── identity
├── users
├── catalog
└── orders
```

Each module owns its database schema and mappings.

Do not create one global DbContext containing every module's entities.

Use UTC consistently for timestamps.

Use appropriate indexes, constraints, and database column types.

## 7.3 Repository Rules

Avoid generic repositories that merely wrap DbSet.

Repositories should reflect aggregate or business persistence requirements.

Avoid returning IQueryable across architectural boundaries.

For read operations, use dedicated query implementations and DTO projections.

Use AsNoTracking for read-only EF Core queries when appropriate.

Avoid N+1 query problems.

---

# 8. Blazor Presentation Layer

Use Blazor Web App with Interactive Server as the initial rendering strategy.

Recommended structure:

```text
MyApp.Web/
│
├── Components/
│   ├── App.razor
│   ├── Routes.razor
│   │
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor
│   │
│   └── Shared/
│
├── Features/
│   │
│   ├── Users/
│   │   ├── Pages/
│   │   ├── Components/
│   │   ├── ViewModels/
│   │   └── State/
│   │
│   ├── Catalog/
│   │
│   └── Orders/
│
├── Authentication/
├── Authorization/
├── Middleware/
├── Extensions/
│
├── wwwroot/
│
├── Program.cs
└── appsettings.json
```

## 8.1 Component Rules

Blazor components must:

- Focus on presentation.
- Display data.
- Handle user interaction.
- Manage presentation state.
- Call application use cases.

Components must not:

- Contain business rules.
- Access DbContext.
- Execute SQL.
- Access infrastructure repositories.
- Implement domain behavior.

## 8.2 Component Design

Prefer small reusable components.

Separate:

- Pages.
- Shared components.
- Feature-specific components.
- View models.
- Presentation state.

Handle all important UI states:

- Loading.
- Empty.
- Success.
- Validation error.
- Unexpected error.

Use accessible HTML and keyboard navigation.

## 8.3 Authentication and Authorization

Authorization must be enforced server-side.

Hiding a button or page is not sufficient authorization.

Use ASP.NET Core authorization policies.

Do not scatter role-name checks throughout Razor components.

Use secure authentication cookies in production.

---

# 9. Authentication and Security

Use ASP.NET Core Identity unless a documented requirement justifies another approach.

Security requirements:

- Secure password hashing.
- Authentication lifecycle management.
- Authorization policies.
- Permission-based authorization when necessary.
- Secure cookies.
- CSRF protection appropriate to the hosting model.
- Input validation.
- Output encoding.
- Rate limiting for public endpoints.
- Secure secret management.

Never:

- Store passwords in plaintext.
- Log access tokens or refresh tokens.
- Commit credentials.
- Expose connection strings.
- Trust frontend authorization.
- Return internal stack traces to users.

Production cookies should use appropriate:

- HttpOnly.
- Secure.
- SameSite.

Use HTTPS in production.

---

# 10. Error Handling

Use centralized error handling.

Separate expected business failures from unexpected system failures.

Examples:

- ValidationError.
- NotFound.
- Conflict.
- Unauthorized.
- Forbidden.
- InternalServerError.

Do not use exceptions for normal business control flow.

Do not silently swallow exceptions.

Log unexpected failures using structured logging.

Never expose internal exception details to end users in production.

For HTTP endpoints, use Problem Details consistently.

---

# 11. Logging and Observability

Use Serilog for structured logging.

Include useful contextual information:

- Correlation ID.
- Request ID.
- Module name.
- Operation name.
- Execution duration.

Avoid logging sensitive personal information.

Implement health checks for:

- Application liveness.
- Database readiness.
- Required external dependencies.

Distinguish liveness from readiness.

---

# 12. Coding Standards

These conventions are mandatory.

## 12.1 General C# Rules

- Use nullable reference types.
- Use async/await for I/O operations.
- Always propagate CancellationToken.
- Avoid async void.
- Avoid .Result and .Wait().
- Use explicit dependency injection.
- Prefer constructor injection.
- Prefer immutable records for commands and DTOs.
- Use meaningful names.
- Keep classes focused.
- Avoid unnecessary abstractions.
- Avoid static service locators.
- Avoid magic strings.
- Avoid duplicated business logic.

## 12.2 Naming Conventions

| Type | Convention |
|---|---|
| Class | PascalCase |
| Interface | I + PascalCase |
| Method | PascalCase |
| Property | PascalCase |
| Local variable | camelCase |
| Parameter | camelCase |
| Constant | PascalCase |
| Namespace | PascalCase |

Examples:

```csharp
CreateOrderCommand
CreateOrderHandler
CreateOrderValidator
IOrderRepository
OrderConfiguration
OrdersDbContext
```

## 12.3 Class Design

Use sealed classes when inheritance is not intended.

Use private setters for domain state where appropriate.

Keep constructors and factory methods responsible for valid object creation.

Avoid giant classes.

Avoid methods with too many parameters.

Do not introduce inheritance where composition is more appropriate.

## 12.4 Comments

Comments should explain:

- Why a decision exists.
- Complex business constraints.
- Non-obvious implementation details.

Do not write comments that simply repeat the code.

---

# 13. Testing Strategy

Testing is mandatory.

Use the following test layers.

## 13.1 Domain Unit Tests

Test:

- Entity behavior.
- Aggregate invariants.
- Value objects.
- Domain services.
- Domain events.
- Invalid state transitions.

Domain tests must not require a database.

## 13.2 Application Unit Tests

Test:

- Command handlers.
- Query handlers.
- Use-case orchestration.
- Validation.
- Authorization decisions.
- Error handling.
- Port interactions.

Mock application ports, not EF Core internals.

## 13.3 Infrastructure Integration Tests

Use real PostgreSQL through Testcontainers where practical.

Test:

- EF Core mappings.
- Repository implementations.
- Database constraints.
- Transactions.
- Migrations.
- Concurrency behavior.
- Complex queries.

Do not treat EF Core InMemory as a replacement for PostgreSQL integration testing.

## 13.4 Blazor Component Tests

Use bUnit or an equivalent maintained component testing framework.

Test:

- Component rendering.
- User interactions.
- Form validation.
- Conditional rendering.
- Loading states.
- Error states.
- Authorization-sensitive UI.

## 13.5 Architecture Tests

Use NetArchTest or ArchUnitNET after verifying compatibility.

Test:

- Domain does not reference Infrastructure.
- Application does not reference Web.
- No circular project references.
- Modules do not reference each other's Infrastructure.
- Module boundaries remain intact.

## 13.6 Functional Tests

Use WebApplicationFactory for host-level tests where applicable.

Use Playwright for critical end-to-end browser workflows.

Test:

- Authentication.
- Authorization.
- Important business workflows.
- Error scenarios.
- Critical user journeys.

---

# 14. Dependency Management

Centralize NuGet versions using:

Directory.Packages.props

Do not scatter package versions across projects.

Before adding a package:

1. Check compatibility with .NET 10.
2. Check maintenance status.
3. Check security advisories.
4. Check license compatibility.
5. Evaluate whether the dependency is necessary.

Avoid adding packages without a clear reason.

Do not automatically introduce:

- MediatR.
- AutoMapper.
- Redis.
- MassTransit.
- Hangfire.
- Elasticsearch.
- Message brokers.

Introduce these only when the application has an actual requirement.

---

# 15. Performance Guidelines

- Use asynchronous database operations.
- Use AsNoTracking for read-only queries.
- Project queries directly into DTOs.
- Avoid loading unnecessary entities.
- Avoid N+1 queries.
- Add indexes based on actual query patterns.
- Use pagination for large datasets.
- Use caching only where justified.
- Avoid unnecessary object allocations.
- Monitor database query performance.

Do not prematurely optimize without measurements.

---

# 16. Docker and Deployment

The application must support Docker deployment.

Recommended services:

```text
Docker Compose
│
├── myapp-web
│
└── postgres
```

Production configuration must come from environment variables or a secure secret store.

Do not commit production credentials.

Use multi-stage Docker builds.

Keep the final image as small as reasonably practical.

Provide documented database migration procedures.

Never automatically destroy production data.

---

# 17. Architecture Decision Records

Important architectural changes must be documented.

Store ADRs under:

```text
docs/decisions/
```

Each ADR should contain:

- Context.
- Decision.
- Alternatives considered.
- Consequences.
- Trade-offs.

Do not silently change architectural principles.

---

# 18. Development Workflow

Before implementing any feature, follow this workflow.

## Step 1: Understand

Inspect:

- Existing solution structure.
- Relevant module.
- Domain model.
- Application use cases.
- Infrastructure implementations.
- Existing tests.
- Architecture documentation.

Never assume the existing implementation works as intended.

## Step 2: Analyze

Identify:

- Business requirements.
- Affected modules.
- Required domain changes.
- Required application changes.
- Required infrastructure changes.
- UI changes.
- Security implications.
- Testing requirements.

## Step 3: Plan

For non-trivial tasks, provide a short implementation plan before making changes.

Identify files and projects that will be affected.

## Step 4: Implement

Implement the smallest coherent solution.

Follow the existing architecture.

Do not introduce unrelated refactoring.

Do not duplicate existing functionality.

## Step 5: Test

Add or update tests.

Run relevant unit tests.

Run integration tests when persistence behavior changes.

Run architecture tests when project dependencies change.

## Step 6: Verify

Run:

```bash
dotnet restore

dotnet build --configuration Release

dotnet test --configuration Release

dotnet format --verify-no-changes
```

Only report commands that were actually executed.

Do not claim successful verification without checking exit codes.

## Step 7: Review

Review:

- Dependency boundaries.
- Security.
- Error handling.
- Performance.
- Naming.
- Test coverage.
- Unrelated file changes.
- Secrets accidentally included in changes.

## Step 8: Report

Summarize:

- What was implemented.
- Which projects were modified.
- Which tests were added.
- Which commands were executed.
- Test results.
- Known limitations.

---

# 19. Existing Code and Bug Fixing Rules

When fixing a bug:

1. Reproduce or understand the failure.
2. Identify the root cause.
3. Check whether the issue violates an existing architectural rule.
4. Fix the root cause instead of adding a superficial workaround.
5. Add a regression test.
6. Verify affected workflows.
7. Avoid unrelated refactoring.

Do not rewrite working modules just because another implementation looks cleaner.

Do not remove existing functionality without explicit approval.

Preserve backward compatibility unless a breaking change is requested.

---

# 20. Architecture Protection

These rules cannot be violated without explicit approval.

- Never move business logic into Blazor components.
- Never move business rules into Infrastructure.
- Never let Domain depend on EF Core.
- Never let Application depend on Infrastructure.
- Never allow direct cross-module Infrastructure access.
- Never expose database entities as UI contracts.
- Never bypass application use cases for database operations.
- Never introduce circular dependencies.
- Never introduce microservices without explicit requirements.
- Never add unnecessary design patterns.
- Never silently change database ownership.

If a requested implementation conflicts with these rules, explain the conflict and propose a compliant alternative.

---

# 21. Definition of Done

A feature is considered complete only when:

- Business requirements are satisfied.
- Domain invariants are enforced.
- Application use cases are implemented.
- Infrastructure adapters are implemented where necessary.
- Blazor UI is implemented where necessary.
- Validation is implemented.
- Authorization is enforced server-side.
- Error handling is consistent.
- Relevant tests are added or updated.
- Tests pass.
- Solution builds successfully.
- Architecture boundaries remain valid.
- Documentation is updated when necessary.
- No secrets are committed.
- No unrelated changes are introduced.

---

# 22. Final Instructions to Claude

You are responsible for maintaining the long-term quality of this codebase.

Do not optimize for the fastest implementation at the expense of architecture.

Do not blindly follow a requested implementation if it introduces architectural problems.

Think in terms of:

- Business capabilities.
- Domain boundaries.
- Explicit use cases.
- Dependency inversion.
- Module ownership.
- Testability.
- Security.
- Maintainability.

Prefer simple, explicit, maintainable solutions over unnecessary abstractions.

Always inspect existing code before changing it.

Always preserve established architectural decisions unless there is a documented reason to change them.

Your primary responsibility is to ensure that every new feature, bug fix, and refactoring remains consistent with this architecture.
