# Mermaid Diagrams for Documentation

This document contains reusable Mermaid diagram scripts for the Trellis documentation. All diagrams use the ````mermaid` code fence and are compatible with DocFX, GitHub, and mermaid.live.

## Table of Contents

1. [Architecture Diagrams](#architecture-diagrams)
2. [Railway-Oriented Programming](#railway-oriented-programming)
3. [Entity Framework Integration](#entity-framework-integration)
4. [Error Handling](#error-handling)
5. [Value Objects](#value-objects)
6. [Domain-Driven Design](#domain-driven-design)
7. [Sequence Diagrams](#sequence-diagrams)

---

## Diagrams Added to Documentation

### ? clean-architecture.md (6 diagrams added)

1. **Simple Pattern Architecture** - Shows 3-layer architecture (API, Domain, Infrastructure)
2. **CQRS Architecture** - Shows 4-layer architecture with Application layer
3. **User Registration Sequence** - Complete flow from HTTP request to response
4. **Railway-Oriented Flow** - Simple Pattern ROP chain visualization
5. **CQRS Command Flow** - Sequence diagram with Mediator and Event Bus
6. **Dependency Flow** - Clean architecture dependency rules
7. **Validation Layers** - Three layers of validation (API, Application, Domain)

### ? integration-ef.md (4 diagrams added)

1. **Repository Pattern Architecture** - Shows Maybe<T> and Result<T> usage
2. **Maybe vs Result Decision** - Flowchart showing when to use each
3. **Database Command Result** - Exception handling in SaveAsync
4. **Exception Handling Strategy** - Expected vs unexpected failures

---

## Architecture Diagrams

### Simple Pattern Architecture (clean-architecture.md)

```mermaid
graph TB
    subgraph API["API Layer (ASP.NET Core)"]
        C[Controllers]
        VO[Value Objects]
        D[Domain Logic]
    end
    
    subgraph Domain["Domain Layer (DDD)"]
        A[Aggregates]
        E[Entities]
        VO2[Value Objects]
        DL[Domain Logic]
    end
    
    subgraph Infrastructure["Infrastructure Layer"]
        R[Repositories]
        DB[(Database)]
        EXT[External Services]
    end
    
    C --> VO
    VO --> D
    D --> A
    A --> E
    A --> VO2
    D --> R
    R --> DB
    D --> EXT
    
    style API fill:#e1f5ff
    style Domain fill:#fff4e1
    style Infrastructure fill:#f0f0f0
```

### CQRS Pattern Architecture (clean-architecture.md)

```mermaid
graph TB
    subgraph API["API Layer (ASP.NET Core)"]
        CTRL[Controllers/Endpoints]
        HTTP[ToActionResult/ToHttpResult]
    end
    
    subgraph Application["Application Layer (CQRS)"]
        CMD[Commands]
        QRY[Queries]
        HAND[Handlers]
        VAL[Validation]
    end
    
    subgraph Domain["Domain Layer (DDD)"]
        AGG[Aggregates]
        ENT[Entities]
        VO[Value Objects]
        EVT[Domain Events]
    end
    
    subgraph Infrastructure["Infrastructure Layer"]
        REPO[Repositories]
        DBCTX[DbContext]
        EXT[External Services]
        DB[(Database)]
    end
    
    CTRL --> HTTP
    HTTP --> CMD
    HTTP --> QRY
    CMD --> HAND
    QRY --> HAND
    HAND --> VAL
    VAL --> AGG
    AGG --> ENT
    AGG --> VO
    AGG --> EVT
    HAND --> REPO
    REPO --> DBCTX
    DBCTX --> DB
    HAND --> EXT
    
    style API fill:#e1f5ff
    style Application fill:#ffe1f5
    style Domain fill:#fff4e1
    style Infrastructure fill:#f0f0f0
```

---

## Railway-Oriented Programming

### Happy Path Flow (basics.md, intro.md)

```mermaid
graph LR
    A[Input] --> B{TryCreate}
    B -->|Success| C{Combine}
    C -->|Success| D{Bind}
    D -->|Success| E{Tap}
    E -->|Success| F[Result Success]
    
    B -.->|Failure| G[Error Track]
    C -.->|Failure| G
    D -.->|Failure| G
    
    style F fill:#90EE90
    style G fill:#FFB6C6
