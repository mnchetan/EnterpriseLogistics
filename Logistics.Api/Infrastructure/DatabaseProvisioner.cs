using Microsoft.Data.SqlClient;

namespace Logistics.Api.Infrastructure
{
    public static class DatabaseProvisioner
    {
        private static void HardResetLocalDbInstance()
        {
            Console.WriteLine("[Provisioning] FATAL LOCALDB CORRUPTION DETECTED. Executing hard reset of MSSQLLocalDB...");

            try
            {
                using System.Diagnostics.Process process = new();
                process.StartInfo.FileName = "cmd.exe";
                // Chain the 4 commands together using the '&' operator
                process.StartInfo.Arguments = "/c sqllocaldb stop MSSQLLocalDB & sqllocaldb delete MSSQLLocalDB & sqllocaldb create MSSQLLocalDB & sqllocaldb start MSSQLLocalDB";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true; // Run silently

                process.Start();
                process.WaitForExit();

                Console.WriteLine("[Provisioning] LocalDB hard reset complete. Clean instance started.");

                // Give the SQL Engine a brief moment to accept connections again
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Provisioning] Failed to execute CLI reset: {ex.Message}");
            }
        }
        public static void EnsureDatabaseAndTableExist()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string mdfFilePath = Path.Combine(appDirectory, "LogisticsDB.mdf");
            string ldfFilePath = Path.Combine(appDirectory, "LogisticsDB_log.ldf");

            string masterConnString = "Data Source=(LocalDB)\\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;";
            string targetConnString = "Data Source=(LocalDB)\\MSSQLLocalDB;Initial Catalog=LogisticsDB;Integrated Security=True;TrustServerCertificate=True;";

            try
            {
                // =================================================================
                // 1. SAFEGUARD: ATTEMPT TO CONNECT AND CLEAN GHOST DBS
                // =================================================================
                try
                {
                    using SqlConnection masterConn = new(masterConnString);
                    masterConn.Open();
                    using SqlCommand checkDbCmd = new("SELECT db_id('LogisticsDB')", masterConn);
                    object result = checkDbCmd.ExecuteScalar();

                    bool dbRegistered = result != null && result != DBNull.Value;
                    bool physicalFilesExist = File.Exists(mdfFilePath);

                    if (dbRegistered && !physicalFilesExist)
                    {
                        Console.WriteLine("[Provisioning] Ghost database detected. Attempting standard cleanup...");
                        using SqlCommand dropCmd = new("DROP DATABASE [LogisticsDB]", masterConn);
                        dropCmd.ExecuteNonQuery();
                    }
                }
                catch (SqlException sqlEx) when (sqlEx.Message.Contains("Operating system error 2") || sqlEx.Message.Contains("File activation failure"))
                {
                    // This catches the exact error you received when SQL Server refuses to DROP the DB.
#if DEBUG
                    HardResetLocalDbInstance();
#else
                    throw new Exception("Database files are missing and the server cannot recover automatically in a non-debug environment.", sqlEx);
#endif
                }

                // =================================================================
                // 2. CREATE DATABASE (Now guaranteed to have a clean slate)
                // =================================================================
                using (SqlConnection masterConn = new(masterConnString))
                {
                    masterConn.Open();
                    using SqlCommand checkDbCmd = new("SELECT db_id('LogisticsDB')", masterConn);
                    if (checkDbCmd.ExecuteScalar() == null || checkDbCmd.ExecuteScalar() == DBNull.Value)
                    {
                        Console.WriteLine("[Provisioning] Creating new LogisticsDB .mdf file...");
                        string createDbSql = $@"
                                CREATE DATABASE [LogisticsDB] 
                                ON PRIMARY (NAME = LogisticsDB_Data, FILENAME = '{mdfFilePath}') 
                                LOG ON (NAME = LogisticsDB_Log, FILENAME = '{ldfFilePath}')";

                        using SqlCommand createDbCmd = new(createDbSql, masterConn);
                        createDbCmd.ExecuteNonQuery();
                    }
                }

                // 3. Ensure Tables Exist
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
                        END
                        -- 5.1 UPGRADE SCHEMA: Add Route Constraints
                        IF COL_LENGTH('dbo.Orders', 'StartPoint') IS NULL
                        BEGIN
                            ALTER TABLE [dbo].[Orders] ADD [StartPoint] INT DEFAULT 1;
                            ALTER TABLE [dbo].[Orders] ADD [EndPoint] INT DEFAULT 10;
                            PRINT '[Provisioning] Upgraded schema: Added Route Constraints.';
                        END
                        ";

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
            Console.WriteLine("[Provisioning] Running Fleet data seed (3 Tons + Routing Overlap)...");

            string idempotentSeedSql = @"
            -- =================================================================
            -- 1. CLEANUP PREVIOUS TEST DATA
            -- =================================================================
            -- Wipe boxes and orders so we start fresh with exactly 3 tons every time
            DELETE FROM dbo.Boxes;
            DELETE FROM dbo.Orders;
            DELETE FROM dbo.Stops;

            DECLARE @RouteId BIGINT;
            DECLARE @StopId BIGINT;

            -- =================================================================
            -- 1.5 ENSURE PARENT ROUTE EXISTS (Fixes Foreign Key Error)
            -- =================================================================
            IF NOT EXISTS (SELECT 1 FROM dbo.Routes WHERE TruckIdentifier = 'FLEET-001')
            BEGIN
                INSERT INTO dbo.Routes (TruckIdentifier, TruckLengthMm, TruckWidthMm, TruckHeightMm) 
                VALUES ('FLEET-001', 5300, 2400, 2600);
                SET @RouteId = SCOPE_IDENTITY();
            END
            ELSE 
            BEGIN 
                SELECT TOP 1 @RouteId = RouteId FROM dbo.Routes WHERE TruckIdentifier = 'FLEET-001'; 
            END
            
            -- Now we can safely insert the Stop using a guaranteed valid RouteId
            INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup) 
            VALUES (@RouteId, 1, 'Main Distribution Hub', 0);
            SET @StopId = SCOPE_IDENTITY();

