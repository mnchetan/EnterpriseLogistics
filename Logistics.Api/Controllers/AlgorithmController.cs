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
            List<CargoBox> sortedBoxes = [.. boxes
                .OrderByDescending(b => b.StopSequence)
                .ThenBy(b => b.IsFragile ? 1 : 0)
                .ThenByDescending(b => b.Weight)];

            int currentX = 0, currentY = 0, currentZ = 0;
            int currentRowMaxLength = 0;

            foreach (CargoBox box in sortedBoxes)
            {
                if (currentX + box.Width > truck.Width)
                {
                    currentX = 0;
                    currentY += currentRowMaxLength;
                    currentRowMaxLength = 0;
                }

                if (currentY + box.Length > truck.Length) continue;

                box.PackedX = currentX;
                box.PackedY = currentY;
                box.PackedZ = currentZ;

                currentX += box.Width;
                if (box.Length > currentRowMaxLength) currentRowMaxLength = box.Length;
            }
        }
    }
}