```

### Complete ROP Chain (basics.md)

```mermaid
graph TB
    START[Raw Input] --> VC1{Value Object 1<br/>TryCreate}
    START --> VC2{Value Object 2<br/>TryCreate}
    START --> VC3{Value Object 3<br/>TryCreate}
    
    VC1 -->|Success| CMB{Combine}
    VC2 -->|Success| CMB
    VC3 -->|Success| CMB
    
    VC1 -.->|Failure| ERR[Aggregate Errors]
    VC2 -.->|Failure| ERR
    VC3 -.->|Failure| ERR
    
    CMB -->|Success| BIND{Bind<br/>Domain Logic}
    CMB -.->|Failure| ERR
    
    BIND -->|Success| ENS{Ensure<br/>Business Rules}
    BIND -.->|Failure| ERR
    
    ENS -->|Success| TAP{Tap<br/>Side Effects}
    ENS -.->|Failure| ERR
    
    TAP --> MATCH{Match}
    ERR --> MATCH
    
    MATCH -->|Success| SUCCESS[200 OK]
    MATCH -->|Failure| FAIL[400/404/409]
    
    style SUCCESS fill:#90EE90
    style FAIL fill:#FFB6C6
    style ERR fill:#FFD700
```

### Railway Track Metaphor (intro.md)

```mermaid
graph LR
    subgraph Success Track
        A[Operation 1] -->|Success| B[Operation 2]
        B -->|Success| C[Operation 3]
        C -->|Success| D[Operation 4]
        D -->|Success| E[Final Result]
    end
    
    subgraph Error Track
        F[Error 1]
        G[Error 2]
        H[Error 3]
    end
    
    A -.->|Failure| F
    B -.->|Failure| G
    C -.->|Failure| H
    
    F --> I[Handle Error]
    G --> I
    H --> I
    
    style E fill:#90EE90
    style I fill:#FFB6C6
```

---

## Entity Framework Integration

### Repository Pattern Architecture (integration-ef.md) ? ADDED

```mermaid
graph TB
    subgraph Controller["Controller Layer"]
        REQ[HTTP Request]
    end
    
    subgraph Service["Service/Domain Layer"]
        VAL{Validate Input}
        LOGIC{Business Logic}
        DEC{Domain Decision}
    end
    
    subgraph Repository["Repository Layer"]
        QUERY[Query Methods<br/>return Maybe&lt;T&gt;]
        COMMAND[Command Methods<br/>return Result]
    end
    
    subgraph Database["Database"]
        DB[(EF Core<br/>DbContext)]
    end
    
    REQ --> VAL
    VAL -->|Valid| LOGIC
    LOGIC --> DEC
    
    DEC -->|Need Data?| QUERY
    QUERY --> DB
    DB -.->|null?| MAYBE[Maybe&lt;T&gt;]
    MAYBE --> DEC
    
    DEC -->|Save/Update?| COMMAND
    COMMAND --> DB
    DB -.->|Success| RES_OK[Result.Ok]
    DB -.->|Duplicate Key| RES_CONFLICT[Error.Conflict]
    DB -.->|FK Violation| RES_DOMAIN[Error.Domain]
    DB -.->|Concurrency| RES_CONFLICT2[Error.Conflict]
    
    RES_OK --> HTTP_OK[200 OK]
    RES_CONFLICT --> HTTP_409[409 Conflict]
    RES_DOMAIN --> HTTP_422[422 Unprocessable]
    RES_CONFLICT2 --> HTTP_409
    
    style MAYBE fill:#E1F5FF
    style RES_OK fill:#90EE90
    style RES_CONFLICT fill:#FFB6C6
    style RES_DOMAIN fill:#FFD700
    style RES_CONFLICT2 fill:#FFB6C6
