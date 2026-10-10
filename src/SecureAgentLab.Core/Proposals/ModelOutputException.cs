namespace SecureAgentLab.Core.Proposals;

public sealed class ModelOutputException : Exception
{
    public ModelOutputException() : base("model_output_rejected")
    {
    }
}
