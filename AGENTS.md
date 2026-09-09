# CrowLink workspace

- This repository is the source of truth. On the owner's Windows PC it is located at `D:\App coding\CrowLink`, under the `D:\App coding` development project.
- Work from this repository, not the former C: chat workspace. Keep build/install outputs in `artifacts/` and local SDK/tool caches in `.tools/`; both are ignored by Git.
- Use repository-relative paths in scripts. See `docs/WORKSPACE.md` for the local layout.
- Do not store credentials or user data in Git. Do not disable ownership protections globally, set `safe.directory=*`, or broaden filesystem permissions to avoid approval.
- Verify tests after code changes. GitHub publishing and destructive cleanup require explicit user authorization; a workspace move does not authorize republishing existing releases.
