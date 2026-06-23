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

        public int PackedX { get; set; }
        public int PackedY { get; set; }
        public int PackedZ { get; set; }
    }
}