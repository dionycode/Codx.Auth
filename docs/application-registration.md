# Application Registration and Application Role Mapping

## 1. Overview

This document describes:

- how business applications are registered in the platform
- how application clients (UI, mobile, API) are configured
- how application roles are defined and assigned per tenant/company
- how application roles are mapped into JWT access tokens at token issuance time

This process builds on the multi-tenant authorization model defined in [requirement.md](requirement.md).

---

## 2. Concepts and Terminology

| Term                     | Meaning                                                                                                                              |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Application**          | A logical business application (e.g., Personnel Management, Invoicing). Identified by a stable `ApplicationId` string.               |
| **ApiResource**          | IdentityServer's representation of an application. The `ApiResource.Name` becomes the `aud` claim in access tokens.                  |
| **ApiScope**             | A named permission that clients request in the `scope` parameter. Scoped to one application.                                         |
| **Client**               | An OAuth2 client registered in IdentityServer. A single application can have multiple clients (UI, mobile, M2M).                     |
| **ApplicationRoles**     | The catalog of roles defined for an application (e.g., `PersonnelAdmin`). Stored in the identity database.                           |
| **UserApplicationRoles** | The assignment of an application role to a specific user within a tenant+company context. Managed by the application's own admin UI. |

---

## 3. Data Model

These tables live in the **Identity and Authorization Database** (owned by IdentityServer/Codx.Auth).

### 3.1 `Applications`

Logical application registry. Links the platform concept of an "application" to its IdentityServer `ApiResource`.

| Column                  | Type     | Notes                                                                |
| ----------------------- | -------- | -------------------------------------------------------------------- |
| `Id`                    | string   | Stable identifier, e.g. `personnel-api`. Matches `ApiResource.Name`. |
| `DisplayName`           | string   | Human-readable name, e.g. `Personnel Management`.                    |
| `Description`           | string   | Optional description.                                                |
| `AllowSelfRegistration` | bool     | Whether users can self-register to access this application.          |
| `IsActive`              | bool     |                                                                      |
| `CreatedAt`             | datetime |                                                                      |
| `CreatedByUserId`       | GUID     |                                                                      |

### 3.2 `ApplicationRoles`

Role catalog per application. Seeded by `PlatformAdmin` when registering an application.

