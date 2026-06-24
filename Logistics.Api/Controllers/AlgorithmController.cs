using Logistics.Api.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using tiny.WebApi.Controllers;
using tiny.WebApi.DatabaseManagers;
using tiny.WebApi.DataObjects;
using tiny.WebApi.IDBContext;
using tiny.WebApi.IRepository;

namespace Logistics.Api.Controllers
{
    public class AlgorithmController : TinyWebApiController
    {
        private readonly ITinyWebApiRepository repository;
        private readonly ILogger<TinyWebApiController> logger;
        private readonly IDBContextSql sqlContext;
        private readonly IDBContextOracle oracleContext;
        private readonly IDBContextNpgsql npgContext;

#pragma warning disable CS8625 
        public AlgorithmController(ITinyWebApiRepository repository, ILogger<TinyWebApiController> logger, IDBContextSql sqlContext, IDBContextOracle oracleContext, IDBContextNpgsql npgContext = null) : base(repository, logger, sqlContext, oracleContext, npgContext)
#pragma warning restore CS8625 
        {
            this.repository = repository;
            this.logger = logger;
            this.sqlContext = sqlContext;
            this.oracleContext = oracleContext;
            this.npgContext = npgContext;
        }

        [HttpPost("[controller]/pack/{routeId}")]
        public IActionResult CalculateTruckPacking(long routeId)
        {
            Global.LogInformation($"[Algorithm Engine] Initialization started for RouteId: {routeId}");

            // 1. THIS IS THE FIX: Change TruckSpace to Truck
            Truck truck = new();
            List<CargoBox> unpackedBoxes = [];

            try
            {
                // =========================================================================
                // 1. LOAD METADATA FROM QUERIES.DEVELOPMENT.JSON
                // =========================================================================
                QuerySpecification routeQuerySpec = Global.GetQuerySpecificationByQueryName("GetTruckDimensionsForRoute");
                QuerySpecification cargoQuerySpec = Global.GetQuerySpecificationByQueryName("GetUnpackedCargoForRoute");
                QuerySpecification updateQuerySpec = Global.GetQuerySpecificationByQueryName("UpdateBoxCoordinates");

                // NEW: Load the reset query
                QuerySpecification resetQuerySpec = Global.GetQuerySpecificationByQueryName("ResetAllBoxesToUnpacked");

                if (routeQuerySpec == null || cargoQuerySpec == null || updateQuerySpec == null || resetQuerySpec == null)
                {
                    Global.LogError("Missing required query specifications in JSON configuration.", null);
                    return StatusCode(500, "Server configuration error.");
                }

                // =========================================================================
                // 2. DATA ACCESS VIA TINY.WEBAPI 
                // =========================================================================

                using DataBaseManagerSql dbManager = new(sqlContext, routeQuerySpec, false);

                List<DatabaseParameters> routeParams =
                [
                    new DatabaseParameters { Name = "RouteId", Value = routeId }
                ];

                // NEW: Wipe the existing load plan before doing the new math
                Global.LogInformation("[Algorithm Engine] Resetting previous load plan for recalculation.");
                dbManager.ExecNonQuery(resetQuerySpec, []);

                // A. Fetch Truck Dimensions
                Global.LogDebug($"[Algorithm Engine] Fetching truck dimensions.");
                System.Data.DataTable truckDt = dbManager.ExecDataTable(routeQuerySpec, routeParams);

                if (truckDt.Rows.Count == 0) return NotFound("Route not found.");

                truck.Length = Convert.ToInt32(truckDt.Rows[0]["TruckLengthMm"]);
                truck.Width = Convert.ToInt32(truckDt.Rows[0]["TruckWidthMm"]);
                truck.Height = Convert.ToInt32(truckDt.Rows[0]["TruckHeightMm"]);

                // B. Fetch Unpacked Cargo (This will now successfully grab all 119 boxes every time)
                Global.LogDebug($"[Algorithm Engine] Fetching unpacked cargo.");
                System.Data.DataTable cargoDt = dbManager.ExecDataTable(cargoQuerySpec, routeParams);

                foreach (DataRow row in cargoDt.Rows)
                {
                    unpackedBoxes.Add(new CargoBox
                    {
                        BoxId = Convert.ToInt64(row["BoxId"]),
                        StopSequence = Convert.ToInt32(row["SequenceNumber"]),
                        Length = Convert.ToInt32(row["LengthMm"]),
                        Width = Convert.ToInt32(row["WidthMm"]),
                        Height = Convert.ToInt32(row["HeightMm"]),
                        Weight = Convert.ToDecimal(row["WeightKg"]),
                        IsFragile = Convert.ToBoolean(row["IsFragile"])
                    });
                }

                // =========================================================================
                // 3. RUN ALGORITHM & UPDATE DB
                // =========================================================================
                if (unpackedBoxes.Count != 0)
                {
                    ExecuteSmartPackingAlgorithm(truck, unpackedBoxes);

                    Global.LogInformation("[Algorithm Engine] Beginning database transaction.");

                    dbManager.BeginTransaction();
                    try
                    {
                        foreach (CargoBox box in unpackedBoxes)
                        {
                            List<DatabaseParameters> updateParams =
                            [
                                new DatabaseParameters { Name = "PackedX", Value = box.PackedX },
                                new DatabaseParameters { Name = "PackedY", Value = box.PackedY },
                                new DatabaseParameters { Name = "PackedZ", Value = box.PackedZ },
                                new DatabaseParameters { Name = "BoxId", Value = box.BoxId }
                            ];
                            dbManager.ExecNonQuery(updateQuerySpec, updateParams);
                        }
                        dbManager.Commit();
                    }
                    catch (Exception dbEx)
                    {
                        dbManager.Rollback();
                        Global.LogError($"[Algorithm Engine] Update transaction failed.", dbEx);
                        throw;
                    }
                }

                return Ok(new { message = $"Successfully calculated load plan.", packedBoxes = unpackedBoxes });
            }
            catch (Exception ex)
            {
                Global.LogError($"[Algorithm Engine] Unhandled exception", ex);
                return StatusCode(500, "An error occurred. Check logs.");
            }
        }
        /// <summary>
        /// Executes the Empty Maximal Space (EMS) 3D bin packing algorithm.
        /// </summary>
        /// <remarks>
        /// This orchestrator method performs three main phases:
        /// 1. Pre-Pass: Identifies manually "locked" boxes and carves out initial free spaces around them.
        /// 2. Sorting: Orders unpacked boxes by Route Sequence (LIFO), Fragility (sturdy first), and Weight (heaviest first).
        /// 3. Packing Pass: Iterates through unpacked boxes, finds the lowest, deepest, and left-most valid space, 
        /// places the box, and dynamically generates new overlapping maximal spaces.
        /// </remarks>
        /// <param name="truck">The dimensions of the container or truck.</param>
        /// <param name="allBoxes">The complete list of boxes, including both locked and unpacked items.</param>
        private static void ExecuteSmartPackingAlgorithm(Truck truck, List<CargoBox> allBoxes)
        {
            // 1. Initialize the truck as one massive empty space
            List<FreeSpace> availableSpaces = [
                new FreeSpace { X = 0, Y = 0, Z = 0, Width = truck.Width, Length = truck.Length, Height = truck.Height }
            ];

            // 2. PRE-PASS: Carve out spaces for "Locked" manual override boxes
            // The algorithm treats these as fixed obstacles before it even starts packing.
            List<CargoBox> lockedBoxes = [.. allBoxes.Where(b => b.IsLocked)];
            foreach (CargoBox locked in lockedBoxes)
            {
                List<FreeSpace> newlyFragmentedSpaces = [];
                foreach (FreeSpace space in availableSpaces)
                {
                    newlyFragmentedSpaces.AddRange(SubtractBoxFromSpace(space, locked));
                }
                availableSpaces = CleanUpFreeSpaces(newlyFragmentedSpaces);
            }

            // 3. PACKING PASS: Pack the remaining boxes
            List<CargoBox> unpackedBoxes = [.. allBoxes.Where(b => !b.IsLocked)
                .OrderByDescending(b => b.StopSequence)
                .ThenBy(b => b.IsFragile ? 0 : 1)
                .ThenByDescending(b => b.Weight)];

            foreach (CargoBox box in unpackedBoxes)
            {
                // Sort spaces: lowest Z (floor), then lowest Y (deepest), then lowest X (left)
                availableSpaces = [.. availableSpaces.OrderBy(s => s.Z).ThenBy(s => s.Y).ThenBy(s => s.X)];

                FreeSpace? bestSpace = null;

                foreach (FreeSpace space in availableSpaces)
                {
                    // 1. Does it physically fit inside the free space boundaries?
                    if (box.Width <= space.Width && box.Length <= space.Length && box.Height <= space.Height)
                    {
                        // 2. NEW: Is it legally safe to put it here?
                        if (IsPlacementSafe(box, space, allBoxes))
                        {
                            bestSpace = space;
                            break; // Space found and validated!
                        }
                    }
                }
                FreeSpace? largestSpace = availableSpaces
    .OrderByDescending(s =>
        (long)s.Width * s.Length * s.Height)
    .FirstOrDefault();

                Global.LogInformation(
                    $"Box={box.BoxId} Fragile={box.IsFragile} " +
                    $"LargestSpace={largestSpace?.Width}x" +
                    $"{largestSpace?.Length}x" +
                    $"{largestSpace?.Height}");
                if (box.IsFragile)
                {
                    Console.WriteLine(
                        $"Fragile Box={box.BoxId} " +
                        $"Spaces={availableSpaces.Count}");
                }
                if (bestSpace == null)
                {
                    Console.WriteLine(
                        $"FAILED Fragile={box.IsFragile} " +
                        $"Box={box.BoxId} " +
                        $"Size={box.Width}x{box.Length}x{box.Height}");
                }
                if (bestSpace == null)
                {
                    Global.LogWarning($"[Algorithm Engine] BoxId {box.BoxId} cannot fit or violates fragility rules.");
                    box.IsPacked = false;
                    continue;
                }

                // Place the box
                box.PackedX = bestSpace.X;
                box.PackedY = bestSpace.Y;
                box.PackedZ = bestSpace.Z;
                box.IsPacked = true;

                // 4. EMS SPLIT: Slicing ALL current free spaces against the newly placed box
                List<FreeSpace> newlyFragmentedSpaces = [];
                foreach (FreeSpace space in availableSpaces)
                {
                    newlyFragmentedSpaces.AddRange(SubtractBoxFromSpace(space, box));
                }

                // 5. GARBAGE COLLECTION: Clean up the overlapped spaces
                availableSpaces = CleanUpFreeSpaces(newlyFragmentedSpaces);
            }
        }

