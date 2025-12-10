# Contributing to Ignixa

Thank you for your interest in contributing to the Ignixa FHIR Server! We welcome contributions from the community to help make this the best .NET-based FHIR server available.

## Getting Started

1.  **Read the Docs**: specific development guides are available in:
    *   [`CLAUDE.md`](CLAUDE.md): Detailed development guide, architectural rules, and checklists.
    *   [`GEMINI.md`](GEMINI.md): Comprehensive guide for understanding, building, and developing the project.
2.  **Fork and Clone**: Fork the repository and clone it locally.
3.  **Branching**: Create a new branch for your feature or bug fix (e.g., `feature/awesome-thing` or `fix/annoying-bug`).

## Development Environment

*   **SDK**: .NET 9.0 SDK is required.
*   **IDE**: Visual Studio 2022, VS Code, or Rider.
*   **Docker**: Recommended for running SQL Server integration tests.

## Building and Testing

Please ensure your changes build and pass all tests.

```bash
# Build
dotnet build All.sln

# Run Unit Tests
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

## Pull Request Process

1.  Ensure all tests pass.
2.  Update documentation if you are changing behavior or adding features.
3.  Submit a Pull Request (PR) to the `main` branch.
4.  Provide a clear description of the problem and solution.

## Code Style

*   We use `StyleCop` to enforce code style.
*   Warnings are treated as errors in the build.
*   Follow the existing patterns in the codebase (Clean Architecture, CQRS).

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
