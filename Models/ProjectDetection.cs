namespace DevLauncher.Models;

/// <summary>Technologies detected in a project folder.</summary>
/// <param name="IsSymfony">The project is a Symfony application.</param>
/// <param name="UsesTailwindBundle">The project depends on symfonycasts/tailwind-bundle.</param>
public record ProjectDetection(bool IsSymfony, bool UsesTailwindBundle);