```

### Maybe vs Result Decision (integration-ef.md) ? ADDED

```mermaid
flowchart LR
    subgraph Repository
        REPO_QUERY[Repository Query<br/>GetByEmailAsync]
        DB_QUERY[(Database Query<br/>FirstOrDefaultAsync)]
    end
    
    subgraph Domain
        CHECK{User exists?}
        LOGIN[Login Flow<br/>HasNoValue = Error]
        REGISTER[Register Flow<br/>HasValue = Error]
    end
    
    REPO_QUERY --> DB_QUERY
    DB_QUERY -->|User or null| MAYBE[Maybe&lt;User&gt;]
    MAYBE --> CHECK
    
    CHECK -->|Login scenario| LOGIN
    CHECK -->|Register scenario| REGISTER
    
    LOGIN -->|HasNoValue| ERR1[Error.NotFound<br/>User not found]
    LOGIN -->|HasValue| OK1[Result.Ok<br/>Verify password]
    
    REGISTER -->|HasValue| ERR2[Error.Conflict<br/>Email taken]
    REGISTER -->|HasNoValue| OK2[Result.Ok<br/>Can register]
    
    style MAYBE fill:#E1F5FF
    style ERR1 fill:#FFB6C6
    style ERR2 fill:#FFB6C6
    style OK1 fill:#90EE90
    style OK2 fill:#90EE90
```

### Database Command Result Pattern (integration-ef.md) ? ADDED

```mermaid
flowchart TB
    START[SaveAsync User] --> TRY{Try SaveChangesAsync}
    
    TRY -->|Success| SUCCESS[Result.Ok]
    
    TRY -->|DbUpdateConcurrencyException| CONFLICT1[Error.Conflict<br/>Modified by another process]
    
    TRY -->|DbUpdateException<br/>Duplicate Key| CONFLICT2[Error.Conflict<br/>Email already exists]
    
    TRY -->|DbUpdateException<br/>Foreign Key| DOMAIN[Error.Domain<br/>Referential integrity]
    
    TRY -->|Other Exception<br/>Connection/Timeout| PROPAGATE[Exception Propagates<br/>Global Handler]
    
    SUCCESS --> HTTP_200[200 OK]
    CONFLICT1 --> HTTP_409[409 Conflict]
    CONFLICT2 --> HTTP_409_2[409 Conflict]
    DOMAIN --> HTTP_422[422 Unprocessable]
    PROPAGATE --> HTTP_500[500 Internal Server Error]
    
    style SUCCESS fill:#90EE90
    style CONFLICT1 fill:#FFB6C6
    style CONFLICT2 fill:#FFB6C6
    style DOMAIN fill:#FFD700
    style PROPAGATE fill:#FF6B6B
```

### Exception Handling Strategy (integration-ef.md) ? ADDED

```mermaid
flowchart TB
    START[Database Operation] --> CATCH{Exception Type?}
    
    CATCH -->|DbUpdateConcurrencyException| EXPECTED1[Expected Failure]
    CATCH -->|DbUpdateException<br/>Duplicate Key| EXPECTED2[Expected Failure]
    CATCH -->|DbUpdateException<br/>Foreign Key| EXPECTED3[Expected Failure]
    CATCH -->|Connection Error<br/>Timeout<br/>Network Issue| UNEXPECTED[Unexpected Failure]
    
    EXPECTED1 --> CONVERT1[Convert to Result<br/>Error.Conflict]
    EXPECTED2 --> CONVERT2[Convert to Result<br/>Error.Conflict]
    EXPECTED3 --> CONVERT3[Convert to Result<br/>Error.Domain]
    
    CONVERT1 --> RETURN[Return Result&lt;T&gt;<br/>to caller]
    CONVERT2 --> RETURN
    CONVERT3 --> RETURN
    
    UNEXPECTED --> PROPAGATE[Let Exception<br/>Propagate]
    PROPAGATE --> GLOBAL[Global Exception<br/>Handler]
    GLOBAL --> RETRY{Retry Policy?}
    RETRY -->|Transient| CIRCUIT[Circuit Breaker]
    RETRY -->|Non-Transient| LOG[Log & Return 500]
    
    RETURN --> HTTP_4XX[4xx Response<br/>Client Error]
    LOG --> HTTP_500[500 Response<br/>Server Error]
    
    style EXPECTED1 fill:#FFE1A8
    style EXPECTED2 fill:#FFE1A8
    style EXPECTED3 fill:#FFE1A8
    style UNEXPECTED fill:#FFB6C6
    style RETURN fill:#90EE90
    style PROPAGATE fill:#FF6B6B
