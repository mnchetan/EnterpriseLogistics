# Enterprise Logistics 3D Load Planner

A full-stack logistics optimization platform that automatically calculates, optimizes, and visualizes truck loading plans in three dimensions.

The application combines route-aware loading logic, cargo stability rules, fragility constraints, and a high-performance 3D Guillotine Space Partitioning engine to generate practical loading plans that can be executed in real warehouse operations.

---

## Overview

Traditional load planning is often performed manually, resulting in:

- Poor space utilization
- Inefficient unloading sequences
- Cargo instability
- Increased risk of damage
- Inconsistent loading decisions

This system automates the entire process by calculating optimal carton placement coordinates while respecting operational constraints such as:

- Last-In-First-Out (LIFO) delivery routing
- Fragile item protection
- Weight distribution
- Load stability
- Vehicle dimensional limits

The resulting load plan can be visualized interactively in a 3D environment.

---

## Key Features

### Intelligent Load Planning

- Automated carton placement
- Route-aware loading strategy
- LIFO unloading optimization
- Support for mixed carton dimensions
- Real-time space utilization calculation

### Cargo Protection

- Heavy cartons positioned first
- Fragile cartons stacked later
- Stable load foundation generation
- Floating placement prevention

### 3D Visualization

- Interactive truck visualization
- Real-time cargo rendering
- Camera controls and zoom
- Placement coordinate inspection

### Enterprise Architecture

- Configuration-driven backend
- Database-first design
- Automated database provisioning
- REST API integration
- Scalable service architecture

---

## Technology Stack

| Layer | Technology |
|---------|---------|
| Backend | .NET 10 |
| Language | C# |
| Data Access | tiny.webapi |
| Database | SQL Server LocalDB |
| Frontend | Angular 16 |
| UI Language | TypeScript |
| Visualization | Three.js |
| Rendering Engine | WebGL |

---

## System Architecture

```text
+------------------------------------------------------+
|                    Angular UI                        |
|                 (Three.js Viewer)                    |
+--------------------------+---------------------------+
                           |
                           v
+------------------------------------------------------+
|                    REST API                          |
|                 .NET 10 Backend                      |
+--------------------------+---------------------------+
                           |
                           v
+------------------------------------------------------+
|           3D Packing Algorithm Engine                |
|      Guillotine Split Space Partitioning             |
+--------------------------+---------------------------+
                           |
                           v
+------------------------------------------------------+
|                tiny.webapi Layer                     |
|       Configuration Driven Data Access               |
+--------------------------+---------------------------+
                           |
                           v
+------------------------------------------------------+
|                SQL Server LocalDB                    |
+------------------------------------------------------+
```

---

## Getting Started

### Prerequisites

Install the following software:

- .NET 10 SDK
- Node.js
- npm
- Angular CLI
- SQL Server LocalDB

Install Angular CLI:

```bash
npm install -g @angular/cli
```

---

## Default Ports

| Service | Address |
|----------|----------|
| API | http://localhost:5059 |
| UI | http://localhost:5100 |

---

## Running the Backend

The backend automatically creates and initializes the database on first execution.

```bash
cd Logistics.Api

dotnet restore
dotnet build

dotnet run --urls "http://localhost:5059"
```

### Automatic Database Setup

On first startup the application:

1. Creates the LocalDB database
2. Builds required tables
3. Creates indexes
4. Seeds sample data
5. Verifies connectivity

No manual SQL execution is required.

---

## Running the Frontend

```bash
cd logistics-ui

npm install

ng serve --port 5100
```

Open:

```text
http://localhost:5100
```

---

## Project Structure

```text
EnterpriseLogistics
│
├── Logistics.Api
│   │
│   ├── Controllers
│   │   └── AlgorithmController.cs
│   │
│   ├── Infrastructure
│   │   └── DatabaseProvisioner.cs
│   │
│   ├── Models
│   │   ├── CargoBox.cs
│   │   ├── TruckSpace.cs
│   │   └── FreeSpace.cs
│   │
│   ├── Queries
│   │   └── queries.Development.json
│   │
│   ├── Program.cs
│   └── Logistics.Api.csproj
│
└── logistics-ui
    │
    ├── src
    │   ├── app
    │   │   ├── app.component.ts
    │   │   ├── app.module.ts
    │   │   └── logistics.service.ts
    │   │
    │   ├── index.html
    │   └── styles.css
    │
    ├── angular.json
    └── package.json
```

