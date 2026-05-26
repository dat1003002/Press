namespace Press.Model
{
    public class LrXData
    {
        public bool IsValid { get; set; }
        public double DistanceMm { get; set; } = double.NaN;

        public static readonly LrXData Invalid = new() { IsValid = false };
    }
}