```

---

## Error Handling

### Error Type to HTTP Status Mapping (error-handling.md, integration-aspnet.md)

```mermaid
graph LR
    subgraph Errors["Error Types"]
        VAL[ValidationError]
        BAD[BadRequestError]
        UNAUTH[UnauthorizedError]
        FORBID[ForbiddenError]
        NOTFOUND[NotFoundError]
        CONFLICT[ConflictError]
        DOMAIN[DomainError]
        RATE[RateLimitError]
        UNEXP[UnexpectedError]
        UNAVAIL[ServiceUnavailableError]
    end
    
    subgraph HTTP["HTTP Status Codes"]
        VAL --> H400[400 Bad Request]
        BAD --> H400
        UNAUTH --> H401[401 Unauthorized]
        FORBID --> H403[403 Forbidden]
        NOTFOUND --> H404[404 Not Found]
        CONFLICT --> H409[409 Conflict]
        DOMAIN --> H422[422 Unprocessable Entity]
        RATE --> H429[429 Too Many Requests]
        UNEXP --> H500[500 Internal Server Error]
        UNAVAIL --> H503[503 Service Unavailable]
    end
    
    style VAL fill:#FFE1E1
    style BAD fill:#FFE1E1
    style UNAUTH fill:#FFE1E1
    style FORBID fill:#FFE1E1
    style NOTFOUND fill:#FFE1E1
    style CONFLICT fill:#FFE1E1
    style DOMAIN fill:#FFD700
    style RATE fill:#FFE1E1
    style UNEXP fill:#FFB6C6
    style UNAVAIL fill:#FFB6C6
```

### Error Aggregation (error-handling.md)

```mermaid
flowchart TB
    START[Multiple Operations] --> OP1[Operation 1]
    START --> OP2[Operation 2]
    START --> OP3[Operation 3]
    
    OP1 --> RES1{Result 1}
    OP2 --> RES2{Result 2}
    OP3 --> RES3{Result 3}
    
    RES1 -->|ValidationError| VAL1[Email invalid]
    RES2 -->|ValidationError| VAL2[Password weak]
    RES3 -->|Success| OK
    
    VAL1 --> COMBINE[Combine Errors]
    VAL2 --> COMBINE
    
    COMBINE --> AGG[AggregateError<br/>Multiple validation errors]
    
    AGG --> RESPONSE[400 Bad Request<br/>errors: {<br/>&nbsp;&nbsp;email: [...],<br/>&nbsp;&nbsp;password: [...]<br/>}]
    
    style VAL1 fill:#FFB6C6
    style VAL2 fill:#FFB6C6
    style OK fill:#90EE90
    style AGG fill:#FFD700
```

---

## Value Objects

### Value Object Class Diagram (PrimitiveValueObjects README)

```mermaid
classDiagram
    class RequiredString {
        <<abstract>>
        +string Value
        +TryCreate(string?) Result~T~
        +Parse(string) T
        +TryParse(string, out T) bool
    }
    
    class RequiredGuid {
        <<abstract>>
        +Guid Value
        +NewUnique() T
        +TryCreate(Guid?) Result~T~
        +TryCreate(string?) Result~T~
        +Parse(string) T
    }
    
    class FirstName {
        +TryCreate(string?) Result~FirstName~
    }
    
    class LastName {
        +TryCreate(string?) Result~LastName~
    }
    
    class EmailAddress {
        +TryCreate(string?) Result~EmailAddress~
    }
    
    class UserId {
        +NewUnique() UserId
        +TryCreate(Guid?) Result~UserId~
    }
    
    class OrderId {
        +NewUnique() OrderId
        +TryCreate(Guid?) Result~OrderId~
    }
    
    RequiredString <|-- FirstName
    RequiredString <|-- LastName
    RequiredString <|-- EmailAddress
    RequiredGuid <|-- UserId
    RequiredGuid <|-- OrderId
```

### Value Object Validation Flow (PrimitiveValueObjects SAMPLES)

```mermaid
flowchart LR
    INPUT[Raw Input<br/>email, firstName, lastName] --> VO1{EmailAddress<br/>TryCreate}
    INPUT --> VO2{FirstName<br/>TryCreate}
    INPUT --> VO3{LastName<br/>TryCreate}
    
    VO1 -->|Success| V1[EmailAddress]
    VO2 -->|Success| V2[FirstName]
    VO3 -->|Success| V3[LastName]
    
    VO1 -.->|Failure| E1[Validation Error]
    VO2 -.->|Failure| E2[Validation Error]
    VO3 -.->|Failure| E3[Validation Error]
    
    V1 --> COMBINE[Combine]
    V2 --> COMBINE
    V3 --> COMBINE
    
    E1 --> ERRORS[Aggregate Errors]
    E2 --> ERRORS
    E3 --> ERRORS
    
    COMBINE --> BIND{Bind}
    BIND --> DOMAIN[User.TryCreate]
    
    ERRORS --> FAIL[Result.Fail]
    DOMAIN --> SUCCESS[Result.Ok]
    
    style SUCCESS fill:#90EE90
    style FAIL fill:#FFB6C6
    style ERRORS fill:#FFD700
