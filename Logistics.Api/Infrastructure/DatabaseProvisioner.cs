using Microsoft.Data.SqlClient;

namespace Logistics.Api.Infrastructure
{
    public static class DatabaseProvisioner
    {
        public static void EnsureDatabaseAndTableExist()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string mdfFilePath = Path.Combine(appDirectory, "LogisticsDB.mdf");
            string ldfFilePath = Path.Combine(appDirectory, "LogisticsDB_log.ldf");

            string masterConnString = "Data Source=(LocalDB)\\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;";
            string targetConnString = "Data Source=(LocalDB)\\MSSQLLocalDB;Initial Catalog=LogisticsDB;Integrated Security=True;TrustServerCertificate=True;";

            try
            {
                // 1. Ensure Database Exists
                using (SqlConnection masterConn = new(masterConnString))
                {
                    masterConn.Open();
                    using SqlCommand checkDbCmd = new("SELECT db_id('LogisticsDB')", masterConn);
                    object result = checkDbCmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                    {
                        Console.WriteLine("[Provisioning] Database not found. Creating new .mdf file...");
                        string createDbSql = $@"
                                CREATE DATABASE [LogisticsDB] 
                                ON PRIMARY (NAME = LogisticsDB_Data, FILENAME = '{mdfFilePath}') 
                                LOG ON (NAME = LogisticsDB_Log, FILENAME = '{ldfFilePath}')";

                        using SqlCommand createDbCmd = new(createDbSql, masterConn);
                        createDbCmd.ExecuteNonQuery();
                    }
                }

                // 2. Ensure Tables Exist
                using SqlConnection targetConn = new(targetConnString);
                targetConn.Open();

                string createTablesSql = @"
                        -- 1. ROUTES TABLE
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Routes' and xtype='U')
                        BEGIN
                            CREATE TABLE [dbo].[Routes] (
                                [RouteId] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                [TruckIdentifier] VARCHAR(50) NOT NULL,
                                [TruckLengthMm] INT NOT NULL,
                                [TruckWidthMm] INT NOT NULL,
                                [TruckHeightMm] INT NOT NULL,
                                [CreatedDate] DATETIME NOT NULL DEFAULT GETDATE()
                            );
                        END

                        -- 2. STOPS TABLE
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Stops' and xtype='U')
                        BEGIN
                            CREATE TABLE [dbo].[Stops] (
                                [StopId] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                [RouteId] BIGINT NOT NULL FOREIGN KEY REFERENCES [Routes](RouteId),
                                [SequenceNumber] INT NOT NULL,
                                [LocationName] VARCHAR(100) NOT NULL,
                                [IsPickup] BIT NOT NULL -- 0 for Delivery, 1 for Mid-Route Pickup
                            );
                        END

                        -- 3. ORDERS TABLE
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Orders' and xtype='U')
                        BEGIN
                            CREATE TABLE [dbo].[Orders] (
                                [OrderId] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                [StopId] BIGINT NOT NULL FOREIGN KEY REFERENCES [Stops](StopId),
                                [CustomerReference] VARCHAR(50) NOT NULL
                            );
                        END

                        -- 4. BOXES TABLE (Holds the 3D Coordinates)
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Boxes' and xtype='U')
                        BEGIN
                            CREATE TABLE [dbo].[Boxes] (
                                [BoxId] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                [OrderId] BIGINT NOT NULL FOREIGN KEY REFERENCES [Orders](OrderId),
                                [LengthMm] INT NOT NULL,
                                [WidthMm] INT NOT NULL,
                                [HeightMm] INT NOT NULL,
                                [WeightKg] DECIMAL(10,2) NOT NULL,
                                [IsFragile] BIT NOT NULL,
                                
                                -- The Algorithm Output Fields
                                [IsPacked] BIT NOT NULL DEFAULT 0,
                                [PackedX] INT NULL,
                                [PackedY] INT NULL,
                                [PackedZ] INT NULL
                            );
                        END";

                using (SqlCommand createTableCmd = new(createTablesSql, targetConn))
                {
                    createTableCmd.ExecuteNonQuery();
                }
                Console.WriteLine("[Provisioning] Schema validation complete. LocalDB is ready.");
                SeedTestData(targetConnString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fatal Provisioning Error] {ex.Message}");
                throw; // Re-throw to prevent the app from starting in a broken state
            }
        }

