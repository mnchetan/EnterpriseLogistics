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
            Console.WriteLine("[Provisioning] Seeding 5,750+ records with randomized distribution...");

            string seedSql = @"
    -- 1. CLEANUP
    DELETE FROM dbo.Boxes; DELETE FROM dbo.Orders; DELETE FROM dbo.Stops;

    -- 2. SETUP ROUTE & STOP
    DECLARE @RouteId BIGINT;
    IF NOT EXISTS (SELECT 1 FROM dbo.Routes WHERE TruckIdentifier = 'FLEET-001')
        INSERT INTO dbo.Routes (TruckIdentifier, TruckLengthMm, TruckWidthMm, TruckHeightMm) VALUES ('FLEET-001', 5300, 2400, 2600);
    SELECT TOP 1 @RouteId = RouteId FROM dbo.Routes WHERE TruckIdentifier = 'FLEET-001';

    DECLARE @StopId BIGINT;
    INSERT INTO dbo.Stops (RouteId, SequenceNumber, LocationName, IsPickup) VALUES (@RouteId, 1, 'Hub', 0);
    SET @StopId = SCOPE_IDENTITY();

    -- 3. CREATE ORDERS
    DECLARE @O_Overlap BIGINT, @O_Sturdy BIGINT, @O_Fragile BIGINT, @O_NonOverlap BIGINT;
    INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) VALUES (@StopId, 'OVL', 6, 9); SET @O_Overlap = SCOPE_IDENTITY();
    INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) VALUES (@StopId, 'STU', 1, 10); SET @O_Sturdy = SCOPE_IDENTITY();
    INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) VALUES (@StopId, 'FRG', 1, 10); SET @O_Fragile = SCOPE_IDENTITY();
    INSERT INTO dbo.Orders (StopId, CustomerReference, StartPoint, EndPoint) VALUES (@StopId, 'NON', 1, 3); SET @O_NonOverlap = SCOPE_IDENTITY();

    -- 4. BULK INSERT HELPERS
    DECLARE @i INT = 1;

    -- 1.000 Overlapping (Mix of sizes to force the algorithm to 'fill gaps')
    WHILE @i <= 1000 BEGIN
        INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
        VALUES (@O_Overlap, 300 + (@i % 200), 300 + (@i % 200), 300, 5.0, CASE WHEN @i % 4 = 0 THEN 1 ELSE 0 END, 0, 0);
        SET @i = @i + 1;
    END

    -- 2,000 Sturdy (Same stop, larger dimensions to fill volume)
    SET @i = 1; WHILE @i <= 2000 BEGIN
        INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
        VALUES (@O_Sturdy, 500, 500, 500, 10.0, 0, 0, 0);
        SET @i = @i + 1;
    END

    -- 1,500 Fragile (Small, high-risk items)
    SET @i = 1; WHILE @i <= 1500 BEGIN
        INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
        VALUES (@O_Fragile, 200, 200, 200, 2.0, 1, 0, 0);
        SET @i = @i + 1;
    END

    -- 1,250 Non-overlapping (Varied sizes)
    SET @i = 1; WHILE @i <= 1250 BEGIN
        INSERT INTO dbo.Boxes (OrderId, LengthMm, WidthMm, HeightMm, WeightKg, IsFragile, IsPacked, IsLocked) 
        VALUES (@O_NonOverlap, 400 + (@i % 300), 400, 400, 8.0, 0, 0, 0);
        SET @i = @i + 1;
    END";

            using (SqlCommand cmd = new(seedSql, conn))
            {
                cmd.CommandTimeout = 300;
                cmd.ExecuteNonQuery();
            }
            Console.WriteLine("[Provisioning] Seed Complete. 5,750+ items ready for dispatch.");
        }
    }
}