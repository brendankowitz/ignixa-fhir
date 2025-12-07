# GitHub Secrets Configuration

This document describes the secrets required for GitHub Actions workflows to function properly.

## Workflow Overview

The NuGet publishing process uses a **two-step approach**:

1. **CI Workflow** (`ci.yml`): Automatically builds and packs NuGet packages on every push to main
   - Uploads packages as GitHub artifacts
   - Does NOT publish to NuGet.org (manual control)

2. **Publish Release Workflow** (`publish-release.yml`): Manual trigger for releasing
   - Downloads latest artifacts from CI
   - Publishes packages to NuGet.org
   - Creates Git tags
   - Generates AI-powered release notes
   - Creates GitHub releases

This separation allows you to build on every commit but release only when intentional.

## Required Secrets

### NuGet Publishing
- **`NUGET_API_KEY`** (required for `publish-to-nuget` job)
  - Type: API Key
  - Source: [NuGet.org Account Settings](https://www.nuget.org/account)
  - Scope: Publish packages to nuget.org
  - Instructions:
    1. Go to nuget.org → Account
    2. Select "API Keys"
    3. Create a new API key with "Push new packages and versions" scope
    4. Add to GitHub secrets as `NUGET_API_KEY`

### Release Notes Generation
- **`GEMINI_API_KEY`** (required for `ai-release-notes` workflow)
  - Type: API Key
  - Source: [Google AI Studio](https://aistudio.google.com/app/apikey)
  - Model: Gemini 1.5 Flash
  - Instructions:
    1. Go to Google AI Studio
    2. Create an API key for your project
    3. Add to GitHub secrets as `GEMINI_API_KEY`
  - Cost: Free tier includes generous limits (subject to Google's terms)

### Docker Registry (GitHub Container Registry)
Docker images are published to **GitHub Container Registry (GHCR)** as public images.

**No secrets required** - `GITHUB_TOKEN` is automatically provided by GitHub Actions for GHCR authentication.

## Setting Up Secrets

1. Navigate to your GitHub repository
2. Go to Settings → Secrets and variables → Actions
3. Click "New repository secret"
4. Enter the secret name (exactly as listed above)
5. Paste the secret value
6. Click "Add secret"

## Validating Secrets

You can test if secrets are properly configured by:

1. **NuGet API Key**:
   - Trigger the **Publish Release** workflow manually
   - Monitor the `Publish to NuGet.org` step logs
   - Should complete without authentication errors

2. **Gemini API Key**:
   - Trigger the **Publish Release** workflow manually
   - Monitor the `Generate Smart Release Notes` step logs
   - Should complete without API authentication errors

3. **Docker Registry (GHCR)**:
   - Docker images are automatically pushed on every commit
   - Verify at: https://github.com/brendankowitz/ignixa-fhir/pkgs/container/ignixa-fhir
   - Images should appear with tags: `main`, `latest`, `release`, version tags

## Publishing a Release

1. Go to **Actions** → **Publish Release** workflow
2. Click **Run workflow**
3. Click **Run workflow** (no inputs needed - uses latest CI artifacts)

The workflow will:
- Download the latest NuGet packages from CI
- Automatically detect version from CI artifacts (no manual input required)
- Promote Docker image to release and version-specific tags
- Publish packages to NuGet.org
- Create a Git tag
- Generate AI release notes using Gemini
- Create a GitHub release with the generated notes

## Security Notes

- Secrets are never logged or exposed in workflow output
- Each secret is only accessible to jobs that explicitly reference it
- Consider rotating API keys periodically for security
- Do not commit secrets to the repository under any circumstances

## Troubleshooting

### "Error: Gemini API returned null or failed"
- Verify `GEMINI_API_KEY` is set and valid
- Check Google Cloud quotas/limits
- Review [Gemini API documentation](https://ai.google.dev/docs)

### "401 Unauthorized" on NuGet publish
- Verify `NUGET_API_KEY` is set and valid
- Ensure API key has "Push" permissions
- Check if API key has expired

### "Permission denied" on GitHub release creation
- Verify workflow has `contents: write` permission
- Check branch protection rules (may require additional approval)