        private static void SeedTestData(string connectionString)
        {
            using SqlConnection conn = new(connectionString);
            conn.Open();
            Console.WriteLine("[Provisioning] Running idempotent data seed...");

            string idempotentSeedSql = @"
            -- =================================================================
            -- 1. TRUCK / ROUTE (Idempotent Insert)
            -- =================================================================
            DECLARE @RouteId BIGINT;
            
            IF NOT EXISTS (SELECT 1 FROM dbo.Routes WHERE TruckIdentifier = 'TRK-001')
            BEGIN
                INSERT INTO dbo.Routes (TruckIdentifier, TruckLengthMm, TruckWidthMm, TruckHeightMm) 
                VALUES ('TRK-001', 5300, 2400, 2600);
                SET @RouteId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                SELECT @RouteId = RouteId FROM dbo.Routes WHERE TruckIdentifier = 'TRK-001';
            END

            -- =================================================================
            -- 2. STOPS (Idempotent Insert)
            -- =================================================================
            DECLARE @Stop1Id BIGINT, @Stop2Id BIGINT;

            -- Stop 1
            IF NOT EXISTS (SELECT 1 FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Warehouse A')
            BEGIN
                INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup)
                VALUES (@RouteId, 1, 'Warehouse A', 0);
                SET @Stop1Id = SCOPE_IDENTITY();
            END
            ELSE 
            BEGIN 
                SELECT @Stop1Id = StopId FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Warehouse A'; 
            END

            -- Stop 2
            IF NOT EXISTS (SELECT 1 FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Store B')
            BEGIN
                INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup)
                VALUES (@RouteId, 2, 'Store B', 0);
                SET @Stop2Id = SCOPE_IDENTITY();
            END
            ELSE 
            BEGIN 
                SELECT @Stop2Id = StopId FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Store B'; 
            END

            -- =================================================================
            -- 3. ORDERS (Idempotent Insert)
            -- =================================================================
            DECLARE @Order1Id BIGINT, @Order2Id BIGINT;

            -- Order 1
            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE CustomerReference = 'ORD-001')
            BEGIN
                INSERT INTO dbo.Orders (StopId, CustomerReference) VALUES (@Stop1Id, 'ORD-001');
                SET @Order1Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Order1Id = OrderId FROM dbo.Orders WHERE CustomerReference = 'ORD-001'; END

            -- Order 2
            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE CustomerReference = 'ORD-002')
            BEGIN
                INSERT INTO dbo.Orders (StopId, CustomerReference) VALUES (@Stop2Id, 'ORD-002');
                SET @Order2Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Order2Id = OrderId FROM dbo.Orders WHERE CustomerReference = 'ORD-002'; END

            -- =================================================================
            -- 4. BOXES (Idempotent Insert)
            -- =================================================================
            -- For boxes, we check against their dimensions and order to prevent duplicates

            -- Boxes for Order 1 (Stop 1)
            IF NOT EXISTS (SELECT 1 FROM dbo.Boxes WHERE OrderId = @Order1Id AND WeightKg = 15.0 AND IsFragile = 0)
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked) 
                VALUES (@Order1Id, 600, 400, 400, 15.0, 0, 0);
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Boxes WHERE OrderId = @Order1Id AND WeightKg = 5.0 AND IsFragile = 1)
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked) 
                VALUES (@Order1Id, 600, 400, 400, 5.0, 1, 0);
            END

            -- Box for Order 2 (Stop 2)
            IF NOT EXISTS (SELECT 1 FROM dbo.Boxes WHERE OrderId = @Order2Id AND WeightKg = 50.0 AND IsFragile = 0)
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked) 
                VALUES (@Order2Id, 1200, 1000, 800, 50.0, 0, 0);
            END
        ";

            using (SqlCommand insertCmd = new(idempotentSeedSql, conn))
            {
                insertCmd.ExecuteNonQuery();
            }
            Console.WriteLine("[Provisioning] Data seed validated. Database is ready.");
        }
    }
}