| Column          | Type     | Notes                                                                                                                                               |
| --------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Id`            | GUID     |                                                                                                                                                     |
| `ApplicationId` | string   | FK → `Applications.Id`                                                                                                                              |
| `Name`          | string   | e.g. `PersonnelAdmin`, `PersonnelManager`, `PersonnelUser`                                                                                          |
| `Description`   | string   | Human-readable description of what this role permits.                                                                                               |
| `IsActive`      | bool     | Deactivate instead of deleting to preserve audit history.                                                                                           |
| `IsDefault`     | bool     | When `true`, automatically assigned to any user who gains a workspace-scoped token and has no existing role for this application. Default: `false`. |
| `CreatedAt`     | datetime |                                                                                                                                                     |

### 3.3 `UserApplicationRoles`

Assignment of an application role to a user within a specific tenant+company workspace context.

| Column             | Type     | Notes                                                       |
| ------------------ | -------- | ----------------------------------------------------------- |
| `Id`               | GUID     |                                                             |
| `UserId`           | GUID     | FK → `AspNetUsers.Id`                                       |
| `TenantId`         | GUID     | FK → `Tenants.Id`                                           |
| `CompanyId`        | GUID     | FK → `Companies.Id`. Always required for application roles. |
| `ApplicationId`    | string   | FK → `Applications.Id`                                      |
| `RoleId`           | GUID     | FK → `ApplicationRoles.Id`                                  |
| `AssignedAt`       | datetime |                                                             |
| `AssignedByUserId` | GUID     |                                                             |

Rules:

- `CompanyId` is always required — application roles are always company-scoped.
- The combination `(UserId, TenantId, CompanyId, ApplicationId, RoleId)` should be unique.
- Role assignments are managed by the **business application's admin UI**, not by IdentityServer's admin UI.
- IdentityServer reads `UserApplicationRoles` at token issuance time only; it does not write to this table.

### 3.4 `ClientProperties` (IdentityServer extension)

Two custom properties are stored on each `Client` record via the standard `ClientProperties` table:

| Key                       | Example Value   | Purpose                                                                                                         |
| ------------------------- | --------------- | --------------------------------------------------------------------------------------------------------------- |
| `application_id`          | `personnel-api` | Links this client to its parent application. Used by `CustomProfileService` to resolve application roles.       |
| `allow_self_registration` | `false`         | Controls whether `AccountController` allows self-registration for users arriving via this client's `returnUrl`. |

---

## 4. Application Registration Process

Performed once per application by a **PlatformAdmin** user via the IdentityServer admin UI.

### Step 1 — Register the Application record

Create a row in the `Applications` table.

```
ApplicationId:         personnel-api
DisplayName:           Personnel Management
Description:           Manages employee records and HR workflows
AllowSelfRegistration: false
IsActive:              true
```

### Step 2 — Register the ApiResource in IdentityServer

Navigate to **API Resources → Add**.

```
Name:         personnel-api        ← must match Applications.Id exactly
DisplayName:  Personnel Management
Enabled:      true
```

### Step 3 — Register ApiScopes

Navigate to **API Scopes → Add**.

Recommended minimum pattern:

| Scope Name           | Display Name              | Notes                                |
| -------------------- | ------------------------- | ------------------------------------ |
| `personnel.api`      | Personnel API Full Access | Used by UI and mobile clients        |
| `personnel.api.read` | Personnel API Read-Only   | Optional, for read-only integrations |

Then link these scopes to the `ApiResource` via **API Resource → Scopes**.

### Step 4 — Define the ApplicationRoles catalog

Navigate to **Applications → Personnel Management → Roles → Add** for each role:

| Name               | Description                                       | IsDefault |
| ------------------ | ------------------------------------------------- | --------- |
| `PersonnelAdmin`   | Full access to all personnel records and settings | ☐         |
| `PersonnelManager` | Manage personnel records within own department    | ☐         |
| `PersonnelUser`    | View-only access to personnel records             | ✅        |

Check **Default Role** on `PersonnelUser` (or whichever is the minimum-access role). This ensures every new member can access the application without waiting for an admin to manually grant them a role — they receive the default role automatically the first time they request a workspace-scoped token.

These roles are the valid values that the application's admin UI will offer when assigning users.

### Step 5 — Register Clients (one per consumer type)

Each of the following is a separate `Client` record. All share the same `ApiResource` and the same `application_id` property.

#### 5a. Web UI Client

```
ClientId:     personnel-web
ClientName:   Personnel Web Application
GrantType:    authorization_code
PKCE:         required
RedirectUri:  https://personnel.yourdomain.com/callback
PostLogoutUri: https://personnel.yourdomain.com/signout
AllowedScopes: openid, profile, personnel.api
ClientProperties:
  application_id = personnel-api
  allow_self_registration = false
```

#### 5b. Mobile Client

```
ClientId:     personnel-mobile
ClientName:   Personnel Mobile App
GrantType:    authorization_code
PKCE:         required
RedirectUri:  personnel://callback          ← custom URI scheme
PostLogoutUri: personnel://signout
AllowedScopes: openid, profile, personnel.api
ClientProperties:
  application_id = personnel-api
  allow_self_registration = false
```

#### 5c. Service-to-Service (M2M) Client

```
ClientId:     personnel-api-m2m
ClientName:   Personnel API Service Account
GrantType:    client_credentials
ClientSecret: <generated, stored hashed>
AllowedScopes: personnel.api
ClientProperties:
  application_id = personnel-api
