# Ez.Handball.Backend

Azure Functions ingestion pipeline that pulls Icelandic handball data from the [hsi.is](https://hsi.is) API, archives raw JSON to Azure Blob Storage, and normalizes it into Azure Table Storage. The data will power a fantasy handball game via a separate API project.

## Overview

Six competitions are tracked:

| Competition | Gender |
|------------|--------|
| Olís deild karla | Men |
| Olís deild kvenna | Women |
| Grill 66 deild karla | Men |
| Grill 66 deild kvenna | Women |
| Powerade bikar karla | Men |
| Powerade bikar kvenna | Women |

## Pipeline

A `POST /api/sync` triggers the full chain:

```
FetchMatchListFunction (HTTP)
  → archives tournaments/{id}/matches.json per tournament

FetchMatchDetailsFunction (blob trigger)
  → archives matches/{id}/details.json + players-{teamId}.json per match
  → skips finished matches already archived

ParseMatchFunction (blob trigger)
  → upserts Clubs, Teams, Matches tables

ParsePlayersFunction (blob trigger)
  → upserts Players, PlayerStats tables
```

All writes are upserts — syncing is always safe to re-run. Blobs are the source of truth; tables can be rebuilt from them.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) — local Azure Storage emulator

```bash
npm install -g azurite
npm install -g azure-functions-core-tools@4
```

## Getting Started

```bash
# 1. Start the storage emulator
azurite --silent &

# 2. Start the Functions host
cd Ez.Handball.Ingestion
func start

# 3. Seed the Tournaments table (required before syncing)
curl -X POST "http://localhost:7071/api/seed/tournaments?season=2025"

# 4. Run a full sync
curl -X POST "http://localhost:7071/api/sync"
# Response: {"synced":6,"failed":[]}
```

## Running Tests

```bash
# Start Azurite first (required for storage integration tests)
azurite --silent --location /tmp/azurite-test &

dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj
```

## Project Structure

```
Ez.Handball.Backend/
├── Ez.Handball.Shared/         # ITableEntity domain classes
├── Ez.Handball.Ingestion/      # Azure Functions project
│   ├── Functions/              # HTTP + blob-triggered functions
│   ├── Services/               # HsiApiClient, BlobArchiver, TableWriter
│   └── Models/                 # hsi.is API response DTOs
└── Ez.Handball.Tests/          # Unit + Azurite integration tests
```

## Configuration

`local.settings.json` (not committed):

| Key | Default | Description |
|-----|---------|-------------|
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | Storage connection string |
| `HsiApiBaseUrl` | `https://hsi.is` | hsi.is API base URL |
| `BlobContainerName` | `raw` | Blob container for raw JSON archive |

## Future

- **Ez.Handball.Api** — Clean Architecture REST API in the same solution, referencing `Ez.Handball.Shared` as its domain layer
- **Ez.Handball.Web** — Fantasy league / handball manager game UI (separate repo)
