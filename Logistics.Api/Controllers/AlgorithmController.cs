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

                if (routeQuerySpec == null || cargoQuerySpec == null || updateQuerySpec == null)
                {
                    Global.LogError("Missing required query specifications in JSON configuration.", null);
                    return StatusCode(500, "Server configuration error.");
                }

                // =========================================================================
                // 2. DATA ACCESS VIA TINY.WEBAPI 
                // =========================================================================

                // FIX: Use the natively injected 'this.sqlContext' and set AutoDispose to FALSE!
                using DataBaseManagerSql dbManager = new(sqlContext, routeQuerySpec, false);

                List<DatabaseParameters> routeParams =
                [
                    new DatabaseParameters { Name = "RouteId", Value = routeId }
                ];

                // A. Fetch Truck Dimensions
                Global.LogDebug($"[Algorithm Engine] Fetching truck dimensions.");
                DataTable truckDt = dbManager.ExecDataTable(routeQuerySpec, routeParams);

                if (truckDt.Rows.Count == 0) return NotFound("Route not found.");

                truck.Length = Convert.ToInt32(truckDt.Rows[0]["TruckLengthMm"]);
                truck.Width = Convert.ToInt32(truckDt.Rows[0]["TruckWidthMm"]);
                truck.Height = Convert.ToInt32(truckDt.Rows[0]["TruckHeightMm"]);

                // B. Fetch Unpacked Cargo
                Global.LogDebug($"[Algorithm Engine] Fetching unpacked cargo.");
                DataTable cargoDt = dbManager.ExecDataTable(cargoQuerySpec, routeParams);

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

                    // The transaction will safely lock the connection until Commit or Rollback
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
                            // Execute using the Update JSON Specification
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

                // The 'using dbManager' block ending here will now safely call Dispose() and close the connection
                return Ok(new { message = $"Successfully calculated load plan.", packedBoxes = unpackedBoxes });
            }
            catch (Exception ex)
            {
                Global.LogError($"[Algorithm Engine] Unhandled exception", ex);
                return StatusCode(500, "An error occurred. Check logs.");
            }
        }

        private static void ExecutePackingAlgorithm(TruckSpace truck, List<CargoBox> boxes)
        {
            // 1. Strict Sorting: 
            // - Reverse Route Order (LIFO)
            // - Heavy sturdy boxes first (form the base)
            // - Fragile / light boxes last (go on top)
            List<CargoBox> sortedBoxes = [.. boxes
        .OrderByDescending(b => b.StopSequence)
        .ThenBy(b => b.IsFragile ? 1 : 0)
        .ThenByDescending(b => b.Weight)];

            // 2. Initialize the truck as one massive empty space
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
                // 3. Find the "Best" space for this box. 
                // We sort spaces to always favor Bottom (Z), then Deepest (Y), then Left (X)
                availableSpaces = [.. availableSpaces
            .OrderBy(s => s.Z)
            .ThenBy(s => s.Y)
            .ThenBy(s => s.X)];

                FreeSpace? bestSpace = null;
                int spaceIndex = -1;

                // Hunt for a space that can fully contain the box dimensions
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

                // 4. Place the box in the bottom-left-back corner of the chosen space
                box.PackedX = bestSpace.X;
                box.PackedY = bestSpace.Y;
                box.PackedZ = bestSpace.Z;
                box.IsPacked = true;

                // 5. Guillotine Split the remaining space around the box
                availableSpaces.RemoveAt(spaceIndex);

                // Space A: Top of the box
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

                // Space B: Right of the box
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

                // Space C: Front of the box
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

                // 6. Optimization: Clean up tiny unusable shards of space to save memory/CPU
                // (Assuming anything smaller than 100mm on any side is unusable padding)
                availableSpaces.RemoveAll(s => s.Width < 100 || s.Length < 100 || s.Height < 100);
            }
        }
    }
}