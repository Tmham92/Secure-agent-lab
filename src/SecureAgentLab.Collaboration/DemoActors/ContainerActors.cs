namespace SecureAgentLab.Collaboration.DemoActors;

public static class ContainerActors
{
    public static Task Agent(string action) => AgentContainerActor.RunAsync(action);
    public static Task Supervisor(string action) => SupervisorContainerActor.RunAsync(action);
}
