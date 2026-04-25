# Codx.Auth — Administrator User Manual

> **Audience:** Platform Administrators and developers responsible for configuring the identity platform.
> **Platform:** Codx.Auth — Duende IdentityServer + ASP.NET Core multi-tenant identity server.

---

## Table of Contents

1. [Overview and Concepts](#1-overview-and-concepts)
2. [Authorization Levels](#2-authorization-levels)
3. [Section: Users](#3-section-users)
4. [Section: Tenants](#4-section-tenants)
5. [Section: Companies](#5-section-companies)
6. [Section: Clients](#6-section-clients)
7. [Section: API Resources](#7-section-api-resources)
8. [Section: API Scopes](#8-section-api-scopes)
9. [Section: Identity Resources](#9-section-identity-resources)
10. [Section: Applications](#10-section-applications)
11. [Section: Memberships](#11-section-memberships)
12. [Section: Invitations](#12-section-invitations)
13. [End-to-End Example: Setting Up Personnel Management](#13-end-to-end-example-setting-up-personnel-management)
14. [SPA Integration Reference](#14-spa-integration-reference)
15. [Troubleshooting: No Roles in Access Token](#15-troubleshooting-no-roles-in-access-token)

---

## 1. Overview and Concepts

Codx.Auth is the centralized identity and authorization server for the platform. It is built on **Duende IdentityServer** and issues JWT access tokens that carry all identity and workspace context a downstream API needs.

### How the System Works

```
User (browser / SPA)
  │
  │  OIDC Authorization Code + PKCE
  ▼
Codx.Auth (IdentityServer)
  │  Issues a JWT access token containing:
  │    sub, tenant_id, company_id, membership_id,
  │    workspace_context_type, workspace_role[], application_role[]
  ▼
Business API (e.g. PersonnelManagement API)
  │  Validates token locally (no introspection calls)
  │  Reads claims to enforce data isolation
  ▼
Application Database
```

### Key Concepts

| Concept               | What it is                                                                                                                                       |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| **User**              | One global account. A user can be a member of many tenants and companies.                                                                        |
| **Tenant**            | The top-level organizational boundary (typically a customer organization).                                                                       |
| **Company**           | A division or subsidiary inside a Tenant. Users work inside a Company.                                                                           |
| **Client**            | An OAuth2 client — the SPA, mobile app, or API that talks to IdentityServer.                                                                     |
| **API Resource**      | Represents a protected API (e.g., the Personnel Management API).                                                                                 |
| **API Scope**         | A named permission that a Client requests and the API Resource exposes.                                                                          |
| **Identity Resource** | Claims about the user returned in the ID token (e.g., `openid`, `profile`, `email`).                                                             |
| **Application**       | A registered application inside Codx.Auth that groups its own role definitions. The Application's `Id` **must match** the API Resource's `Name`. |
| **Membership**        | Records that a User belongs to a Tenant (tenant-scoped) or a Tenant+Company (company-scoped).                                                    |
| **Workspace Role**    | Structural roles (`TenantOwner`, `TenantAdmin`, `CompanyAdmin`, etc.) stored in `UserMembershipRoles`.                                           |
| **Application Role**  | Business roles (`PersonnelAdmin`, `PersonnelManager`, etc.) stored in `UserApplicationRoles`.                                                    |

---

## 2. Authorization Levels

The platform has **three independent authorization layers**. Roles from one layer do not imply any role in another.

```
┌─────────────────────────────────────────────────────────┐
│  Platform Level  →  PlatformAdmin                        │
│  Manages: Tenants, Companies, Clients, API Resources,    │
│           Applications, IdentityServer configuration     │
├─────────────────────────────────────────────────────────┤
│  Workspace Level  →  TenantOwner, TenantAdmin,           │
│                       CompanyAdmin, CompanyManager,       │
│                       Member                             │
│  Manages: Memberships, Invitations within their tenant   │
├─────────────────────────────────────────────────────────┤
│  Application Level  →  PersonnelAdmin, PersonnelManager, │
│                         PersonnelUser  (per-app roles)   │
│  Controls: business feature access inside each app API   │
└─────────────────────────────────────────────────────────┘
```

---

## 3. Section: Users

**Route:** `/Users`
**Required Role:** `PlatformAdmin`

### Purpose

Users are **global accounts** — a user exists once in the system regardless of how many tenants or companies they belong to. Authentication credentials (password, 2FA, external login providers) live here.

### What You Can Do

| Action                   | Description                                                                           |
| ------------------------ | ------------------------------------------------------------------------------------- |
| **List Users**           | Browse and search all registered users.                                               |
| **View Details**         | See user profile, claims, roles, and linked companies/memberships.                    |
| **Add User**             | Create a new user account manually (for internal or admin accounts).                  |
| **Edit User**            | Update profile fields (given name, family name, email).                               |
| **Assign Platform Role** | Grant or revoke the `PlatformAdmin` or `PlatformAdministrator` ASP.NET Identity role. |
| **Manage Claims**        | Add custom claims per user (e.g., `locale`, `department`).                            |
| **View Memberships**     | See all Tenant and Company memberships for this user.                                 |

### Step-by-Step: Creating an Admin User

1. Navigate to **Users → Add**.
2. Fill in:
   - **Username** — unique login name (e.g., `admin@example.com`)
   - **Email** — used for email verification and invitations
   - **Password** — must meet the configured complexity policy
   - **Given Name / Family Name** — used in the access token `name` claim
3. Click **Create**.
4. The user now appears in the list. To grant platform admin access:
   - Open the user's **Details** page.
   - Navigate to the **Roles** tab.
   - Assign the `PlatformAdministrator` role.

> **Important:** A `PlatformAdmin` role grants full access to the Codx.Auth admin UI. This is entirely separate from workspace and application roles — assigning this does NOT give the user access to any business APIs.

---

## 4. Section: Tenants

**Route:** `/Tenants`
**Required Role:** `PlatformAdmin`

### Purpose

A **Tenant** is the top-level customer organization. Everything in the system is scoped under a Tenant. When a user registers for the first time, the system automatically creates a Tenant and seeds initial memberships for them.

Possible `Status` values: `Active`, `Suspended`, `Cancelled`.

A user can belong to multiple tenants. Tokens are always scoped to exactly one tenant at a time.

### Fields

| Field         | Description                                                   |
| ------------- | ------------------------------------------------------------- |
| `Name`        | Display name of the organization (e.g., `"Acme Corporation"`) |
| `Slug`        | URL-friendly short identifier (e.g., `"acme"`)                |
| `Status`      | `Active` / `Suspended` / `Cancelled`                          |
| `Email`       | Primary contact email for the tenant                          |
| `Phone`       | Contact phone number                                          |
| `Address`     | Physical address                                              |
| `Logo`        | URL to the tenant's logo image                                |
| `Theme`       | UI theme identifier                                           |
| `Description` | Optional description                                          |

### Step-by-Step: Creating a Tenant

1. Navigate to **Tenants → Add**.
2. Fill in **Name** (required) and optionally Slug, Email, Phone, etc.
3. Click **Create**.
4. The tenant is now **Active**. Next, create at least one Company inside it.
5. Then create Memberships for users who should belong to this tenant.

> **When a user self-registers**, the system automatically:
>
> - Creates the Tenant
> - Creates a first Company
> - Creates a `TenantOwner` membership for the registering user
> - Creates a `CompanyAdmin` membership for the registering user
>
> You do not need to do this manually for self-registered users.

---

## 5. Section: Companies

**Route:** `/Companies` (also accessible from Tenant Details)
**Required Role:** `PlatformAdmin`

### Purpose

A **Company** is a workspace inside a Tenant. Users work in the context of a specific Company. All business data (personnel records, invoices, etc.) is scoped to a Tenant + Company pair.

A Tenant can have many Companies (e.g., different regional offices or business units).

### Fields

| Field                          | Description                                   |
| ------------------------------ | --------------------------------------------- |
| `TenantId`                     | The parent Tenant this Company belongs to     |
| `Name`                         | Display name (e.g., `"Acme - Manila Office"`) |
| `Status`                       | `Active` / `Suspended` / `Cancelled`          |
| `Email`, `Phone`, `Address`    | Contact information                           |
| `Logo`, `Theme`, `Description` | Branding and metadata                         |

### Step-by-Step: Creating a Company

1. Navigate to **Tenants → Details** for the parent tenant.
2. Click the **Companies** tab and then **Add Company**.
   - Alternatively: **Companies → Add** and select the `TenantId`.
3. Fill in the **Name** and set `Status` to `Active`.
4. Click **Create**.
5. Once the Company exists, create Memberships for users who should work inside it.

---

## 6. Section: Clients

**Route:** `/Clients`
**Required Role:** `PlatformAdmin`

### Purpose

A **Client** is any application that authenticates users through IdentityServer. This includes:

- React / Angular SPAs
- Mobile apps
- Backend services (machine-to-machine)
- The Codx.Auth admin UI itself

For each business application (e.g., Personnel Management), you need **two clients**:

1. **The SPA client** — the React/Angular front-end used by end users (grant type: `authorization_code`)
2. _(Optionally)_ **The API client** — for machine-to-machine calls (grant type: `client_credentials`)

### Key Client Fields

| Field                             | Purpose                                                                     |
| --------------------------------- | --------------------------------------------------------------------------- |
| `ClientId`                        | Unique identifier used by the application in OAuth2 requests                |
| `ClientName`                      | Human-readable display name                                                 |
| `Enabled`                         | Must be checked for the client to work                                      |
| `Require Client Secret`           | Must be **unchecked** for public SPA clients (PKCE replaces the secret)     |
| `Require PKCE`                    | Must be **checked** for SPA clients to enforce PKCE security                |
| `Allow Offline Access`            | Check to enable refresh tokens (required for workspace context flow)        |
| `Grant Types`                     | The OAuth2 flows permitted — `authorization_code` for SPAs                  |
| `Redirect URIs`                   | The exact base URLs the SPA will redirect to after login                    |
| `Post Logout Redirect URIs`       | Where to redirect after logout                                              |
| `CORS Origins`                    | The SPA's origin URL — required to allow browser requests                   |
| `Allowed Scopes`                  | Which API Scopes and Identity Resources this client can request             |
| `Access Token Lifetime`           | How long (seconds) the access token lives before the SPA must refresh       |
| `Refresh Token Usage`             | Set to `ReUse` (0) or `OneTimeOnly` (1) — spec requires **OneTimeOnly** (1) |
| `Absolute Refresh Token Lifetime` | Maximum total lifetime of a refresh token chain                             |

### Step-by-Step: Creating an SPA Client

1. Navigate to **Clients → Add**.
2. Fill in:
   - **Client ID:** `personnel-spa` (URL-safe, lowercase)
   - **Client Name:** `Personnel Management SPA`
   - **Enabled:** ✅
3. Click **Create** to save the basic record, then open **Edit**.
4. Configure security settings:
   - **Require Client Secret:** ☐ (unchecked — no secret for public clients)
   - **Require PKCE:** ✅
   - **Allow Offline Access:** ✅ (enables refresh tokens)
   - **Allow Access Tokens via Browser:** ✅
5. Set token lifetimes:
   - **Access Token Lifetime:** `900` (15 minutes — short-lived per spec)
   - **Absolute Refresh Token Lifetime:** `2592000` (30 days)
   - **Sliding Refresh Token Lifetime:** `1296000` (15 days)
   - **Refresh Token Usage:** `1` (OneTimeOnly — prevents stolen token replay)
   - **Refresh Token Expiration:** `1` (Sliding)
   - **Update Access Token Claims on Refresh:** ✅ (ensures fresh workspace roles on each refresh)
6. Under **Grant Types** tab → Add `authorization_code`.
7. Under **Redirect URIs** tab → Add:
   - `http://localhost:5173/callback` (local development)
   - `https://app.example.com/callback` (production)
8. Under **Post Logout Redirect URIs** tab → Add:
   - `http://localhost:5173`
   - `https://app.example.com`
9. Under **CORS Origins** tab → Add:
   - `http://localhost:5173`
   - `https://app.example.com`
10. Under **Scopes** tab → Add the following (must exist first — see sections 8 and 9):
    - `openid`
    - `profile`
    - `email`
    - `offline_access`
    - `personnel-api` ← the API Scope for the Personnel API Resource
11. Under **Properties** tab → Add:
    - Key: `application_id`, Value: `personnel-management` ← **must match the Application ID exactly** (see Section 10)
12. Click **Save**.

---

## 7. Section: API Resources

**Route:** `/ApiResources`
**Required Role:** `PlatformAdmin`

### Purpose

An **API Resource** represents a protected API. The IdentityServer uses it to group API Scopes. When a client requests an API Scope, IdentityServer looks up which API Resource that scope belongs to — the API Resource's `Name` is then used to find the matching `EnterpriseApplication` in the Application registry.

> **Critical bridge:** `ApiResource.Name` **must exactly match** `EnterpriseApplication.Id`.
> This is the link between the IdentityServer configuration and the Application Role system.
> If they differ by even one character, application roles will never appear in access tokens.

### Fields

| Field          | Description                                                                                           |
| -------------- | ----------------------------------------------------------------------------------------------------- |
| `Name`         | **Machine identifier** — must match `EnterpriseApplication.Id` exactly (e.g., `personnel-management`) |
| `Display Name` | Human-readable name (e.g., `"Personnel Management API"`)                                              |
| `Description`  | Optional description                                                                                  |
| `Enabled`      | Must be checked                                                                                       |

### Step-by-Step: Creating the Personnel API Resource

1. Navigate to **Api Resources → Add**.
2. Fill in:
   - **Name:** `personnel-management`
   - **Display Name:** `Personnel Management API`
   - **Enabled:** ✅
3. Click **Create**.
4. Navigate to the **Scopes** tab on the API Resource details page → Add the scope `personnel-api` (see Section 8).
5. Navigate to the **Claims** tab → Add any user claims this API needs the token to carry (e.g., `email`).

> The `Name` you use here (`personnel-management`) is what you will also enter as the **Application ID** in Section 10.

---

## 8. Section: API Scopes

**Route:** `/ApiScopes`
**Required Role:** `PlatformAdmin`

### Purpose

An **API Scope** is a named permission that a Client requests access to. The Client includes it in its token request; IdentityServer only issues it if the Client is allowed to use it. APIs validate that the token carries the right scope before serving data.

Scopes are the "quantity" a client requests. API Resources group scopes together and define which audience (`aud`) carries them.

### Fields

| Field               | Description                                                                |
| ------------------- | -------------------------------------------------------------------------- |
| `Name`              | Machine identifier used in OAuth2 requests (e.g., `personnel-api`)         |
| `Display Name`      | Shown on the consent screen                                                |
| `Description`       | Optional description                                                       |
| `Emphasize`         | Whether the consent UI should highlight this scope                         |
| `Required`          | If checked, the user cannot skip granting this scope on the consent screen |
| `Show in Discovery` | Whether to include in the OIDC discovery document                          |

### Step-by-Step: Creating the Personnel API Scope

1. Navigate to **Api Scopes → Add**.
2. Fill in:
   - **Name:** `personnel-api`
   - **Display Name:** `Personnel Management`
   - **Description:** `Access to the Personnel Management API`
3. Click **Create**.
4. Go back to the **API Resource** (`personnel-management`) and add `personnel-api` under its **Scopes** tab.
5. Go to the **Client** (`personnel-spa`) and add `personnel-api` under its **Scopes** tab.

> The scope name `personnel-api` will be what the SPA includes in its token request's `scope` parameter. The backend API should validate that the access token's `scope` claim contains `personnel-api`.

---

## 9. Section: Identity Resources

**Route:** `/IdentityResources`
**Required Role:** `PlatformAdmin`

### Purpose

**Identity Resources** define what user identity claims can appear in the **ID token** and can be requested via the `scope` parameter. They are always about the _identity of the user_, not about API permissions.

The three standard identity resources should always exist:

| Name      | Scope     | Claims Included                                           |
| --------- | --------- | --------------------------------------------------------- |
| `openid`  | `openid`  | `sub` (subject/user ID)                                   |
| `profile` | `profile` | `name`, `given_name`, `family_name`, `preferred_username` |
| `email`   | `email`   | `email`, `email_verified`                                 |

### When to Add Custom Identity Resources

Add a custom identity resource only if you need new claims in the **ID token** itself (e.g., `locale`, `tenant_id` in the ID token). For claims that only need to be in the **access token** (like `tenant_id`, `workspace_role`, `application_role`), no identity resource is needed — those are handled by `CustomProfileService`.

### Step-by-Step: Verifying Standard Identity Resources Exist

1. Navigate to **Identity Resources**.
2. Confirm `openid`, `profile`, and `email` are listed and **Enabled**.
3. If any are missing, click **Add** and create them with the exact names above.
4. Ensure the Client (`personnel-spa`) has `openid`, `profile`, `email` added to its **Scopes**.

---

## 10. Section: Applications

**Route:** `/Applications`
**Required Role:** `PlatformAdmin`

### Purpose

An **Application** is the Codx.Auth registry entry for a business application. It serves two purposes:

1. **Defines the application's role catalogue** — the list of business roles that users can be assigned (e.g., `PersonnelAdmin`, `PersonnelManager`, `PersonnelUser`).
2. **Links to the IdentityServer Client** — via the `application_id` Client Property, creating the bridge that lets the token pipeline resolve application roles.

> **The critical ID link chain:**
>
> ```
> Client (ClientProperty: application_id = "personnel-management")
>    ↕
> ApiResource (Name = "personnel-management")
>    ↕
> Application (Id = "personnel-management")
>    ↕
> EnterpriseApplicationRole (ApplicationId = "personnel-management")
>    ↕
> UserApplicationRole (assigns roles to users per tenant+company)
> ```
>
> All four values must be identical for the token pipeline to work.

### Application Fields

| Field                     | Description                                                                                                                                      |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Id`                      | **Must exactly match** `ApiResource.Name` and the Client's `application_id` property. Use lowercase with hyphens (e.g., `personnel-management`). |
| `Display Name`            | Human-readable name (e.g., `"Personnel Management"`)                                                                                             |
| `Description`             | Optional description                                                                                                                             |
| `Allow Self Registration` | Whether users can self-assign roles (reserved for future use)                                                                                    |
| `Active`                  | Must be checked                                                                                                                                  |

### Step-by-Step: Creating the Personnel Management Application

1. Navigate to **Applications → Add**.
2. Fill in:
   - **ID:** `personnel-management` ← **must exactly match** `ApiResource.Name`
   - **Display Name:** `Personnel Management`
   - **Description:** `HR and Personnel Operations Application`
3. Click **Create**.
4. Open the Application **Details** page and navigate to the **Roles** tab.
5. Click **Add Role** and create each business role:

**Role 1:**

- **Name:** `PersonnelAdmin`
- **Description:** `Full administrative access to personnel records`
- **Default Role:** ☐ (unchecked)

**Role 2:**

- **Name:** `PersonnelManager`
- **Description:** `Manage team members and approve leave requests`
- **Default Role:** ☐ (unchecked)

**Role 3:**

- **Name:** `PersonnelUser`
- **Description:** `View own records and submit requests`
- **Default Role:** ✅ (checked) ← **this is the minimum-access role auto-assigned to new members**

6. Navigate to the **Linked Clients** tab to verify the `personnel-spa` client appears. If not, go to **Clients → personnel-spa → Properties** and add the `application_id` property (see Section 6, Step 11).

---

## 11. Section: Memberships

**Route:** `/Memberships`
**Required Role:** `PlatformAdmin` (full access) or `TenantOwner`/`TenantAdmin` (scoped to their tenant)

### Purpose

A **Membership** is the record that authorizes a User to access a specific workspace. Without a membership, a user cannot obtain a workspace-scoped access token — IdentityServer will reject the token request.

There are two kinds of memberships:

| Type               | CompanyId | Context Type | Roles Assigned                                |
| ------------------ | --------- | ------------ | --------------------------------------------- |
| **Tenant-scoped**  | `NULL`    | `tenant`     | `TenantOwner`, `TenantAdmin`, `TenantManager` |
| **Company-scoped** | `<GUID>`  | `company`    | `CompanyAdmin`, `CompanyManager`, `Member`    |

A user typically needs **both** — one Tenant membership and one Company membership per company they work in.

### Membership Fields

| Field       | Description                               |
| ----------- | ----------------------------------------- |
| `UserId`    | The user being given access               |
| `TenantId`  | Which tenant                              |
| `CompanyId` | Which company (`NULL` for tenant-scoped)  |
| `Status`    | `Active` / `Inactive` / `Suspended`       |
| `JoinedAt`  | Timestamp when the membership was created |

### Workspace Roles (assigned after membership is created)

| Code              | DisplayName     | ScopeType |
| ----------------- | --------------- | --------- |
| `TENANT_OWNER`    | Tenant Owner    | Tenant    |
| `TENANT_ADMIN`    | Tenant Admin    | Tenant    |
| `TENANT_MANAGER`  | Tenant Manager  | Tenant    |
| `COMPANY_ADMIN`   | Company Admin   | Company   |
| `COMPANY_MANAGER` | Company Manager | Company   |
| `MEMBER`          | Member          | Company   |

### Step-by-Step: Creating a Tenant Membership (PlatformAdmin path)

1. Navigate to **Tenants → Details** for the target tenant.
2. Click the **Members** tab → **Add Member**.
3. Search for the user by email and select them.
4. Set **Context**: `Tenant` and leave **Company** blank.
5. Click **Create**.
6. The membership is created. Open its **Details** and assign the workspace role:
   - Click **Add Role** and select `TenantAdmin`.

### Step-by-Step: Creating a Company Membership (PlatformAdmin path)

1. Navigate to **Tenants → Details** → **Companies** → select the company.
2. Click **Members → Add Member**.
3. Search for the user by email.
4. Set **Context**: `Company` with the target company pre-selected.
5. Click **Create**.
6. Open the membership **Details** and assign:
   - Click **Add Role** and select `CompanyAdmin` or `Member`.

### Step-by-Step: Assigning an Application Role to a User (via Memberships)

Application roles authorize what the user can do _inside a business application_ (e.g., Personnel Management). They are assigned per company-scoped membership.

> **Default role auto-assignment (no manual step needed for new members):** If an `EnterpriseApplicationRole` has `IsDefault = true`, Codx.Auth automatically assigns it on the user's first workspace token exchange — as long as the user has an active membership and the token request includes the application's scope. No admin action is required for baseline access.

#### SPA: Listing available application roles (for a role assignment picker)

```http
GET /api/v1/applications/{appId}/roles
Authorization: Bearer <workspace-scoped-access-token>
```

Response:

```json
[
  {
    "id": "...",
    "name": "PersonnelAdmin",
    "description": "...",
    "isDefault": false
  },
  {
    "id": "...",
    "name": "PersonnelUser",
    "description": "...",
    "isDefault": true
  }
]
```

#### SPA: Assigning an elevated role (`CompanyAdmin` / `TenantAdmin` only)

> **Note:** The `POST` and `DELETE` endpoints require the caller's token to carry `workspace_role: CompanyAdmin`, `TenantAdmin`, or `PlatformAdministrator`. Regular members cannot assign roles to other users.

```http
POST /api/v1/applications/{appId}/user-roles
Authorization: Bearer <workspace-scoped-access-token>
Content-Type: application/json

{
  "userId":    "<target-user-guid>",
  "tenantId":  "<tenant-guid-from-your-own-token-claim>",
  "companyId": "<company-guid-from-your-own-token-claim>",
  "roleId":    "<enterprise-application-role-guid>"
}
```

Expected response: `201 Created`

To find the `roleId`, use the `GET /api/v1/applications/{appId}/roles` endpoint above, or check the Applications page **Roles** tab for the GUIDs.

---

## 12. Section: Invitations

**Route:** `/Invitations`
**Required Role:** `PlatformAdmin`, `TenantOwner`, `TenantAdmin`, or `CompanyAdmin`

### Purpose

**Invitations** are the recommended way to add new users to a Tenant or Company. An invitation email is sent with a secure one-time link. When the recipient registers or logs in, they are automatically granted the specified workspace role(s).

Invitations follow a **POST-based token consumption flow** to prevent token leakage via browser history, server logs, and HTTP Referer headers:

```
1. GET  /invite/{rawToken}       → Renders auto-submitting form
2. POST /invite/consume          → Token consumed from body (never in URL)
3. Redirect to /account/register or /account/login
4. User registers/logs in
5. AcceptInvitationAsync() creates Membership + MembershipRole(s)
```

The raw token is **never stored** in the database — only its SHA-256 hash (`InviteTokenHash`) is stored.

### Invitation Fields

| Field        | Description                                                                                  |
| ------------ | -------------------------------------------------------------------------------------------- |
| `Email`      | Recipient's email address                                                                    |
| `TenantId`   | Which tenant the invitation is for                                                           |
| `CompanyId`  | Which company (`NULL` for tenant-scoped invitations)                                         |
| `Roles`      | One or more workspace roles to assign on acceptance                                          |
| `Expires At` | Configurable expiry (default: 7 days — set in `appsettings.json` via `InvitationExpiryDays`) |

### Step-by-Step: Inviting a User to a Company

1. Navigate to **Invitations → Create** (or from the Company/Tenant details page).
2. Fill in:
   - **Email:** recipient's email
   - **Tenant:** select the target tenant
   - **Company:** select the target company (leave blank for tenant-scoped)
   - **Roles:** select `CompanyAdmin` or `Member`
3. Click **Send Invitation**.
4. The system generates a secure token, sends the email, and stores only the hash.
5. The recipient clicks the link, auto-submits the token, and is redirected to register or log in.
6. After login/registration, the membership and roles are created automatically.

> **After invitation acceptance**, workspace roles are created automatically. Application roles with `IsDefault = true` are assigned automatically on the user's first workspace token exchange (no separate step needed for baseline access). Admins assign elevated application roles via the SPA's user management UI \u2014 see Section 11.

### Invitation Status Values

| Status     | Meaning                                               |
| ---------- | ----------------------------------------------------- |
| `Pending`  | Invitation sent, waiting for acceptance               |
| `Accepted` | User accepted; membership created                     |
| `Revoked`  | Manually revoked by an admin; link is no longer valid |
| `Expired`  | Expiry date passed; link is no longer valid           |

---

## 13. End-to-End Example: Setting Up Personnel Management

This section walks through the complete setup from scratch for a hypothetical **Personnel Management** SaaS application.

### Scenario

- Tenant: `Acme Corporation`
- Company: `Acme - Manila Office`
- Application: `Personnel Management`
- User: `juan@acme.com` (will have `PersonnelManager` role)
- SPA: React app at `http://localhost:5173`
- API: .NET API at `http://localhost:5200`

---

### Step 1 — Create the Tenant

1. **Admin UI → Tenants → Add**
2. Name: `Acme Corporation`
3. Slug: `acme`
4. Status: `Active`
5. Click **Create** → note the Tenant ID (GUID).

---

### Step 2 — Create the Company

1. **Admin UI → Companies → Add** (or from Tenant Details → Companies → Add):
2. Tenant: `Acme Corporation`
3. Name: `Acme - Manila Office`
4. Status: `Active`
5. Click **Create** → note the Company ID (GUID).

---

### Step 3 — Create the Identity Resources (if not present)

1. **Admin UI → Identity Resources**
2. Verify `openid`, `profile`, `email` exist and are Enabled.
3. Create any that are missing.

---

### Step 4 — Create the API Scope

1. **Admin UI → Api Scopes → Add**
2. Name: `personnel-api`
3. Display Name: `Personnel Management`
4. Description: `Access to the Personnel Management API`
5. Click **Create**.

---

### Step 5 — Create the API Resource

1. **Admin UI → Api Resources → Add**
2. **Name:** `personnel-management` ← **critical: will be used as Application ID**
3. Display Name: `Personnel Management API`
4. Enabled: ✅
5. Click **Create**.
6. Open the API Resource → **Scopes** tab → Add `personnel-api`.
7. Open the API Resource → **Claims** tab → Add `email` (optional, if the API needs e-mail).

---

### Step 6 — Create the Application and its Roles

1. **Admin UI → Applications → Add**
2. **ID:** `personnel-management` ← **must exactly match ApiResource.Name**
3. Display Name: `Personnel Management`
4. Active: ✅
5. Click **Create**.
6. Open the Application → **Roles** tab:
   - Add role: Name = `PersonnelAdmin`, Description = `Full admin access`, Default Role = ☐
   - Add role: Name = `PersonnelManager`, Description = `Manage team members`, Default Role = ☐
   - Add role: Name = `PersonnelUser`, Description = `View own records`, Default Role = ✅ ← **minimum-access default**
7. Note the GUID of each role from the Roles table (you will need these for manual role assignment).

---

### Step 7 — Create the Client (SPA)

1. **Admin UI → Clients → Add**
2. Client ID: `personnel-spa`
3. Client Name: `Personnel Management SPA`
4. Application ID: `personnel-management` ← write this in the ApplicationId field on Add
5. Click **Create**.
6. Open Client **Edit**:

**Security tab / basic settings:**

| Setting                         | Value                           |
| ------------------------------- | ------------------------------- |
| Enabled                         | ✅                              |
| Require Client Secret           | ☐ (unchecked)                   |
| Require PKCE                    | ✅                              |
| Allow Offline Access            | ✅                              |
| Allow Access Tokens via Browser | ✅                              |
| Enable Local Login              | ✅                              |
| Require Consent                 | ☐ (unchecked for internal apps) |

**Token lifetimes:**

| Setting                               | Value     | Notes                                    |
| ------------------------------------- | --------- | ---------------------------------------- |
| Access Token Lifetime                 | `900`     | 15 minutes                               |
| Authorization Code Lifetime           | `300`     | 5 minutes                                |
| Absolute Refresh Token Lifetime       | `2592000` | 30 days                                  |
| Sliding Refresh Token Lifetime        | `1296000` | 15 days                                  |
| Refresh Token Usage                   | `1`       | OneTimeOnly (required by spec)           |
| Refresh Token Expiration              | `1`       | Sliding                                  |
| Update Access Token Claims on Refresh | ✅        | Re-evaluates membership on every refresh |

7. **Grant Types** tab → Add: `authorization_code`
8. **Redirect URIs** tab → Add:
   - `http://localhost:5173/callback`
9. **Post Logout Redirect URIs** tab → Add:
   - `http://localhost:5173`
10. **CORS Origins** tab → Add:
    - `http://localhost:5173`
11. **Scopes** tab → Add all of:
    - `openid`
    - `profile`
    - `email`
    - `offline_access`
    - `personnel-api`
12. **Properties** tab → Add:
    - Key: `application_id`, Value: `personnel-management`
13. Click **Save**.

---

### Step 8 — Create the User and Tenant Membership

1. **Admin UI → Users → Add** (or invite via email — see Section 12):
   - Create user `juan@acme.com`.
2. **Admin UI → Tenants → Acme Corporation → Members → Add Member**:
   - Search: `juan@acme.com`
   - Context: `Tenant`
   - Click **Create Membership**.
   - Open the membership → **Add Role** → select `TenantAdmin`.

---

### Step 9 — Create the Company Membership

1. **Admin UI → Tenants → Acme Corporation → Companies → Acme - Manila Office → Members → Add Member**:
   - Search: `juan@acme.com`
   - Context: `Company` (Acme - Manila Office)
   - Click **Create Membership**.
   - Open the membership → **Add Role** → select `CompanyAdmin`.

---

### Step 10 — Assign the Application Role

Because `PersonnelUser` is flagged as the **default role**, it is automatically assigned the first time `juan@acme.com` exchanges a refresh token with company context. No manual API call is required for baseline access.

To assign an **elevated role** (e.g. `PersonnelManager`) manually:

1. Obtain a workspace-scoped access token as a user with `workspace_role: CompanyAdmin` or `TenantAdmin`.
2. Call the application role assignment API:

```http
POST https://auth.example.com/api/v1/applications/personnel-management/user-roles
Authorization: Bearer <your-workspace-token-with-tenant_id-and-CompanyAdmin-workspace_role>
Content-Type: application/json

{
  "userId": "<juan-user-guid>",
  "tenantId": "<acme-tenant-guid>",
  "companyId": "<manila-company-guid>",
  "roleId": "<PersonnelManager-role-guid>"
}
```

Expected response: `201 Created`

To look up the `roleId`:

```http
GET https://auth.example.com/api/v1/applications/personnel-management/roles
Authorization: Bearer <workspace-token>
```

---

### Step 11 — Verify the Configuration

Run this SQL query against the identity database to confirm the full chain is wired:

```sql
-- Verify tenant + company membership exists
SELECT um.Status, um.CompanyId, wrd.Code
FROM UserMemberships um
JOIN UserMembershipRoles umr ON umr.MembershipId = um.Id
JOIN WorkspaceRoleDefinitions wrd ON wrd.Id = umr.RoleId
WHERE um.UserId = '<juan-guid>'
  AND um.TenantId = '<acme-guid>'
  AND um.CompanyId = '<manila-guid>'
  AND um.Status = 'Active';

-- Verify application role assignment exists
SELECT uar.ApplicationId, ear.Name AS RoleName
FROM UserApplicationRoles uar
JOIN EnterpriseApplicationRoles ear ON ear.Id = uar.RoleId
WHERE uar.UserId = '<juan-guid>'
  AND uar.TenantId = '<acme-guid>'
  AND uar.CompanyId = '<manila-guid>'
  AND uar.ApplicationId = 'personnel-management';

-- Verify API scope → API resource → Application link
SELECT rs.Scope, ar.Name AS ApiResourceName
FROM ApiResourceScopes rs
JOIN ApiResources ar ON ar.Id = rs.ApiResourceId
WHERE rs.Scope = 'personnel-api';
-- ar.Name MUST equal 'personnel-management' (the EnterpriseApplication.Id)
```

---

## 14. SPA Integration Reference

### 14.1 OIDC Configuration (React / oidc-client-ts)

```typescript
import { UserManager, WebStorageStateStore } from "oidc-client-ts";

const userManager = new UserManager({
  authority: "https://auth.example.com",
  client_id: "personnel-spa",
  redirect_uri: "http://localhost:5173/callback",
  post_logout_redirect_uri: "http://localhost:5173",
  response_type: "code",
  scope: "openid profile email offline_access personnel-api",
  // PKCE is automatic in oidc-client-ts
});
```

### 14.2 Step 1 — Initial Login (no workspace context)

```typescript
// Trigger login — workspace context is NOT included here
await userManager.signinRedirect();

// In the callback component:
const user = await userManager.signinRedirectCallback();
const initialAccessToken = user.access_token; // Contains: sub, email
const refreshToken = user.refresh_token;
```

### 14.3 Step 2 — Fetch Memberships

```typescript
const response = await fetch("https://auth.example.com/api/v1/memberships", {
  headers: { Authorization: `Bearer ${initialAccessToken}` },
});
const memberships = await response.json();
// Example response:
// [
//   { tenantId: "...", tenantName: "Acme Corp", contextType: "tenant", roles: ["TENANT_ADMIN"] },
//   { tenantId: "...", companyId: "...", companyName: "Acme Manila", contextType: "company", roles: ["COMPANY_ADMIN"] }
// ]
```

### 14.4 Step 3 — User Selects Workspace

Show the user a workspace selector. When they pick a company workspace, collect:

- `tenantId`
- `companyId`
- `workspaceContextType` = `"company"`

### 14.5 Step 4 — Upgrade to Workspace-Scoped Token

This is the critical step. You exchange the refresh token for a workspace-scoped token by passing the workspace parameters as **additional POST body parameters**:

```typescript
async function getWorkspaceScopedToken(
  refreshToken: string,
  tenantId: string,
  companyId: string,
): Promise<string> {
  const params = new URLSearchParams({
    grant_type: "refresh_token",
    client_id: "personnel-spa",
    refresh_token: refreshToken,
    tenant_id: tenantId,
    company_id: companyId,
    workspace_context_type: "company",
  });

  const response = await fetch("https://auth.example.com/connect/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: params.toString(),
  });

  const data = await response.json();
  // Store data.access_token and data.refresh_token
  // The new access_token now contains:
  //   tenant_id, company_id, membership_id,
  //   workspace_context_type: "company",
  //   workspace_role: ["COMPANY_ADMIN"],
  //   application_role: ["PersonnelManager"],
  //   workspace_session_id
  return data.access_token;
}
```

### 14.6 Step 5 — Call the Business API

```typescript
const workspaceToken = await getWorkspaceScopedToken(
  refreshToken,
  tenantId,
  companyId,
);

const employeesResponse = await fetch(
  "https://personnel-api.example.com/api/employees",
  {
    headers: { Authorization: `Bearer ${workspaceToken}` },
  },
);
```

### 14.7 Workspace Switching

When the user switches to a different company, perform a new refresh token exchange with the new `tenant_id` / `company_id`. The previous workspace session is automatically revoked by IdentityServer. The previous refresh token is invalidated (OneTimeOnly) and a new one is issued.

### 14.8 Decoded Workspace-Scoped Access Token Example

```json
{
  "iss": "https://auth.example.com",
  "aud": "personnel-management",
  "sub": "a1b2c3d4-e5f6-...",
  "client_id": "personnel-spa",
  "exp": 1743500000,
  "email": "juan@acme.com",
  "tenant_id": "e5f6a7b8-...",
  "company_id": "c9d0e1f2-...",
  "membership_id": "d1e2f3a4-...",
  "workspace_context_type": "company",
  "workspace_session_id": "f4a5b6c7-...",
  "workspace_role": ["COMPANY_ADMIN"],
  "application_role": ["PersonnelManager"]
}
```

### 14.9 PersonnelManagement API — Token Validation Example (.NET)

```csharp
// Program.cs / Startup.cs in the Personnel API
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://auth.example.com";
        options.Audience = "personnel-management"; // Must match ApiResource.Name
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
    });

// In a controller — reading claims
var tenantId  = Guid.Parse(User.FindFirst("tenant_id")!.Value);
var companyId = Guid.Parse(User.FindFirst("company_id")!.Value);
var appRoles  = User.FindAll("application_role").Select(c => c.Value).ToList();

// Example authorization check
if (!appRoles.Contains("PersonnelAdmin") && !appRoles.Contains("PersonnelManager"))
    return Forbid();
```

---

## 15. Troubleshooting: No Roles in Access Token

If the Personnel Management API returns 403 or the access token contains no `application_role` claims, work through this checklist in order:

### Checklist

**1. Is `EnableWorkspaceContext` set to `true`?**

```json
// appsettings.json
{
  "EnableWorkspaceContext": true
}
```

If false, the workspace pipeline does not run.

**2. Is the token requested with company context?**

The refresh token exchange request body must include:

```
tenant_id=<guid>
company_id=<guid>
workspace_context_type=company
```

Without `company_id`, `ResolveApplicationRolesAsync` returns empty immediately.

**3. Does `ApiResource.Name` match `EnterpriseApplication.Id` exactly?**

Query:

```sql
SELECT ar.Name AS ApiResourceName, ea.Id AS AppId
FROM ApiResources ar
CROSS JOIN EnterpriseApplications ea
WHERE ar.Name = 'personnel-management' AND ea.Id = 'personnel-management';
-- Both must return rows, and the values must match
```

**4. Is the API Scope linked to the API Resource?**

```sql
SELECT rs.Scope, ar.Name
FROM ApiResourceScopes rs
JOIN ApiResources ar ON ar.Id = rs.ApiResourceId
WHERE rs.Scope = 'personnel-api';
-- Name must be: personnel-management
```

**5. Did the client request the API scope in the token?**

The refresh token exchange must include `scope=personnel-api` (or it must be in the initial authorization request's scope list). If the client doesn't request the scope, IdentityServer will not include it, and the API Resource lookup in `ResolveApplicationRolesAsync` returns nothing.

**6. Does the `UserApplicationRole` row exist?**

```sql
SELECT uar.Id, ear.Name AS RoleName
FROM UserApplicationRoles uar
JOIN EnterpriseApplicationRoles ear ON ear.Id = uar.RoleId
WHERE uar.UserId = '<user-guid>'
  AND uar.TenantId = '<tenant-guid>'
  AND uar.CompanyId = '<company-guid>'
  AND uar.ApplicationId = 'personnel-management';
-- If no rows: application role has not been assigned to this user
```

If this query returns no rows:

- **Check whether a default role is configured** for this application:

  ```sql
  SELECT Name, IsDefault FROM EnterpriseApplicationRoles
  WHERE ApplicationId = 'personnel-management' AND IsActive = 1;
  ```

  If `IsDefault = 1` exists on a role, the auto-assignment runs the next time the user exchanges a refresh token with company context. If the user has already done that and still has no row, the auto-assign may have been skipped because a row already existed from a previous state — check for soft-deleted or stale records.

- **To manually assign,** use `GET /api/v1/applications/personnel-management/roles` to find role GUIDs, then `POST /api/v1/applications/personnel-management/user-roles` (requires `CompanyAdmin` or `TenantAdmin` workspace role).

**7. Is the active membership present?**

```sql
SELECT um.Id, um.Status
FROM UserMemberships um
WHERE um.UserId = '<user-guid>'
  AND um.TenantId = '<tenant-guid>'
  AND um.CompanyId = '<company-guid>'
  AND um.Status = 'Active';
-- Must return one row with Status = 'Active'
```

**8. Is the EnterpriseApplicationRole active?**

```sql
SELECT Id, Name, IsActive
FROM EnterpriseApplicationRoles
WHERE ApplicationId = 'personnel-management';
-- All roles must have IsActive = 1
```

### Resolution Map

| Symptom                                  | Likely Cause                              | Fix                                                                                                                  |
| ---------------------------------------- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `application_role` missing entirely      | No `UserApplicationRole` row              | Check `IsDefault` on roles; re-exchange token. Or manually assign via `POST /api/v1/applications/{appId}/user-roles` |
| `application_role` missing               | `company_id` not sent in refresh exchange | Fix SPA token request to include `company_id`                                                                        |
| `application_role` missing               | `ApiResource.Name` ≠ `Application.Id`     | Correct one to match the other                                                                                       |
| `application_role` missing               | API Scope not linked to API Resource      | Add scope to API Resource's Scopes tab                                                                               |
| Token request rejected (`invalid_grant`) | No active `UserMembership`                | Create membership and assign workspace role                                                                          |
| Token request rejected                   | Tenant or Company inactive                | Set Status to Active                                                                                                 |
| 403 on API                               | Wrong `audience` in API's JWT validation  | Set `Audience = "personnel-management"` (matching `ApiResource.Name`)                                                |
| 403 on `POST /user-roles`                | Caller lacks admin workspace role         | Token must contain `workspace_role: CompanyAdmin` or `TenantAdmin`                                                   |
