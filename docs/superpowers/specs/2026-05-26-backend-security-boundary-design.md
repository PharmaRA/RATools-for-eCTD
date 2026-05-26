# Backend Security Boundary Design

## Goal

Phase 2 adds a narrow backend security boundary around server filesystem access without redesigning the whole API authentication model. The immediate goal is to stop unauthenticated users from browsing server directories, creating server-side workspace directories, or importing applications from arbitrary server paths, while keeping the rest of the current local-development workflow stable.

## Current Context

The API currently has no authentication or authorization middleware. Most endpoints are controller actions under `src/RATools.Api.Controllers`. The highest-risk endpoints are:

- `GET /api/filesystem/directories`
- `POST /api/filesystem/resolve-directory`
- `POST /api/applications`
- `POST /api/applications/import`

`FilesystemController` delegates directly to `IServerDirectoryBrowser`. `LocalServerDirectoryBrowser` resolves user-supplied paths with `Path.GetFullPath`, browses root drives when no path is supplied, and currently has no configured allowlist. `ApplicationsController.Create` accepts a `WorkingDirectoryParentPath` and can create server-side workspace directories. `ApplicationsController.Import` accepts a `WorkingDirectoryPath` and passes it to `ApplicationImportService`, which normalizes it and reads directories from the server filesystem.

## Chosen Approach

Use static API key authentication for high-risk endpoints and add an allowlist-based workspace path policy. This is the smallest change that meaningfully reduces risk.

The design intentionally does not protect every existing business API in this phase. It also does not introduce users, sessions, roles, or OAuth. Those can be added later once the API's deployment model is clearer.

## Public And Protected Endpoints

These endpoints remain public:

- `GET /`
- `GET /health`
- `GET /version`
- Swagger endpoints when `Swagger:Enabled` is true
- Existing non-filesystem business API endpoints not listed below

These endpoints require the configured API key:

- `GET /api/filesystem/directories`
- `POST /api/filesystem/resolve-directory`
- `POST /api/applications`
- `POST /api/applications/import`

The protected endpoints require request header `X-RA-Tools-Api-Key`. Missing or invalid keys return `401 Unauthorized` before controller logic runs.

## Configuration

Add a new `Security` configuration section:

```json
{
  "Security": {
    "ApiKey": "",
    "AllowedWorkspaceRoots": []
  }
}
```

`Security:ApiKey` is the only valid key for protected endpoints. If it is empty or whitespace, protected endpoints reject every request with `401 Unauthorized`. This fail-closed default avoids accidentally exposing filesystem operations in production.

`Security:AllowedWorkspaceRoots` is the list of server directories that filesystem browsing, directory resolution, application creation, and application import may access. If the list is empty, path-scoped operations fail with `400 Bad Request` using a clear message that no workspace roots are configured.

Development environments can configure these values through user secrets, environment variables, or a local development settings file. The repository should not commit real secrets.

## Path Boundary Rules

All path checks use a single application abstraction named `IWorkspacePathPolicy`.

The policy normalizes configured roots and request paths with `Path.GetFullPath`. A candidate path is allowed only when it is equal to a configured root or located under a configured root. Sibling paths with a shared string prefix are not allowed.

Existing directories from the matched allowed root through the candidate path must not be reparse points. Symlink, junction, and other reparse-point directories are rejected even when their lexical path is inside an allowed root, because they can redirect filesystem access outside the configured boundary. For non-existing candidate paths, the policy checks the existing ancestors before creation.

Examples with allowed root `D:\\Workspace\\RATools`:

- `D:\\Workspace\\RATools` is allowed
- `D:\\Workspace\\RATools\\App001` is allowed
- `D:\\Workspace\\RAToolsSibling` is rejected
- `D:\\Workspace\\RATools\\..\\Secrets` is rejected after normalization if it resolves outside the root
- `D:\\Workspace\\RATools\\LinkToSecrets` is rejected if it is a symlink, junction, or other reparse point

Path comparison uses `StringComparison.OrdinalIgnoreCase` on Windows and `StringComparison.Ordinal` on non-Windows platforms.

## Filesystem Browsing Behavior

`LocalServerDirectoryBrowser` must enforce `IWorkspacePathPolicy` before returning filesystem data.

When browsing with no `path`, the root response should not enumerate all server drives. Instead, it returns the configured allowed workspace roots as top-level entries. This keeps the directory picker useful without exposing unrelated drives.

Configured root entries are validated with `IWorkspacePathPolicy` before existence checks or child probing. If a configured root is missing or is rejected because it is a symlink, junction, or other reparse-point directory, the browser returns that root as an inaccessible entry and does not enumerate the target.

When browsing or resolving a specific path:

- The path is normalized and checked against `IWorkspacePathPolicy`.
- Each child directory entry is checked against `IWorkspacePathPolicy` before probing whether it has children.
- A path outside all allowed roots raises an `InvalidOperationException` with a boundary-specific message.
- Reparse-point child entries are returned as inaccessible entries with `CanBrowse=false` and `HasChildren=false` instead of being traversed.
- Existing inaccessible or missing directory handling remains unchanged.

`FilesystemController` continues mapping `InvalidOperationException` to `400 Bad Request` and `DirectoryNotFoundException` to `404 Not Found`.

## Application Import Behavior

`ApplicationImportService.ImportAsync` must validate `request.WorkingDirectoryPath` with `IWorkspacePathPolicy` before reading the directory or deriving the application number. If the path is outside all allowed roots or no roots are configured, it throws `InvalidOperationException`. `ApplicationsController.Import` already maps this exception to `400 Bad Request`.

After enumerating sequence directories, import must validate each sequence directory with `IWorkspacePathPolicy` before checking for `index.xml` or loading XML. The combined `index.xml` file path itself must also be validated with `IWorkspacePathPolicy` before `File.Exists` or XML loading so file symlinks and other reparse-point files cannot redirect import outside the allowed roots. While processing each leaf, import must validate the resolved leaf file parent directory and the resolved leaf file path itself with `IWorkspacePathPolicy` before `File.Exists`, checksum calculation, or file metadata reads. Sequence directory, `index.xml`, leaf parent, and leaf file symlinks, junctions, and other reparse points are rejected before any target content is read.

This protects the server-side import path even if a client bypasses the frontend directory picker.

## Application Create Behavior

`ApplicationService.CreateAsync` must combine `WorkingDirectoryParentPath` and `ApplicationNumber`, then validate that final requested working directory with `IWorkspacePathPolicy` before creating directories. The workspace service must create the allowed normalized final path, not the raw user-supplied parent path.

This protects server-side application workspace creation even if a client bypasses the frontend directory picker.

## Authentication Architecture

Use ASP.NET Core authentication with a small custom API key handler in `RATools.Api`. Add an authorization policy for high-risk endpoints, for example `HighRiskFilesystemAccess`.

Controller actions should be annotated explicitly rather than relying on global authorization:

- `FilesystemController` can require the policy at controller level.
- `ApplicationsController.Create` and `ApplicationsController.Import` require the policy at action level because both consume server-side workspace paths.

The handler reads `Security:ApiKey` from configuration or options and compares it to `X-RA-Tools-Api-Key` using a fixed-time comparison where practical. Empty configured keys never authenticate.

## Frontend And Smoke Test Impact

This design does not require frontend routing changes. Existing frontend calls to protected endpoints will receive `401` until the frontend is configured to send the header.

Implementation should add a small frontend API-key configuration path only if needed for local manual testing. It should not hard-code secrets in source code.

Smoke tests that exercise application import or filesystem browsing must provide `X-RA-Tools-Api-Key` when security is enabled.

## Error Handling

Authentication failures return `401 Unauthorized` without exposing whether the key is missing or wrong.

Path boundary failures return `400 Bad Request` with a concise message, such as `Path '<path>' is outside the configured workspace roots.` For empty root configuration, use a concise message, such as `No allowed workspace roots are configured.`

Missing directories continue returning `404 Not Found` from filesystem browsing and `400 Bad Request` from application import through existing controller behavior.

## Testing Strategy

Backend tests drive the implementation with red-green-refactor cycles.

Required tests:

- Missing API key on `GET /api/filesystem/directories` returns `401`.
- Wrong API key on `GET /api/filesystem/directories` returns `401`.
- Correct API key on `GET /api/filesystem/directories` reaches controller behavior.
- Missing API key on `POST /api/applications` returns `401`.
- Missing API key on `POST /api/applications/import` returns `401`.
- Empty `AllowedWorkspaceRoots` rejects filesystem browsing with `400`.
- A path outside configured roots rejects filesystem browsing with `400`.
- A configured root and its child directories are allowed.
- Filesystem browsing returns configured reparse-point roots and reparse-point child entries as inaccessible without enumerating their targets.
- A sibling path with a shared prefix is rejected.
- Application import rejects a working directory outside configured roots before reading filesystem contents.
- Application import rejects reparse-point sequence directories before reading `index.xml`.
- Application import rejects reparse-point `index.xml` files before checking existence or loading XML.
- Application import rejects reparse-point leaf parent directories before checking or hashing referenced leaf files.
- Application create rejects a final working directory outside configured roots before creating filesystem contents.

Verification commands:

```powershell
dotnet test "tests/RATools.Tests/RATools.Tests.csproj"
```

If frontend API-key plumbing is changed, also run:

```powershell
cd frontend
npm test
npm run build
```

## Non-Goals

This phase does not add user accounts, role management, token issuance, refresh tokens, OpenID Connect, or a complete API-wide authorization model. It does not encrypt existing persisted paths. It does not change the database schema.