        // --- CORE EMS GEOMETRY ENGINE ---

        /// <summary>
        /// Carves out up to six new overlapping FreeSpaces around a newly placed or locked box.
        /// </summary>
        /// <remarks>
        /// This is the core geometric engine of the EMS algorithm. It checks for 3D intersection between 
        /// a given free space and a physical box. If they intersect, it generates new "maximal" spaces 
        /// representing the empty air extending to the limits of the parent space above, below, left, right, 
        /// in front of, and behind the box.
        /// </remarks>
        /// <param name="space">The original free space being evaluated.</param>
        /// <param name="box">The physical box acting as an obstacle within the space.</param>
        /// <returns>A list of newly generated, overlapping free spaces.</returns>
        private static List<FreeSpace> SubtractBoxFromSpace(FreeSpace space, CargoBox box)
        {
            // 1. Check for 3D Intersection. If the box doesn't physically touch the space, the space survives intact.
            bool intersectsX = box.PackedX < space.X + space.Width && box.PackedX + box.Width > space.X;
            bool intersectsY = box.PackedY < space.Y + space.Length && box.PackedY + box.Length > space.Y;
            bool intersectsZ = box.PackedZ < space.Z + space.Height && box.PackedZ + box.Height > space.Z;

            if (!intersectsX || !intersectsY || !intersectsZ) return [space];

            List<FreeSpace> fragments = [];

            // 2. Generate overlapping "Maximal" spaces around the box

            // Space above the box
            if (box.PackedZ + box.Height < space.Z + space.Height)
            {
                fragments.Add(new FreeSpace
                {
                    X = space.X,
                    Y = space.Y,
                    Z = box.PackedZ + box.Height,
                    Width = space.Width,
                    Length = space.Length,
                    Height = (space.Z + space.Height) - (box.PackedZ + box.Height)
                });
            }
            // Space below the box (crucial if a user 'locked' a floating box via the UI)
            if (box.PackedZ > space.Z)
            {
                fragments.Add(new FreeSpace
                {
                    X = space.X,
                    Y = space.Y,
                    Z = space.Z,
                    Width = space.Width,
                    Length = space.Length,
                    Height = box.PackedZ - space.Z
                });
            }
            // Space to the right of the box
            if (box.PackedX + box.Width < space.X + space.Width)
            {
                fragments.Add(new FreeSpace
                {
                    X = box.PackedX + box.Width,
                    Y = space.Y,
                    Z = space.Z,
                    Width = (space.X + space.Width) - (box.PackedX + box.Width),
                    Length = space.Length,
                    Height = space.Height
                });
            }
            // Space to the left of the box
            if (box.PackedX > space.X)
            {
                fragments.Add(new FreeSpace
                {
                    X = space.X,
                    Y = space.Y,
                    Z = space.Z,
                    Width = box.PackedX - space.X,
                    Length = space.Length,
                    Height = space.Height
                });
            }
            // Space in front of the box
            if (box.PackedY + box.Length < space.Y + space.Length)
            {
                fragments.Add(new FreeSpace
                {
                    X = space.X,
                    Y = box.PackedY + box.Length,
                    Z = space.Z,
                    Width = space.Width,
                    Length = (space.Y + space.Length) - (box.PackedY + box.Length),
                    Height = space.Height
                });
            }
            // Space behind the box
            if (box.PackedY > space.Y)
            {
                fragments.Add(new FreeSpace
                {
                    X = space.X,
                    Y = space.Y,
                    Z = space.Z,
                    Width = space.Width,
                    Length = box.PackedY - space.Y,
                    Height = space.Height
                });
            }

            return fragments;
        }

