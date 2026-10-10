namespace SecureAgentLab.Api.Configuration;

internal sealed record LabOptions(bool UseInMemory, bool RuntimeLimits, bool ReportWrites, string PolicyVersion, string? DocumentRoot)
{
    internal static LabOptions From(IConfiguration configuration)
    {
        bool Flag(string key) => bool.TryParse(configuration[key], out bool value) && value;
        var options = new LabOptions(Flag("Lab:UseInMemory"), Flag("Lab:EnableRuntimeLimits"), Flag("Lab:EnableReportWrites"), configuration["Lab:PolicyVersion"] ?? "synthetic-v1", configuration["Lab:DocumentRoot"]);
        if (string.IsNullOrWhiteSpace(options.PolicyVersion))
        {
            throw new ArgumentException("A nonempty policy version is required.");
        }

        return options;
    }
}