```

---

## Domain-Driven Design

### Aggregate Pattern (DomainDrivenDesign README)

```mermaid
classDiagram
    class Aggregate~TId~ {
        <<abstract>>
        +TId Id
        +bool IsChanged
        +IReadOnlyList~IDomainEvent~ UncommittedEvents()
        +void AcceptChanges()
        #void AddDomainEvent(IDomainEvent)
    }
    
    class Order {
        +OrderId Id
        +CustomerId CustomerId
        +OrderStatus Status
        +Money Total
        +IReadOnlyList~OrderLine~ Lines
        +TryCreate(CustomerId) Result~Order~
        +AddLine(...) Result~Order~
        +Submit() Result~Order~
    }
    
    class OrderLine {
        +ProductId ProductId
        +string ProductName
        +Money Price
        +int Quantity
    }
    
    class OrderCreatedEvent {
        +OrderId OrderId
        +CustomerId CustomerId
        +DateTime CreatedAt
    }
    
    class OrderSubmittedEvent {
        +OrderId OrderId
        +Money Total
        +DateTime SubmittedAt
    }
    
    Aggregate <|-- Order
    Order "1" *-- "*" OrderLine
    Order ..> OrderCreatedEvent : raises
    Order ..> OrderSubmittedEvent : raises
```

### Entity vs Value Object vs Aggregate (DomainDrivenDesign README)

```mermaid
graph TB
    subgraph Aggregate Root
        AGG[Order Aggregate]
        AGG_ID[OrderId]
        AGG --> AGG_ID
    end
    
    subgraph Entities
        ENT1[OrderLine Entity]
        ENT1_ID[OrderLineId]
        ENT1 --> ENT1_ID
    end
    
    subgraph Value Objects
        VO1[Money]
        VO2[ProductName]
        VO3[Quantity]
    end
    
    AGG -->|contains| ENT1
    AGG -->|uses| VO1
    ENT1 -->|uses| VO2
    ENT1 -->|uses| VO3
    
    AGG -.->|references by ID| CUST[Customer Aggregate]
    AGG -.->|references by ID| PROD[Product Aggregate]
    
    style AGG fill:#FFE1A8
    style ENT1 fill:#E1F5FF
    style VO1 fill:#E8F5E9
    style VO2 fill:#E8F5E9
    style VO3 fill:#E8F5E9
```

---

## Sequence Diagrams

### User Registration Flow (clean-architecture.md)

```mermaid
sequenceDiagram
    participant Client
    participant Controller
    participant ValueObjects
    participant Domain
    participant Repository
    participant Database
    
    Client->>Controller: POST /api/users/register<br/>{email, firstName, lastName}
    
    Controller->>ValueObjects: FirstName.TryCreate()
    ValueObjects-->>Controller: Result<FirstName>
    
    Controller->>ValueObjects: LastName.TryCreate()
    ValueObjects-->>Controller: Result<LastName>
    
    Controller->>ValueObjects: EmailAddress.TryCreate()
    ValueObjects-->>Controller: Result<EmailAddress>
    
    Note over Controller: Combine all validations
    
    alt All validations pass
        Controller->>Domain: User.TryCreate(...)
        Domain->>Domain: Validate business rules
        Domain-->>Controller: Result<User>
        
        Controller->>Repository: Save(user)
        Repository->>Database: INSERT INTO Users
        
        alt Save successful
            Database-->>Repository: Success
            Repository-->>Controller: Result.Ok()
            Controller-->>Client: 200 OK + User JSON
        else Duplicate email
            Database-->>Repository: Duplicate key error
            Repository-->>Controller: Error.Conflict()
            Controller-->>Client: 409 Conflict
        end
    else Validation fails
        Controller-->>Client: 400 Bad Request<br/>+ Validation errors
    end
