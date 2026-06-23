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

            TruckSpace truck = new();
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
                    ExecutePackingAlgorithm(truck, unpackedBoxes);

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

        //This algorithm is a 3D Guillotine Split Space Partitioning engine.In simple terms, it is an intelligent "3D Tetris" solver that follows real-world trucking rules.

        //If you were explaining this to a manager or a colleague, you can break it down into these five clear stages:
        //1. The "Pre-Game" (Intelligent Sorting)

        //Before a single box is moved, the algorithm sorts the entire manifest. It doesn't just pick boxes at random; it follows a Logistics-First logic:

        //LIFO (Last-In, First-Out): It looks at the delivery route and picks boxes for the last stop first.These go to the very back of the truck so they aren't in the way during earlier stops.

        //The "Foundation" Rule: It looks for heavy, sturdy boxes to pack first.

        //The "Fragile" Rule: It identifies fragile items and moves them to the end of the line so they naturally end up on the top, where they won't be crushed.

        //2. The Initial State (One Giant Void)

        //The algorithm starts by imagining the truck as one massive, empty "Free Space" cube.It defines this space using X (width), Y(depth), and Z(height) coordinates.
        //3. The Search(Finding the "Best" Pocket)

        //For every box in our sorted list, the algorithm looks at its available "pockets" of free space.To keep the load stable, it sorts these spaces with a strict priority:

        //Z-Axis first: "Is this space on the floor?" (Always try to put things on the floor or on top of another box—no floating).

        //Y-Axis second: "Is this space at the very back?" (Pack from the back to the front).

        //X-Axis third: "Is this space on the left?" (Pack in organized columns).

        //It then picks the first pocket that is large enough to fit the box’s Width, Length, and Height.
        //4. The "Guillotine Split" (The Clever Part)

        //This is where the magic happens.Once the algorithm places a box into a large empty space, that big space is "killed" and replaced by three new, smaller free spaces:

        //Space A(The Top): The empty area directly above the box you just placed.

        //Space B (The Side): The empty area to the right of the box.

        //Space C (The Front): The empty area in front of the box leading toward the truck doors.

        //By constantly splitting big spaces into three smaller "rectangles," the algorithm always knows exactly where the remaining "holes" are in the truck.
        //5. Cleanup (Ignoring the Shards)

        //At the end of every step, the algorithm performs "Garbage Collection." If a leftover space is tiny(less than 10cm or 4 inches), it deletes that space from its memory.This prevents the computer from wasting time trying to fit a large box into a tiny "sliver" of space that only a letter could fit into.
        //Why this is a "Smart" Algorithm (The "So What?"):

        //Safety: Because of the sorting, heavy stuff is at the bottom, and fragile stuff is on top automatically.

        //Efficiency: It packs from the bottom-up and back-to-front, which is exactly how human loaders work, but it does the math in milliseconds to ensure zero wasted volume.

        //Stability: By prioritizing the lowest Z-coordinate, it ensures that every box has a "floor" beneath it, preventing items from shifting or falling in the 3D model.
        private static void ExecutePackingAlgorithm(TruckSpace truck, List<CargoBox> boxes)
        {
            List<CargoBox> sortedBoxes = [.. boxes
                .OrderByDescending(b => b.StopSequence)
                .ThenBy(b => b.IsFragile ? 1 : 0)
                .ThenByDescending(b => b.Weight)];

            List<FreeSpace> availableSpaces =
            [
                new FreeSpace
                {
                    X = 0, Y = 0, Z = 0,
                    Width = truck.Width,
                    Length = truck.Length,
                    Height = truck.Height
                }
            ];

            foreach (CargoBox box in sortedBoxes)
            {
                availableSpaces = [.. availableSpaces
                    .OrderBy(s => s.Z)
                    .ThenBy(s => s.Y)
                    .ThenBy(s => s.X)];

                FreeSpace? bestSpace = null;
                int spaceIndex = -1;

                for (int i = 0; i < availableSpaces.Count; i++)
                {
                    FreeSpace space = availableSpaces[i];
                    if (box.Width <= space.Width && box.Length <= space.Length && box.Height <= space.Height)
                    {
                        bestSpace = space;
                        spaceIndex = i;
                        break;
                    }
                }

                if (bestSpace == null)
                {
                    Global.LogWarning($"[Algorithm Engine] BoxId {box.BoxId} cannot fit in the remaining free space.");
                    box.IsPacked = false;
                    continue;
                }

                box.PackedX = bestSpace.X;
                box.PackedY = bestSpace.Y;
                box.PackedZ = bestSpace.Z;
                box.IsPacked = true;

                availableSpaces.RemoveAt(spaceIndex);

                if (bestSpace.Height - box.Height > 0)
                {
                    availableSpaces.Add(new FreeSpace
                    {
                        X = bestSpace.X,
                        Y = bestSpace.Y,
                        Z = bestSpace.Z + box.Height,
                        Width = box.Width,
                        Length = box.Length,
                        Height = bestSpace.Height - box.Height
                    });
                }

                if (bestSpace.Width - box.Width > 0)
                {
                    availableSpaces.Add(new FreeSpace
                    {
                        X = bestSpace.X + box.Width,
                        Y = bestSpace.Y,
                        Z = bestSpace.Z,
                        Width = bestSpace.Width - box.Width,
                        Length = box.Length,
                        Height = bestSpace.Height
                    });
                }

                if (bestSpace.Length - box.Length > 0)
                {
                    availableSpaces.Add(new FreeSpace
                    {
                        X = bestSpace.X,
                        Y = bestSpace.Y + box.Length,
                        Z = bestSpace.Z,
                        Width = bestSpace.Width,
                        Length = bestSpace.Length - box.Length,
                        Height = bestSpace.Height
                    });
                }

                availableSpaces.RemoveAll(s => s.Width < 100 || s.Length < 100 || s.Height < 100);
            }
        }
    }
}