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
                                [IsLocked] BIT NOT NULL DEFAULT 0, -- NEW FIELD
                                [PackedX] INT NULL,
                                [PackedY] INT NULL,
                                [PackedZ] INT NULL
                            );
                          END
                        -- 5. UPGRADE EXISTING SCHEMA (Add IsLocked if missing)
                        IF COL_LENGTH('dbo.Boxes', 'IsLocked') IS NULL
                        BEGIN
                            ALTER TABLE [dbo].[Boxes] ADD [IsLocked] BIT NOT NULL DEFAULT 0;
                            PRINT '[Provisioning] Upgraded schema: Added IsLocked column to Boxes table.';
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
            Console.WriteLine("[Provisioning] Running massive data seed to stress-test 3D Packing Algorithm...");

            string idempotentSeedSql = @"
            -- =================================================================
            -- VARIABLE DECLARATIONS (Done exactly once)
            -- =================================================================
            DECLARE @RouteId BIGINT;
            DECLARE @Stop1Id BIGINT, @Stop2Id BIGINT, @Stop3Id BIGINT;
            DECLARE @Order1Id BIGINT, @Order2Id BIGINT, @Order3Id BIGINT;
            DECLARE @Counter INT;

            -- =================================================================
            -- 1. TRUCK / ROUTE
            -- =================================================================
            IF NOT EXISTS (SELECT 1 FROM dbo.Routes WHERE TruckIdentifier = 'TRK-001')
            BEGIN
                INSERT INTO dbo.Routes (TruckIdentifier, TruckLengthMm, TruckWidthMm, TruckHeightMm) 
                VALUES ('TRK-001', 5300, 2400, 2600);
                SET @RouteId = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @RouteId = RouteId FROM dbo.Routes WHERE TruckIdentifier = 'TRK-001'; END

            -- =================================================================
            -- 2. STOPS (3 Deliveries: Deepest, Middle, Doors)
            -- =================================================================
            -- Stop 1: Deepest in the truck (Sequence 1)
            IF NOT EXISTS (SELECT 1 FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Mega Warehouse (Deepest)')
            BEGIN
                INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup) VALUES (@RouteId, 1, 'Mega Warehouse (Deepest)', 0);
                SET @Stop1Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Stop1Id = StopId FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Mega Warehouse (Deepest)'; END

            -- Stop 2: Middle of the truck (Sequence 2)
            IF NOT EXISTS (SELECT 1 FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Retail Outlet (Middle)')
            BEGIN
                INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup) VALUES (@RouteId, 2, 'Retail Outlet (Middle)', 0);
                SET @Stop2Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Stop2Id = StopId FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Retail Outlet (Middle)'; END

            -- Stop 3: Near the doors (Sequence 3)
            IF NOT EXISTS (SELECT 1 FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Local Boutique (Doors)')
            BEGIN
                INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup) VALUES (@RouteId, 3, 'Local Boutique (Doors)', 0);
                SET @Stop3Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Stop3Id = StopId FROM dbo.Stops WHERE RouteId = @RouteId AND LocationName = 'Local Boutique (Doors)'; END

            -- =================================================================
            -- 3. ORDERS
            -- =================================================================
            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE CustomerReference = 'ORD-DEEP-01')
            BEGIN
                INSERT INTO dbo.Orders (StopId, CustomerReference) VALUES (@Stop1Id, 'ORD-DEEP-01');
                SET @Order1Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Order1Id = OrderId FROM dbo.Orders WHERE CustomerReference = 'ORD-DEEP-01'; END

            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE CustomerReference = 'ORD-MID-02')
            BEGIN
                INSERT INTO dbo.Orders (StopId, CustomerReference) VALUES (@Stop2Id, 'ORD-MID-02');
                SET @Order2Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Order2Id = OrderId FROM dbo.Orders WHERE CustomerReference = 'ORD-MID-02'; END

            IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE CustomerReference = 'ORD-DOOR-03')
            BEGIN
                INSERT INTO dbo.Orders (StopId, CustomerReference) VALUES (@Stop3Id, 'ORD-DOOR-03');
                SET @Order3Id = SCOPE_IDENTITY();
            END
            ELSE BEGIN SELECT @Order3Id = OrderId FROM dbo.Orders WHERE CustomerReference = 'ORD-DOOR-03'; END

            -- =================================================================
            -- 4. MASSIVE BOX GENERATION
            -- =================================================================
            -- Clear out old boxes first so we have a clean test
            DELETE FROM dbo.Boxes WHERE OrderId IN (@Order1Id, @Order2Id, @Order3Id);

            -- -----------------------------------------------------------------
            -- STOP 1: (Packed deep near the cab)
            -- Creates 8 massive heavy pallets and 20 fragile top-boxes
            -- -----------------------------------------------------------------
            SET @Counter = 1;
            WHILE @Counter <= 8
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@Order1Id, 1200, 1000, 1100, 500.0, 0, 0, 0); 
                SET @Counter = @Counter + 1;
            END

            SET @Counter = 1;
            WHILE @Counter <= 20
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@Order1Id, 600, 400, 300, 5.0, 1, 0, 0); 
                SET @Counter = @Counter + 1;
            END

            -- -----------------------------------------------------------------
            -- STOP 2: (Packed in the middle)
            -- Creates 40 standard sturdy retail boxes and 30 medium fragile boxes
            -- -----------------------------------------------------------------
            SET @Counter = 1;
            WHILE @Counter <= 40
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@Order2Id, 500, 500, 500, 25.0, 0, 0, 0); 
                SET @Counter = @Counter + 1;
            END

            SET @Counter = 1;
            WHILE @Counter <= 30
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@Order2Id, 500, 500, 400, 10.0, 1, 0, 0); 
                SET @Counter = @Counter + 1;
            END

            -- -----------------------------------------------------------------
            -- STOP 3: (Packed near the doors)
            -- Creates 6 large appliances (sturdy) and 15 long awkward fragile items
            -- -----------------------------------------------------------------
            SET @Counter = 1;
            WHILE @Counter <= 6
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@Order3Id, 800, 800, 1500, 150.0, 0, 0, 0); 
                SET @Counter = @Counter + 1;
            END

            SET @Counter = 1;
            WHILE @Counter <= 15
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@Order3Id, 1200, 300, 200, 8.0, 1, 0, 0); 
                SET @Counter = @Counter + 1;
            END
        ";

            using (SqlCommand insertCmd = new(idempotentSeedSql, conn))
            {
                insertCmd.ExecuteNonQuery();
            }
            Console.WriteLine("[Provisioning] Successfully generated 119 boxes across 3 stops.");
        }
    }
}