```

> Note: M2M clients use `client_credentials`. There is no user context. `workspace_roles` and `application_roles` are not emitted. The receiving API authorizes based on `client_id` and `scope` only.

---

## 5. Application Role Assignment

Role assignments are **not** managed via the IdentityServer admin UI. Each business application provides its own admin interface for this, because only that application's admin understands what each role permits.

### 5.1 Automatic default role assignment

When `IsDefault = true` is set on an `EnterpriseApplicationRole`, Codx.Auth automatically assigns that role to any user who:

1. has a valid active membership for the company, **and**
2. requests a workspace-scoped token that includes the application's scope, **and**
3. has **no existing `UserApplicationRole`** for that application in this workspace.

This happens inside `WorkspaceContextValidator` during the token exchange, before claims are emitted. It is idempotent — once a user has any assignment for the application, auto-assignment is skipped.

> **Design intent:** default roles solve the bootstrap problem. New members can access the application immediately with minimum permissions. Admins then elevate specific users to higher roles as needed.

### 5.2 Who can assign roles manually

| Assignment Action                            | Permitted By                                                                   |
| -------------------------------------------- | ------------------------------------------------------------------------------ |
| Auto-assigned on first token                 | System (WorkspaceContextValidator) — for roles marked `IsDefault`              |
| Assign role within a company                 | `CompanyAdmin` or `TenantAdmin` (token must carry that `workspace_role` claim) |
| Assign role across all companies in a tenant | `TenantAdmin` (token must carry `workspace_role: TenantAdmin`)                 |
| Remove role assignment                       | Same as assign — `CompanyAdmin` or `TenantAdmin`                               |

The `POST` and `DELETE` endpoints on `/api/v1/applications/{appId}/user-roles` enforce this: the caller's access token must contain a `workspace_role` claim with value `CompanyAdmin`, `TenantAdmin`, or `PlatformAdministrator`. If not, the API returns `403`.

### 5.3 API endpoints

**List available role definitions** (use to populate a role picker in the admin UI):

```http
GET /api/v1/applications/{appId}/roles
Authorization: Bearer <workspace-scoped-access-token>
```

Response:

```json
[
  {
    "id": "guid",
    "name": "PersonnelAdmin",
    "description": "...",
    "isDefault": false
  },
  {
    "id": "guid",
    "name": "PersonnelManager",
    "description": "...",
    "isDefault": false
  },
  {
    "id": "guid",
    "name": "PersonnelUser",
    "description": "...",
    "isDefault": true
  }
]
```

**List assignments for a workspace** (use to display current role assignments per user):

```http
GET /api/v1/applications/{appId}/user-roles?tenantId={t}&companyId={c}
Authorization: Bearer <workspace-scoped-access-token>
```

**Assign a role to a user** (`CompanyAdmin` / `TenantAdmin` only):

```http
POST /api/v1/applications/{appId}/user-roles
Authorization: Bearer <workspace-scoped-access-token>
Content-Type: application/json

{
  "userId":    "<target-user-guid>",
  "tenantId":  "<tenant-guid>",
  "companyId": "<company-guid>",
  "roleId":    "<enterprise-application-role-guid>"
}
```

The server validates that the caller's `tenant_id` claim matches the request body's `tenantId` — preventing cross-tenant writes.

**Revoke a role assignment** (`CompanyAdmin` / `TenantAdmin` only):

```http
DELETE /api/v1/applications/{appId}/user-roles/{id}
Authorization: Bearer <workspace-scoped-access-token>
```

### 5.4 Manual assignment flow (inside the business application's admin UI)

```
1. CompanyAdmin navigates to "User Management" in the Personnel app
2. SPA calls GET /api/v1/applications/personnel-api/roles  ← populate role dropdown
3. SPA calls GET /api/v1/applications/personnel-api/user-roles?tenantId=...&companyId=...  ← show who has what
4. Admin selects a user and a role
5. SPA calls POST /api/v1/applications/personnel-api/user-roles
   {
     userId:    <selected user>,
     tenantId:  <from admin's own token claim>,
     companyId: <from admin's own token claim>,
     roleId:    <selected from role list>
   }
```

The `TenantId` and `CompanyId` values written here are sourced from the **admin's own access token claims** — never from user input — to prevent privilege escalation.

---

## 6. How Application Roles Map into the Access Token

This is the core of the application role flow. It happens inside `CustomProfileService.GetProfileDataAsync` during every interactive access token issuance and refresh token exchange.

### 6.1 Scope → Application binding

When a client requests a token, it includes a `scope` parameter. The `CustomProfileService` inspects `context.ValidatedRequest.RequestedScopes` to determine which application(s) this token is for.

```
Client scope request:  "openid profile personnel.api"
                                          ↓
ProfileService resolves:  "personnel.api" scope → ApiResource.Name = "personnel-api"
                                          ↓
Looks up:  UserApplicationRoles WHERE ApplicationId = "personnel-api"
                                       AND UserId        = @userId
                                       AND TenantId      = @tenantId  ← from validated workspace context
                                       AND CompanyId     = @companyId ← from validated workspace context
```

> Application roles are only emitted if the client explicitly requests the corresponding scope. A token issued for `invoicing.api` will never contain `PersonnelAdmin`.

### 6.2 Role resolution query

```sql
SELECT ar.Name
FROM UserApplicationRoles uar
JOIN ApplicationRoles ar ON ar.Id = uar.RoleId
WHERE uar.UserId        = @UserId
  AND uar.TenantId      = @TenantId
  AND uar.CompanyId     = @CompanyId
  AND uar.ApplicationId = @ApplicationId
  AND ar.IsActive       = 1
