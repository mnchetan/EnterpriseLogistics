namespace Logistics.Api.Models
{
    public class TruckSpace
    {
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class CargoBox
    {
        public long BoxId { get; set; }
        public int StopSequence { get; set; }
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public decimal Weight { get; set; }
        public bool IsFragile { get; set; }

        // Add this missing property to fix the CS1061 error
        public bool IsPacked { get; set; }

        public int PackedX { get; set; }
        public int PackedY { get; set; }
        public int PackedZ { get; set; }
        public bool IsLocked { get; internal set; }
    }

    public class FreeSpace
    {
        public int X { get; set; }
        public int Y { get; set; } // Depth into the truck
        public int Z { get; set; } // Vertical height
        public int Width { get; set; }
        public int Length { get; set; }
        public int Height { get; set; }

        // Volume helps us prioritize which spaces to fill first
        public long Volume => (long)Width * Length * Height;
    }
}