```

### CQRS Command Flow (clean-architecture.md)

```mermaid
sequenceDiagram
    participant Client
    participant Controller
    participant Mediator
    participant CommandHandler
    participant Domain
    participant Repository
    participant EventBus
    
    Client->>Controller: POST /api/orders
    Controller->>Controller: CreateOrderCommand.TryCreate()
    Controller->>Mediator: Send(command)
    Mediator->>CommandHandler: Handle(command)
    
    CommandHandler->>Repository: GetByEmailAsync(email)
    Repository-->>CommandHandler: Maybe<Customer>
    
    CommandHandler->>Domain: Order.TryCreate()
    Domain-->>CommandHandler: Result<Order>
    
    CommandHandler->>Repository: SaveAsync(order)
    Repository-->>CommandHandler: Result<Unit>
    
    CommandHandler->>Domain: order.UncommittedEvents()
    Domain-->>CommandHandler: [OrderCreatedEvent]
    
    CommandHandler->>EventBus: Publish(events)
    EventBus-->>CommandHandler: Ack
    
    CommandHandler->>Domain: order.AcceptChanges()
    
    CommandHandler-->>Mediator: Result<Order>
    Mediator-->>Controller: Result<Order>
    Controller-->>Client: 200 OK
```

### OpenTelemetry Tracing Flow (integration-observability.md)

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant ROP as Result Operations
    participant Tracer as OpenTelemetry
    participant Backend as Jaeger/AppInsights
    
    Client->>API: HTTP Request
    API->>Tracer: Start Root Span
    
    API->>ROP: Operation 1 (TryCreate)
    ROP->>Tracer: Start Child Span
    ROP-->>Tracer: End Span (Status: Ok)
    ROP-->>API: Result<T>
    
    API->>ROP: Operation 2 (Bind)
    ROP->>Tracer: Start Child Span
    ROP-->>Tracer: End Span (Status: Ok)
    ROP-->>API: Result<T>
    
    API->>ROP: Operation 3 (Ensure)
    ROP->>Tracer: Start Child Span
    Note over ROP,Tracer: Validation fails
    ROP->>Tracer: Set Status: Error
    ROP->>Tracer: Set Error Attributes
    ROP-->>Tracer: End Span (Status: Error)
    ROP-->>API: Result.Fail
    
    API-->>Tracer: End Root Span
    Tracer->>Backend: Export Trace
    API-->>Client: 400 Bad Request
    
    Note over Backend: Trace shows:<br/>- Request duration<br/>- Operation timeline<br/>- Error location<br/>- Error details
```

---

## Usage Instructions

### In DocFX Markdown Files

Simply paste the diagram code with triple backticks and `mermaid` language identifier:

````markdown
```mermaid
graph LR
    A[Start] --> B[End]
```
````

### Testing Diagrams

1. **Mermaid Live Editor**: https://mermaid.live/
2. **GitHub Preview**: Commit and view on GitHub (supports Mermaid natively)
3. **VS Code Extension**: Install "Markdown Preview Mermaid Support"

### Color Palette Used

- Success: `#90EE90` (Light Green)
- Error: `#FFB6C6` (Light Pink)
- Warning: `#FFD700` (Gold)
- Info: `#E1F5FF` (Light Blue)
- Neutral: `#F0F0F0` (Light Gray)
- Critical: `#FF6B6B` (Red)
- Highlight: `#FFE1A8` (Light Orange)

### Best Practices

1. **Keep diagrams simple** - Focus on one concept per diagram
2. **Use consistent colors** - Follow the palette above for consistency
3. **Add descriptions** - Include a paragraph explaining what the diagram shows
4. **Test before commit** - Verify diagrams render correctly in mermaid.live
5. **Mobile-friendly** - Avoid overly complex diagrams that don't render well on mobile

---

## Contributing

To add a new diagram:

1. Create the diagram in [mermaid.live](https://mermaid.live/)
2. Test it renders correctly
3. Add it to this document with proper categorization
4. Include usage notes and which doc files it's suitable for
5. Use the established color palette for consistency

---

**Last Updated:** December 2024
**Mermaid Version:** Compatible with DocFX, GitHub, and Mermaid Live v10+