---

# API Endpoints

Base URL:

```text
http://localhost:5059
```

---

## Generate Load Plan

Calculates carton placement coordinates and stores the resulting plan.

### Request

```http
POST /tinyWebApi/Algorithm/pack/{routeId}
```

### Response

```json
{
  "message": "Successfully calculated load plan.",
  "packedBoxes": [
    {
      "boxId": 105,
      "stopSequence": 1,
      "packedX": 0,
      "packedY": 0,
      "packedZ": 0,
      "isPacked": true
    }
  ]
}
```

---

## Get Vehicle Dimensions

```http
GET /tinyWebApi/Get/GetTruckDimensionsForRoute/DataTableText
```

### Parameters

| Parameter | Type |
|------------|--------|
| RouteId | Integer |

---

## Get Route Cargo

```http
GET /tinyWebApi/Get/GetUnpackedCargoForRoute/DataTableText
```

### Parameters

| Parameter | Type |
|------------|--------|
| RouteId | Integer |

---

## Update Carton Coordinates

```http
POST /tinyWebApi/Post/UpdateBoxCoordinates/NonQueryText
```

### Request Body

```json
{
  "BoxId": 1,
  "PackedX": 100,
  "PackedY": 200,
  "PackedZ": 0
}
```

---

## Reset Load Plan

```http
POST /tinyWebApi/Post/ResetAllBoxesToUnpacked/NonQueryText
```

---

# Packing Algorithm

## What Is It?

The packing engine is a deterministic 3D Guillotine Split Space Partitioning algorithm.

A useful way to think about it is:

> A warehouse-optimized version of 3D Tetris that follows real transportation and loading rules.

Unlike generic packing algorithms, the engine understands:

- Delivery sequence
- Carton fragility
- Load stability
- Space optimization

---

## Loading Strategy

| Rule | Purpose |
|--------|---------|
| LIFO Sorting | Delivery optimization |
| Heavy First | Stable foundation |
| Fragile Last | Damage prevention |
| Lowest Z First | Structural support |
| Back To Front | Efficient unloading |
| Left To Right | Organized placement |
| Guillotine Split | Space tracking |
| Cleanup Pass | Performance optimization |

---

## Algorithm Workflow

### Stage 1 — Shipment Optimization

Before loading begins:

- Cargo is analyzed
- Delivery sequence is evaluated
- Heavy cartons are prioritized
- Fragile cartons are deferred

### Stage 2 — Create Initial Free Space

The truck is represented as one large empty volume.

```text
+----------------------+
|                      |
|     Empty Truck      |
|                      |
+----------------------+
```

### Stage 3 — Find Best Position

For every carton:

1. Search all free spaces
2. Evaluate fit
3. Select optimal location
4. Place carton

Priority order:

1. Lowest Z
2. Lowest Y
3. Lowest X

### Stage 4 — Guillotine Split

After placement:

```text
+----------------------+
|      Space A         |
+----------+-----------+
| Carton   | Space B   |
+----------+-----------+
|      Space C         |
+----------------------+
```

The original volume is replaced with:

- Top Space
- Side Space
- Front Space

### Stage 5 — Space Cleanup

Unused fragments are removed.

Examples:

- Narrow gaps
- Tiny slivers
- Non-usable volumes

This improves both packing speed and placement quality.

---

## Benefits

### Operational

- Faster loading
- Faster unloading
- Reduced manual planning

### Financial

- Improved vehicle utilization
- Reduced transportation cost
- Lower damage rates

### Technical

- Deterministic results
- Millisecond execution time
- Scalable architecture
- Enterprise-ready deployment

---

## Future Enhancements

- Multi-truck optimization
- Weight balancing analysis
- AI-assisted packing recommendations
- Multi-depot route planning
- Real-time warehouse integration
- Live WMS connectivity

---

## License

Internal enterprise demonstration project.

All rights reserved.