        /// <summary>
        /// Optimizes spatial data by removing unusable shards and redundant geometry.
        /// </summary>
        /// <remarks>
        /// Acts as a spatial garbage collector to prevent memory bloat and CPU slow-down. 
        /// First, it removes physical spaces that are too small to hold standard padding (e.g., &lt; 100mm). 
        /// Second, it eliminates "inscribed" spaces—where one generated FreeSpace is completely swallowed 
        /// by the boundary of another overlapping FreeSpace.
        /// </remarks>
        /// <param name="spaces">The raw list of fragmented free spaces.</param>
        /// <returns>A filtered list of optimized, distinct free spaces.</returns>
        private static List<FreeSpace> CleanUpFreeSpaces(List<FreeSpace> spaces)
        {
            // 1. Remove impossible shards (Assuming 100mm is your smallest possible padding)
            spaces.RemoveAll(s => s.Width < 100 || s.Length < 100 || s.Height < 100);

            // 2. EMS Garbage Collection: Remove "Inscribed" spaces
            // If Space A is completely swallowed by Space B, Space A is useless and slows down the computer.
            List<FreeSpace> filteredSpaces = [];
            for (int i = 0; i < spaces.Count; i++)
            {
                FreeSpace inner = spaces[i];
                bool isInscribed = false;

                for (int j = 0; j < spaces.Count; j++)
                {
                    if (i == j) continue;
                    FreeSpace outer = spaces[j];

                    if (inner.X >= outer.X &&
                        inner.Y >= outer.Y &&
                        inner.Z >= outer.Z &&
                        inner.X + inner.Width <= outer.X + outer.Width &&
                        inner.Y + inner.Length <= outer.Y + outer.Length &&
                        inner.Z + inner.Height <= outer.Z + outer.Height)
                    {
                        isInscribed = true;
                        break; // No need to check further, it's swallowed up
                    }
                }

                if (!isInscribed)
                {
                    filteredSpaces.Add(inner);
                }
            }

            return filteredSpaces;
        }