```

### 6.3 Resulting access token claims

```json
{
  "sub": "user-guid",
  "tenant_id": "tenant-guid",
  "company_id": "company-guid",
  "membership_id": "membership-guid",
  "workspace_context_type": "company",
  "workspace_role": ["COMPANY_ADMIN", "MEMBER"],
  "application_role": ["PersonnelAdmin"],
  "workspace_session_id": "session-guid",
  "aud": "personnel-api",
  "iss": "https://auth.yourdomain.com",
  "exp": 1234567890
}
```

### 6.4 Full token building sequence in ProfileService

```
GetProfileDataAsync (access token)
  │
  ├─ 1. Read validated workspace context
  │      (tenantId, companyId, membershipId, contextType)
  │      → set by WorkspaceContextValidator before ProfileService runs
  │
  ├─ 2. Emit workspace claims
  │      tenant_id, company_id, membership_id,
  │      workspace_context_type, workspace_session_id
  │
  ├─ 3. Emit workspace roles
  │      query UserMemberships → UserMembershipRoles → WorkspaceRoleDefinitions
  │      emit as: workspace_role = "COMPANY_ADMIN"
  │
  ├─ 4. Identify requested application(s) from scopes
  │      RequestedScopes → match against ApiResource names
  │      → applicationIds = ["personnel-api"]
  │
  ├─ 5. For each applicationId:
  │      query UserApplicationRoles → ApplicationRoles
  │      scoped to (userId, tenantId, companyId, applicationId)
  │      emit as: application_role = "PersonnelAdmin"
  │
  └─ 6. Context.IssuedClaims.AddRange(claims)
```

---

## 7. API-Side Role Enforcement

Inside the business application API, policies are defined against the `application_role` claim. The API does **not** call IdentityServer to validate — it reads claims from the locally validated JWT only.

```csharp
// Program.cs / Startup.cs of the business API
services.AddAuthorization(options =>
{
    options.AddPolicy("PersonnelAdminOnly", policy =>
        policy.RequireClaim("application_role", "PersonnelAdmin"));

    options.AddPolicy("PersonnelReadAccess", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("application_role", "PersonnelAdmin") ||
            ctx.User.HasClaim("application_role", "PersonnelManager") ||
            ctx.User.HasClaim("application_role", "PersonnelUser")));

    // Always enforce company context for business APIs
    options.AddPolicy("CompanyContext", policy =>
        policy.RequireClaim("workspace_context_type", "company")
              .RequireClaim("company_id"));
});
```

Data access is then filtered using claims read from `IHttpContextAccessor`:

```csharp
// Enforced via EF Core global query filter
modelBuilder.Entity<PersonnelRecord>()
    .HasQueryFilter(p =>
        p.TenantId  == _workspaceContext.TenantId &&
        p.CompanyId == _workspaceContext.CompanyId);
```

---

## 8. Multi-Client Token Consistency

Because all three clients (`personnel-web`, `personnel-mobile`, `personnel-api-m2m`) share the same `ApiResource` (`personnel-api`), a token issued to any of them:

- carries `aud = personnel-api`
- passes the same audience validation on the receiving API
- emits the same `application_role` claims (for interactive flows)

The Personnel API does not need to know or care which client issued the request — only the `aud`, signature, and claims matter.

---

## 9. Workspace Context Requirement for Application Roles

Application roles are **always** company-scoped. This means:

- a token with `workspace_context_type = tenant` will **never** carry `application_role` claims
- a client that needs to perform app-level operations must request a token in company context
- the workspace selection step (choosing a company) must happen before any application-level operation

This is a deliberate design constraint: business data operations require knowing which company's data to access. A tenant-context token is for tenant management operations only.

---

## 10. Summary: Responsibilities by Role

| Responsibility                           | Owner                                                                                    |
| ---------------------------------------- | ---------------------------------------------------------------------------------------- |
| Register Application + ApiResource       | `PlatformAdmin` via IdentityServer admin UI                                              |
| Define ApplicationRoles catalog          | `PlatformAdmin` via IdentityServer admin UI                                              |
| Mark roles as `IsDefault`                | `PlatformAdmin` via IdentityServer admin UI (Applications → Roles → Add Role)            |
| Register Clients (web, mobile, M2M)      | `PlatformAdmin` via IdentityServer admin UI                                              |
| Auto-assign default roles on first token | `WorkspaceContextValidator` (system) — runs on workspace token exchange                  |
| Assign elevated roles to users           | `CompanyAdmin` / `TenantAdmin` via `POST /api/v1/applications/{appId}/user-roles`        |
| Revoke role assignments                  | `CompanyAdmin` / `TenantAdmin` via `DELETE /api/v1/applications/{appId}/user-roles/{id}` |
| Emit application roles into token        | `CustomProfileService` (IdentityServer) — reads `UserApplicationRoles`                   |
| Enforce application roles on endpoints   | Business application API — reads claims from JWT                                         |
