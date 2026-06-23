# \# Enterprise Logistics 3D Load Planner

# 

# A full-stack enterprise application designed to automatically calculate, optimize, and visualize 3D truck load plans. The system uses a Guillotine Split space partitioning algorithm to handle Last-In-First-Out (LIFO) routing and fragility stacking constraints.

# 

# \## 🏗️ Architecture \& Tech Stack

# 

# \* \*\*Backend:\*\* .NET 10 (C#)

# \* \*\*Framework:\*\* `tiny.webapi` (Configuration-driven SQL data access)

# \* \*\*Database:\*\* SQL Server LocalDB (`LogisticsDB.mdf`)

# \* \*\*Frontend:\*\* Angular 16 (TypeScript)

# \* \*\*3D Visualization:\*\* Three.js / WebGL

# 

# \## 🚀 Getting Started

# 

# \### Prerequisites

# \* .NET 10 SDK

# \* Node.js \& npm

# \* Angular CLI (`npm install -g @angular/cli`)

# \* SQL Server LocalDB

# 

# \### Environment Setup

# 

# The application runs on the following dedicated ports to avoid browser security restrictions (e.g., SIP port blocks):

# \* \*\*REST API:\*\* `http://localhost:5059`

# \* \*\*Angular UI:\*\* `http://localhost:5100`

# 

# \### Running the API

# The backend includes an idempotent `DatabaseProvisioner` that will automatically create the `.mdf` database, build the schema, and seed test data on the first run.

# 

# ```bash

# cd Logistics.Api

# dotnet build

# dotnet run --urls "http://localhost:5059"

# ```

# 

# \### Running the UI

# ```bash

# cd logistics-ui

# npm install

# ng serve --port 5100

# ```

# Navigate to `http://localhost:5100` in your browser to access the dispatch dashboard.

# 

# \---

# 

# \## 📂 Project Structure

# 

# ```text

# EnterpriseLogistics/

# │

# ├── Logistics.Api/                     # .NET 10 Backend

# │   ├── Controllers/

# │   │   └── AlgorithmController.cs     # 3D Packing Engine \& tiny.webapi integration

# │   ├── Models/

# │   │   ├── CargoBox.cs                # Box POCO (Includes 3D Coordinates)

# │   │   ├── TruckSpace.cs              # Route Dimensions POCO

# │   │   └── FreeSpace.cs               # Guillotine Partitioning Model

# │   ├── Infrastructure/

# │   │   └── DatabaseProvisioner.cs     # Automated LocalDB Schema \& Data Seeding

# │   ├── Queries/

# │   │   └── queries.Development.json   # tiny.webapi SQL definitions (No EF needed)

# │   ├── Program.cs                     # API Bootstrapper \& CORS Policies

# │   └── Logistics.Api.csproj

# │

# └── logistics-ui/                      # Angular 16 Frontend

# &#x20;   ├── src/

# &#x20;   │   ├── app/

# &#x20;   │   │   ├── app.component.ts       # Main Dashboard \& Three.js Canvas Engine

# &#x20;   │   │   ├── app.module.ts          # Module Declarations (HttpClientModule)

# &#x20;   │   │   └── logistics.service.ts   # HTTP Service for API communication

# &#x20;   │   ├── index.html

# &#x20;   │   └── styles.css                 # Global Enterprise Dark Theme styles

# &#x20;   ├── angular.json                   # Angular workspace config (Port 5100)

# &#x20;   └── package.json                   # Node dependencies (three.js)

# ```

# 

# \---

# 

# \## 🔌 API Call Details (Endpoints)

# 

# The backend exposes native database interactions via `tiny.webapi` configurations, alongside a custom algorithm engine. All endpoints are hosted on `http://localhost:5059`.

# 

# \### 1. 3D Packing Algorithm Engine

# Calculates the spatial coordinates for all unpacked boxes on a given route, writes the coordinates to the database, and returns the manifest.

# 

# \* \*\*URL:\*\* `http://localhost:5059/tinyWebApi/Algorithm/pack/{routeId}`

# \* \*\*Method:\*\* `POST`

# \* \*\*Response (Success 200):\*\*

# &#x20; ```json

# &#x20; {

# &#x20;   "message": "Successfully calculated load plan.",

# &#x20;   "packedBoxes": \[

# &#x20;     {

# &#x20;       "boxId": 105,

# &#x20;       "stopSequence": 1,

# &#x20;       "length": 1200,

# &#x20;       "width": 1000,

# &#x20;       "height": 1100,

# &#x20;       "weight": 500.0,

# &#x20;       "isFragile": false,

# &#x20;       "isPacked": true,

# &#x20;       "packedX": 0,

# &#x20;       "packedY": 0,

# &#x20;       "packedZ": 0

# &#x20;     }

# &#x20;   ]

# &#x20; }

# &#x20; ```

# 

# \### 2. Native Data Access (tiny.webapi)

# These endpoints read directly from the `queries.Development.json` specifications, requiring no custom C# repository code.

# 

# \*\*Get Truck Dimensions\*\*

# \* \*\*URL:\*\* `http://localhost:5059/tinyWebApi/Get/GetTruckDimensionsForRoute/DataTableText?RouteId={id}`

# \* \*\*Method:\*\* `GET`

# 

# \*\*Get Cargo Roster\*\*

# \* \*\*URL:\*\* `http://localhost:5059/tinyWebApi/Get/GetUnpackedCargoForRoute/DataTableText?RouteId={id}`

# \* \*\*Method:\*\* `GET`

# 

# \*\*Update Coordinates (Internal Use)\*\*

# \* \*\*URL:\*\* `http://localhost:5059/tinyWebApi/Post/UpdateBoxCoordinates/NonQueryText`

# \* \*\*Method:\*\* `POST`

# \* \*\*Body:\*\*

# &#x20; ```json

# &#x20; {

# &#x20;   "BoxId": 1,

# &#x20;   "PackedX": 100,

# &#x20;   "PackedY": 200,

# &#x20;   "PackedZ": 0

# &#x20; }

# &#x20; ```

# 

# \*\*Reset Load Plan (Wipe Coordinates)\*\*

# \* \*\*URL:\*\* `http://localhost:5059/tinyWebApi/Post/ResetAllBoxesToUnpacked/NonQueryText`

# \* \*\*Method:\*\* `POST`

# 

# \---

# 

# \## 🧠 Algorithm Overview

# 

# The `AlgorithmController` utilizes a \*\*3D Guillotine Split Space Partitioning\*\* approach:

# 1\. \*\*LIFO Sorting:\*\* Boxes are sorted in reverse delivery order.

# 2\. \*\*Fragility Sorting:\*\* Heavy, sturdy objects are prioritized to form base layers (`Z = 0`).

# 3\. \*\*Partitioning:\*\* When a box is placed into the truck, the remaining negative space is split into three new `FreeSpace` boundaries (Top, Right, Front).

# 4\. \*\*Iterative Placement:\*\* The engine continually hunts for the lowest and deepest available `FreeSpace` that can physically contain the next box in the queue.