        /// <summary>
        /// Validates structural integrity by ensuring sturdy cargo is not placed over fragile cargo.
        /// </summary>
        /// <remarks>
        /// Casts a 2D downward raycast onto the X-Y floor plane. If the footprint of the new sturdy box 
        /// overlaps with the footprint of any previously packed fragile box, it verifies the Z-axis height. 
        /// If the sturdy box is positioned anywhere above the fragile box in that shared column, 
        /// the placement is rejected to prevent crushing.
        /// </remarks>
        /// <param name="newBox">The box attempting to be placed.</param>
        /// <param name="space">The candidate free space boundaries.</param>
        /// <param name="allBoxes">The list of all boxes to check for existing packed fragile items.</param>
        /// <returns>True if the placement violates no fragility constraints; otherwise, false.</returns>
        private static bool IsPlacementSafe(CargoBox newBox, FreeSpace space, List<CargoBox> allBoxes)
        {
            // 1. Fragile boxes can be stacked anywhere, so they always pass.
            if (newBox.IsFragile)
            {
                return true;
            }

            // 2. If it's a sturdy box, find all the fragile boxes we have already packed
            List<CargoBox> packedFragileBoxes = [.. allBoxes.Where(b => b.IsPacked && b.IsFragile)];

            // 3. Calculate the boundaries of where this NEW box is trying to go
            int newLeft = space.X;
            int newRight = space.X + newBox.Width;
            int newFront = space.Y;
            int newBack = space.Y + newBox.Length;
            int newBottom = space.Z;

            // 4. Look underneath it
            foreach (CargoBox? fragBox in packedFragileBoxes)
            {
                int fragLeft = fragBox.PackedX;
                int fragRight = fragBox.PackedX + fragBox.Width;
                int fragFront = fragBox.PackedY;
                int fragBack = fragBox.PackedY + fragBox.Length;

                // Check if their shadows overlap on the floor (X-Y Plane)
                bool overlapX = newLeft < fragRight && newRight > fragLeft;
                bool overlapY = newFront < fragBack && newBack > fragFront;

                // If the shadows overlap AND the sturdy box is being placed higher than the fragile box...
                if (overlapX && overlapY && newBottom >= fragBox.PackedZ)
                {
                    return false; // UNSAFE! The sturdy box would crush the fragile box.
                }
            }

            return true; // The space is safe
        }

