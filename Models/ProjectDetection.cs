namespace DevLauncher.Models;

/// <summary>Technologies detected in a project folder.</summary>
/// <param name="ProjectType">Framework of the project, Other when none is recognized.</param>
/// <param name="UsesTailwindBundle">The project depends on symfonycasts/tailwind-bundle.</param>
/// <param name="HasPackageJson">The project has npm scripts.</param>
/// <param name="HasDockerCompose">The project has a Docker Compose file at its root.</param>
public record ProjectDetection(ProjectType ProjectType, bool UsesTailwindBundle, bool HasPackageJson, bool HasDockerCompose = false);
