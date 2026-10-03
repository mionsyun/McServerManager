using McServerManager.Models.Dependencies;

namespace McServerManager.Services.Dependencies;

public interface IDependencyPlanner
{
    DependencyPlan Plan(DependencyPlanningRequest request, CancellationToken cancellationToken = default);
}