        private static void DispatchCargoToTrucks(List<CargoBox> allCargo, List<Truck> trucks)
        {
            // 1. Sort cargo by weight (Largest first) to maximize space usage
            List<CargoBox> sortedCargo = [.. allCargo.OrderByDescending(c => c.Weight)];

            foreach (CargoBox box in sortedCargo)
            {
                Console.WriteLine(
    $"BOX={box.BoxId} " +
    $"Fragile={box.IsFragile} " +
    $"Route={box.StartPoint}-{box.EndPoint}");
                // 2. Identify available trucks for this specific box
                // A truck can take the box if:
                // (CurrentLoad + box.Weight <= 2000kg) AND (The box route fits within the truck's route)
                List<Truck> viableTrucks = [.. trucks.Where(t =>
                    (t.CurrentLoad + box.Weight <= 2000) &&
                    (box.StartPoint >= t.RouteStart && box.EndPoint <= t.RouteEnd)
                ).OrderByDescending(t => t.RemainingCapacity)];
                Console.WriteLine(
    $"BOX={box.BoxId} " +
    $"Fragile={box.IsFragile} " +
    $"Viable={viableTrucks.Count}");
                if (viableTrucks.Count != 0)
                {
                    Truck bestTruck = viableTrucks.First(); // Pick the one with most room
                    bestTruck.Boxes.Add(box);
                    bestTruck.CurrentLoad += box.Weight;
                }
                else
                {
                    Global.LogWarning($"[Dispatcher] Box {box.BoxId} cannot be delivered: No viable truck.");
                    box.IsPacked = false;
                }
            }
        }

