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
        public int StartPoint { get; set; }
        public int EndPoint { get; set; }
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

    public class Truck
    {
        public string Id { get; set; } = string.Empty;
        public int RouteStart { get; set; }
        public int RouteEnd { get; set; }
        public decimal Capacity { get; set; }
        public decimal CurrentLoad { get; set; }
        public decimal RemainingCapacity => Capacity - CurrentLoad;
        public List<CargoBox> Boxes { get; set; } = [];

        // NEW: Add dimensions so the 3D algorithm can read them!
        public int Width { get; set; } = 2400;
        public int Length { get; set; } = 5300;
        public int Height { get; set; } = 2600;
    }
}