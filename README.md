# 🚀 Realtime Middleware

> **High-performance real-time communication middleware** between C#/.NET and Python modules using WebSockets, a priority message bus, and a REST API — production-ready with Docker and CI/CD.

---

## 📐 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        C# Backend (.NET 8)                       │
│                                                                   │
│  ┌──────────────┐    ┌──────────────────────┐   ┌────────────┐  │
│  │  REST API    │    │  PriorityMessageBus  │   │ WebSocket  │  │
│  │  (ASP.NET)   │───▶│  (in-memory queue)   │◀──│  Handler   │  │
│  └──────────────┘    └──────────────────────┘   └────────────┘  │
│         │                     │                        │          │
│  ┌──────────────┐    ┌──────────────────────┐   ┌────────────┐  │
│  │ MessageSvc   │    │  RetryBackgroundSvc  │   │  WsManager │  │
│  └──────────────┘    └──────────────────────┘   └────────────┘  │
│         │                                                         │
│  ┌──────────────────────────────┐                                 │
│  │   InMemoryMessageRepository │ (swap with EF Core / Redis)     │
│  └──────────────────────────────┘                                 │
└─────────────────────────────────────────────────────────────────┘
          ▲  REST (HTTP)                ▲  WebSocket (ws://)
          │                            │
┌─────────────────┐          ┌─────────────────────────┐
│   API Clients   │          │    Python Client(s)      │
│  (curl/Postman) │          │  SensorSimulator + WS    │
└─────────────────┘          └─────────────────────────┘
```

### Key Design Decisions

| Concern | Choice | Rationale |
|---|---|---|
| Message ordering | `PriorityQueue<Message, int>` | O(log n) dequeue, Critical messages first |
| Serialization | System.Text.Json | Built-in, fast, AOT-friendly |
| Auth | JWT Bearer | Stateless, works for both HTTP and WS |
| Repository | In-memory (swappable) | Clean Architecture interface — drop in EF Core/Redis |
| Retry | BackgroundService | Non-blocking, configurable interval |
| Logging | Serilog structured | JSON-friendly, file + console sinks |

---

## 📁 Project Structure

```
realtime-middleware/
├── RealtimeMiddleware.sln
├── docker-compose.yml
├── docker/
│   ├── Dockerfile.api
│   └── Dockerfile.python
├── .github/workflows/
│   └── ci-cd.yml
├── src/
│   ├── Core/
│   │   ├── Domain/
│   │   │   ├── Entities/Message.cs          # Aggregate root
│   │   │   ├── Enums/MessageEnums.cs        # Priority + Status enums
│   │   │   └── Interfaces/                  # IMessageRepository, IMessageBus
│   │   ├── Application/
│   │   │   ├── DTOs/MessageDTOs.cs          # Request/Response records
│   │   │   ├── Interfaces/IServices.cs      # Service contracts
│   │   │   └── Services/MessageService.cs   # Core business logic
│   │   └── Infrastructure/
│   │       ├── MessageBus/
│   │       │   ├── PriorityMessageBus.cs    # Priority queue + dispatcher
│   │       │   └── RetryBackgroundService.cs
│   │       ├── WebSocket/
│   │       │   ├── WebSocketManager.cs      # Connection registry
│   │       │   └── WebSocketHandler.cs      # Per-connection lifecycle
│   │       └── Persistence/
│   │           └── InMemoryMessageRepository.cs
│   ├── Api/
│   │   ├── Controllers/Controllers.cs       # Messages, Auth, Health
│   │   ├── Auth/AuthService.cs             # JWT generation
│   │   ├── Middleware/ExceptionMiddleware.cs
│   │   ├── Program.cs                       # DI + pipeline
│   │   └── appsettings.json
│   └── Tests/
│       └── Unit/
│           ├── MessageBusTests.cs
│           ├── MessageEntityTests.cs
│           └── MessageServiceTests.cs
└── python-client/
    ├── src/client.py                        # Async WS client + simulator
    ├── tests/test_client.py
    └── requirements.txt
```

---

## ⚡ Quick Start

### Option A — Docker (recommended)

```bash
# Clone the repo
git clone https://github.com/your-org/realtime-middleware.git
cd realtime-middleware

# Start everything
docker-compose up --build

# Start with 2 Python clients
docker-compose --profile multi-client up --build
```

### Option B — Local development

**Prerequisites:** .NET 8 SDK, Python 3.12+

```bash
# Terminal 1 — Start C# API
cd src/Api
dotnet run
# API: http://localhost:5000  |  WebSocket: ws://localhost:5000/ws

# Terminal 2 — Start Python client
cd python-client
pip install -r requirements.txt
python src/client.py
```

---

## 🔐 Authentication

All REST endpoints (except `/api/health` and `/api/auth/login`) require a JWT Bearer token.

### Get a token

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "admin123"}'
```

```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "username": "admin"
  }
}
```

**Built-in users:**
| Username | Password | Role |
|---|---|---|
| `admin` | `admin123` | Admin |
| `client` | `client123` | Client |

---

## 📡 REST API Reference

> Set `TOKEN=<your-jwt-token>` before running the examples below.

### Publish a message

```bash
curl -X POST http://localhost:5000/api/messages \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "topic": "sensors.temperature",
    "payload": "{\"value\": 36.5, \"unit\": \"C\"}",
    "priority": 2,
    "source": "device-001"
  }'
```

**Priority values:** `0`=Low, `1`=Normal, `2`=High, `3`=Critical

### Get all messages

```bash
curl http://localhost:5000/api/messages \
  -H "Authorization: Bearer $TOKEN"
```

### Get messages by topic

```bash
curl http://localhost:5000/api/messages/topic/sensors.temperature \
  -H "Authorization: Bearer $TOKEN"
```

### Get system statistics

```bash
curl http://localhost:5000/api/messages/stats \
  -H "Authorization: Bearer $TOKEN"
```

```json
{
  "success": true,
  "data": {
    "totalMessages": 142,
    "pendingMessages": 3,
    "processedMessages": 135,
    "failedMessages": 4,
    "connectedClients": 2,
    "serverTime": "2024-01-15T10:30:00Z"
  }
}
```

### Trigger retry of failed messages (Admin only)

```bash
curl -X POST http://localhost:5000/api/messages/retry \
  -H "Authorization: Bearer $TOKEN"
```

### Health check (no auth)

```bash
curl http://localhost:5000/api/health
```

---

## 🔌 WebSocket Reference

Connect to `ws://localhost:5000/ws?clientId=my-client`

### Message format

```json
{
  "type": "publish",
  "topic": "sensors.temperature",
  "payload": "{\"value\": 22.5}",
  "priority": 1,
  "source": "my-client",
  "target": null,
  "timestamp": "2024-01-15T10:30:00Z"
}
```

### Supported message types

| Type | Direction | Description |
|---|---|---|
| `publish` | Client → Server | Publish a message to the bus |
| `ping` | Client → Server | Keepalive ping |
| `pong` | Server → Client | Response to ping |
| `message` | Server → Client | Incoming message from bus |
| `connected` | Server → Client | Connection confirmation |
| `error` | Server → Client | Error response |

### Example: send with `wscat`

```bash
# Install: npm i -g wscat
wscat -c "ws://localhost:5000/ws?clientId=test-001"

# Then type:
{"type":"publish","topic":"alerts","payload":"{\"msg\":\"critical alert\"}","priority":3,"source":"test"}

# Ping
{"type":"ping","topic":"__heartbeat__","payload":"","priority":0,"source":"test"}
```

---

## 🧪 Running Tests

### C# tests (NUnit)

```bash
dotnet test src/Tests/RealtimeMiddleware.Tests.csproj -v normal
```

### Python tests (pytest)

```bash
cd python-client
pip install -r requirements.txt
pytest tests/ -v
```

---

## 🔄 Priority Queue Behavior

Messages are dequeued in priority order regardless of arrival time:

```
Arrival order:  LOW → NORMAL → CRITICAL
Processing order: CRITICAL → NORMAL → LOW
```

The `PriorityQueue<Message, int>` uses `-(int)priority` as the key, so `Critical=3 → -3` (lowest int = highest priority in .NET's min-heap).

---

## 🔁 Retry System

Failed messages are automatically retried by `RetryBackgroundService`:
- Runs every **30 seconds**
- Max **3 retries** per message
- After 3 failures → message stays in `Failed` state (dead letter)
- Each retry resets `Status` to `Pending` and clears `ErrorMessage`

---

## 🌐 Extending to Production

### Replace in-memory repository with EF Core

```csharp
// In Program.cs, replace:
builder.Services.AddSingleton<IMessageRepository, InMemoryMessageRepository>();

// With:
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IMessageRepository, EfMessageRepository>();
```

### Add Redis pub/sub

Replace `PriorityMessageBus` with a Redis Streams implementation while keeping the same `IMessageBus` interface — no changes to application layer needed.

---

## 📜 Swagger UI

Available at: **http://localhost:5000/swagger** (Development mode)

---

## 🏗️ CI/CD Pipeline

GitHub Actions pipeline (`.github/workflows/ci-cd.yml`):

```
Push to main/develop
       │
       ├── .NET Build & Test (NUnit)
       ├── Python Test (pytest)
       │
       └── [main only] Docker Build & Push → GHCR
```