        [HttpPost("[controller]/dispatch")]
        public IActionResult DispatchMultipleTrucks()
        {
            // 1. Load data for two trucks
            List<Truck> fleet = [
                new Truck { Id = "TruckA", RouteStart = 1, RouteEnd = 10, Capacity = 2000, Width = 2400, Length = 5300, Height = 2600 },
                new Truck { Id = "TruckB", RouteStart = 5, RouteEnd = 15, Capacity = 2000, Width = 2400, Length = 5300, Height = 2600 }
            ];

            // 2. Clear out the previous run's coordinates
            QuerySpecification resetSpec = Global.GetQuerySpecificationByQueryName("ResetAllBoxesToUnpacked");
            DataBaseManagerSql dbManager = new(sqlContext, resetSpec, false);
            dbManager.ExecNonQuery(resetSpec, []);

            List<CargoBox> allCargo = FetchAllPendingCargo();

            // 3. Run Knapsack Dispatcher
            DispatchCargoToTrucks(allCargo, fleet);
            sqlContext.GetConnection(resetSpec.DatabaseSpecification.ConnectionString, true);
            dbManager = new(sqlContext, resetSpec, false);
            // 4. Run 3D Packing AND Save to Database


            dbManager.BeginTransaction();
            try
            {
                QuerySpecification updateSpec = Global.GetQuerySpecificationByQueryName("UpdateBoxCoordinates");

                foreach (Truck truck in fleet)
                {
                    Console.WriteLine(
    $"Truck={truck.Id} " +
    $"Total={truck.Boxes.Count} " +
    $"Fragile={truck.Boxes.Count(x => x.IsFragile)}");
                    ExecuteSmartPackingAlgorithm(truck, truck.Boxes);
                    foreach (CargoBox? b in truck.Boxes.Where(x => x.IsFragile))
                    {
                        Console.WriteLine(
                            $"FRAGILE {b.BoxId} Packed={b.IsPacked} " +
                            $"({b.PackedX},{b.PackedY},{b.PackedZ})");
                    }

                    foreach (CargoBox box in truck.Boxes)
                    {
                        if (box.IsPacked)
                        {
                            List<DatabaseParameters> updateParams = [
                                new DatabaseParameters { Name = "PackedX", Value = box.PackedX },
                                new DatabaseParameters { Name = "PackedY", Value = box.PackedY },
                                new DatabaseParameters { Name = "PackedZ", Value = box.PackedZ },
                                new DatabaseParameters { Name = "BoxId", Value = box.BoxId }
                            ];
                            dbManager.ExecNonQuery(updateSpec, updateParams);
                        }
                    }
                    Console.WriteLine(
    $"Packed Fragile=" +
    truck.Boxes.Count(x => x.IsFragile && x.IsPacked));
                }
                dbManager.Commit();
            }
            catch (Exception ex)
            {
                dbManager.Rollback();
                Global.LogError("[Algorithm Engine] Fleet Dispatch Save Failed.", ex);
                throw;
            }

            // NEW: After the trucks are packed, find all boxes that were left behind
            // (Either because of weight limits, route mismatch, or physical 3D space rejection)
            List<CargoBox> leftBehindCargo = allCargo.Where(b => !b.IsPacked).ToList();
            foreach (CargoBox? box in allCargo.Where(x => x.IsFragile).Take(20))
            {
                Console.WriteLine(
                    $"Fragile Box={box.BoxId} " +
                    $"Start={box.StartPoint} " +
                    $"End={box.EndPoint}");
            }
            return Ok(new { fleetStatus = fleet, unassignedBoxes = leftBehindCargo });
        }

        private List<CargoBox> FetchAllPendingCargo()
        {
            QuerySpecification cargoSpec = Global.GetQuerySpecificationByQueryName("GetAllPendingCargo");
            using DataBaseManagerSql dbManager = new(sqlContext, cargoSpec, false);

            DataTable dt = dbManager.ExecDataTable(cargoSpec, []);
            List<CargoBox> cargoList = [];

            foreach (DataRow row in dt.Rows)
            {
                cargoList.Add(new CargoBox
                {
                    BoxId = Convert.ToInt64(row["BoxId"]),

                    // Fields from Orders Table
                    StartPoint = row["StartPoint"] != DBNull.Value ? Convert.ToInt32(row["StartPoint"]) : 1,
                    EndPoint = row["EndPoint"] != DBNull.Value ? Convert.ToInt32(row["EndPoint"]) : 10,

                    // Fields from Stops Table
                    StopSequence = row["SequenceNumber"] != DBNull.Value ? Convert.ToInt32(row["SequenceNumber"]) : 1,

                    // Fields from Boxes Table
                    Weight = Convert.ToDecimal(row["WeightKg"]),
                    Height = Convert.ToInt32(row["HeightMm"]),
                    Width = Convert.ToInt32(row["WidthMm"]),
                    Length = Convert.ToInt32(row["LengthMm"]),
                    // Change this line in FetchAllPendingCargo:
                    IsFragile = row["IsFragile"] != DBNull.Value && (Convert.ToBoolean(row["IsFragile"]) || row["IsFragile"].ToString() == "1"),
                    IsLocked = row["IsLocked"] != DBNull.Value && Convert.ToBoolean(row["IsLocked"]),
                    IsPacked = row["IsPacked"] != DBNull.Value && Convert.ToBoolean(row["IsPacked"]),

                    PackedX = row["PackedX"] != DBNull.Value ? Convert.ToInt32(row["PackedX"]) : 0,
                    PackedY = row["PackedY"] != DBNull.Value ? Convert.ToInt32(row["PackedY"]) : 0,
                    PackedZ = row["PackedZ"] != DBNull.Value ? Convert.ToInt32(row["PackedZ"]) : 0
                });
            }
            return cargoList;
        }
    }
}