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

## 🧠 Packing Algorithm — 3D Empty Maximal Space (EMS) Engine

### Overview

The load planning engine is built on a **deterministic 3D Empty Maximal Space (EMS) Partitioning algorithm** designed specifically for enterprise logistics and warehouse operations.

Unlike traditional guillotine-based approaches that continuously fragment available volume into smaller regions, the EMS engine maintains a dynamic representation of the **largest available free spaces** within the vehicle. This enables higher packing density, better utilization of irregular gaps, and improved adaptability to operational constraints.

The engine combines mathematical optimization with real-world loading rules such as delivery sequencing, cargo fragility, weight distribution, and operator-defined manual placements.

---

### Core Capabilities

- Route-aware loading optimization
- Empty Maximal Space (EMS) tracking
- Dynamic space merging and cleanup
- Heavy-base load stabilization
- Fragility-aware placement validation
- Manual placement preservation
- Deterministic and repeatable results
- Real-time 3D visualization support

---

## Operational Loading Strategy

The packing process follows a series of warehouse-oriented rules that balance efficiency, safety, and operational practicality.

| Rule | Purpose |
|--------|---------|
| LIFO Sorting | Ensures cartons are loaded according to delivery sequence requirements. |
| Heavy-Base Foundation | Places heavier cargo first to establish a stable structural base. |
| Fragility Protection | Prevents heavy or rigid cartons from being positioned above fragile items. |
| Manual Override Locking | Preserves operator-defined placements throughout optimization. |
| EMS Space Merging | Reduces fragmentation and maximizes usable cargo volume. |

---

## Algorithm Workflow

### Stage 1 — Operational Pre-Processing

Before automated packing begins, the system identifies cartons that have been manually positioned through the user interface.

These cartons are treated as fixed obstacles and excluded from optimization calculations.

The truck volume is pre-partitioned around these locked placements, ensuring that automated packing respects operational decisions made by warehouse personnel.

#### Objectives

- Preserve operator intent
- Support hybrid manual/automated workflows
- Prevent coordinate overwrites
- Enable incremental load planning

---

### Stage 2 — Shipment Optimization

Remaining cartons are evaluated and sorted using a multi-factor prioritization model.

#### Route Sequencing (LIFO)

Cartons destined for later delivery stops are prioritized to ensure efficient unloading operations.

#### Weight Prioritization

Heavy cartons are loaded earlier to establish a stable load-bearing foundation.

#### Fragility Prioritization

Fragile cargo is intentionally deferred, naturally positioning it closer to the upper layers of the load.

#### Benefits

- Faster unloading
- Improved load stability
- Reduced cargo damage
- More predictable loading behavior

---

### Stage 3 — EMS Search and Placement

For each carton, the engine searches all currently available Empty Maximal Spaces and identifies the most suitable placement location.

#### Spatial Search Priority

Available spaces are evaluated using the following coordinate hierarchy:

1. Lowest Z Coordinate (Floor First)
2. Lowest Y Coordinate (Deepest Position)
3. Lowest X Coordinate (Leftmost Position)

This strategy naturally produces loading patterns that progress:

```text
Bottom → Top
Rear → Front
Left → Right
```

#### Safety Validation

Before finalizing a placement, the engine performs a vertical footprint analysis.

If a non-fragile carton would occupy a position directly above a fragile carton, the candidate space is rejected and the search continues.

This validation mechanism acts as a virtual safety inspection layer within the optimization process.

---

### Stage 4 — EMS Partitioning and Space Consolidation

After a carton is placed, the occupied volume is removed from the selected Empty Maximal Space.

The remaining volume is partitioned using six-directional spatial subtraction.

#### Generated Space Regions

| Direction | Description |
|------------|-------------|
| Top | Space above the carton |
| Bottom | Space below the carton |
| Left | Space on the left side |
| Right | Space on the right side |
| Front | Space toward the vehicle doors |
| Back | Space toward the vehicle bulkhead |

The resulting candidate spaces are then evaluated and normalized.

#### Space Consolidation

To prevent excessive fragmentation, the engine performs a consolidation pass that:

- Removes inscribed spaces
- Eliminates redundant volumes
- Merges adjacent compatible regions
- Reconstructs larger Empty Maximal Spaces

This process enables future cartons to utilize larger continuous volumes rather than being constrained by small fragmented gaps.

---

### Stage 5 — Cleanup and Persistence

After all cartons have been evaluated, the engine performs final optimization cleanup.

#### Tiny Space Removal

Free-space fragments below the configured threshold are discarded.

Typical threshold:

```text
100 mm
```

These regions are considered operationally unusable and would only increase search complexity.

#### State Persistence

The finalized load plan is committed to the database, including:

- Carton coordinates
- Placement status
- Route assignments
- Packing metadata

The persisted data is subsequently consumed by:

- The 3D visualization engine
- Warehouse manifests
- Operational dashboards
- Reporting systems

---

## Technical Characteristics

### Packing Model

```text
3D Empty Maximal Space (EMS)
```

### Space Management

```text
6-Way Spatial Subtraction
```

### Optimization Strategy

```text
Multi-Criteria Heuristic Sorting
```

Factors include:

- Route Sequence
- Weight
- Fragility
- Placement Constraints

### Safety Mechanism

```text
Vertical Raycast-Based Fragility Validation
```

### Obstacle Handling

```text
Dynamic EMS Recalculation Around Locked Objects
```

### Execution Characteristics

- Deterministic output
- Repeatable packing plans
- High packing density
- Low computational overhead
- Enterprise-scale workload support

---

## Key Advantages

### Operational Efficiency

- Route-aware loading plans
- Reduced loading time
- Faster unloading operations

### Cargo Safety

- Stable heavy-base construction
- Fragility-aware placement validation
- Reduced damage risk

### Space Utilization

- Large-space preservation through EMS merging
- Reduced fragmentation
- Higher truck utilization rates

### Enterprise Readiness

- Supports manual overrides
- Database-backed persistence
- Real-time visualization integration
- Scalable architecture for large shipment volumes

---

## Summary

The EMS engine combines advanced spatial optimization techniques with practical warehouse operating rules to produce loading plans that are both mathematically efficient and operationally executable.

By continuously maintaining and consolidating Empty Maximal Spaces, validating fragility constraints, and respecting operator-defined placements, the system achieves high cargo density, load stability, and predictable execution in real-world logistics environments.

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