namespace RLNet.Core
{
    public struct StepResult
    {
        public double[] State { get; set; } // Changed from int[] to double[] for continuous support
        public double Reward { get; set; }
        public bool Done { get; set; }
        public string Info { get; set; }
    }

    public interface IEnvironment
    {
        StepResult Reset();
        StepResult Step(int action);
        int GetActionSpaceSize();
        int[] GetObservationSpaceShape(); // Dimension of observation
    }
}