            -- =================================================================
            -- 2. CREATE ORDERS (Defining Start and End Points)
            -- =================================================================
            DECLARE @OrderA_Id BIGINT; -- Fits ONLY Truck A (Route 1-10)
            DECLARE @OrderB_Id BIGINT; -- Fits ONLY Truck B (Route 5-15)
            DECLARE @OrderOverlap_Id BIGINT; -- Fits BOTH Trucks (Route 6-9)
            DECLARE @OrderReject_Id BIGINT; -- Fits NEITHER Truck (Route 2-14)

            -- Order A: Start 1, End 4 (Exclusive to Truck A)
            INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) 
            VALUES (@StopId, 'ORD-TRUCK-A-ONLY', 1, 4);
            SET @OrderA_Id = SCOPE_IDENTITY();

            -- Order B: Start 11, End 14 (Exclusive to Truck B)
            INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) 
            VALUES (@StopId, 'ORD-TRUCK-B-ONLY', 11, 14);
            SET @OrderB_Id = SCOPE_IDENTITY();

            -- Order Overlap: Start 6, End 9 (The algorithm must balance this)
            INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) 
            VALUES (@StopId, 'ORD-OVERLAP-ZONE', 6, 9);
            SET @OrderOverlap_Id = SCOPE_IDENTITY();

            -- Order Reject: Start 2, End 14 (Too long for either truck)
            INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) 
            VALUES (@StopId, 'ORD-UNROUTABLE', 2, 14);
            SET @OrderReject_Id = SCOPE_IDENTITY();
            -- =================================================================
            -- 3. GENERATE MASSIVE CARGO (Mixed Fragility)
            -- =================================================================
            DECLARE @Counter INT = 0;

            -- -----------------------------------------------------------------
            -- ZONE A (Truck A Only): 20 Sturdy, 12 Fragile
            -- -----------------------------------------------------------------
            SET @Counter = 1;
            WHILE @Counter <= 20
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@OrderA_Id, 500, 500, 500, 30.0, 0, 0, 0);  -- STURDY
                SET @Counter = @Counter + 1;
            END

            SET @Counter = 1;
            WHILE @Counter <= 20
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@OrderA_Id, 500, 500, 400, 10.0, 1, 0, 0);  -- FRAGILE
                SET @Counter = @Counter + 1;
            END

            -- -----------------------------------------------------------------
            -- ZONE B (Truck B Only): 20 Sturdy, 12 Fragile
            -- -----------------------------------------------------------------
            SET @Counter = 1;
            WHILE @Counter <= 20
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@OrderB_Id, 500, 500, 500, 30.0, 0, 0, 0);  -- STURDY
                SET @Counter = @Counter + 1;
            END

            SET @Counter = 1;
            WHILE @Counter <= 12
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@OrderB_Id, 500, 500, 400, 10.0, 1, 0, 0);  -- FRAGILE
                SET @Counter = @Counter + 1;
            END

            -- -----------------------------------------------------------------
            -- ZONE C (Overlap Balancing): 20 Heavy Pallets, 10 Awkward Fragile
            -- -----------------------------------------------------------------
            SET @Counter = 1;
            WHILE @Counter <= 20
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@OrderOverlap_Id, 800, 600, 600, 60.0, 0, 0, 0); -- STURDY
                SET @Counter = @Counter + 1;
            END

            SET @Counter = 1;
            WHILE @Counter <= 10
            BEGIN
                INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
                VALUES (@OrderOverlap_Id, 1200, 300, 300, 15.0, 1, 0, 0); -- LONG FRAGILE
                SET @Counter = @Counter + 1;
            END

            -- -----------------------------------------------------------------
            -- ZONE D (Unroutable Reject): 1 massive box
            -- -----------------------------------------------------------------
            INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
            VALUES (@OrderReject_Id, 2000, 1000, 1000, 100.0, 1, 0, 0);
            ";

            using (SqlCommand insertCmd = new(idempotentSeedSql, conn))
            {
                insertCmd.ExecuteNonQuery();
            }
            Console.WriteLine("[Provisioning] Successfully generated 3,200 kg of Fleet Cargo data.");
        }
    }
}