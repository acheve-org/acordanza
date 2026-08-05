# Deploying to Azure

Terraform-managed infrastructure + a GitHub Actions pipeline that builds,
tests, and deploys `backend/Acordanza.Api` and `frontend` to Azure Container
Apps, and `landing` to an Azure Static Web App, on every push to `main`.

## Architecture

```
Resource group (rg-acordanza-prod, created by scripts/bootstrap-azure.sh)
├── Azure Container Registry (Basic)            — holds built API + web images
├── Log Analytics workspace                     — Container Apps logs
├── Container Apps Environment
│   ├── Container App (ca-acordanza-prod)       — the API, scale-to-zero
│   └── Container App (ca-acordanza-prod-web)   — the frontend (nginx), scale-to-zero
│       └── shared user-assigned managed identity — AcrPull on the registry
├── Static Web App (stapp-acordanza-prod-landing) — marketing landing page (landing/)
├── Postgres Flexible Server (Burstable B2s)
│   └── database: acordanza
└── Storage Account (stacordanzaprod<random>)     — topo images, private container
    └── container: topos                          — read via backend-issued SAS URLs only
```

The landing page is a separate Azure primitive from the two Container Apps
above — a free-tier static-file host (`azurerm_static_web_app` in
`infra/terraform/static_web_app.tf`), not a container/registry/nginx setup.
It only exists to explain what Acordanza is at the bare `acordanza.eu` apex;
the actual app lives on the Container Apps.

Everything above except the resource group itself is created by Terraform
(`infra/terraform/`). The resource group is created once, out-of-band, by
`scripts/bootstrap-azure.sh` — see "Why the resource group isn't in
Terraform" below.

Terraform manages the *infrastructure*; it does **not** manage which image
tag either Container App is running. CI builds and pushes a new image on
every push to `main`, then updates the running revision directly with
`az containerapp update --image ...`. This avoids a chicken-and-egg problem
(the Container App can't be created pointing at an image that doesn't exist
yet in a registry that also doesn't exist yet) and means deploys aren't
gated on a `terraform apply`.

The frontend is a static Vite build (`bun run build` → `dist/`) served by
`nginx-unprivileged` inside its container — `VITE_API_URL` is baked into
the JS bundle at Docker build time (as a build arg pointing at the API's
custom domain, `var.backend_custom_domain`), since Vite env vars can't be
swapped at runtime once bundled. The backend's CORS config, in turn,
includes both the frontend Container App's FQDN and its custom domain
automatically (`local.frontend_origin` / `local.frontend_custom_origin` in
`infra/terraform/container_app.tf`) — `cors_allowed_origins` is only for
extra origins beyond those two. The backend's WebAuthn/passkey config
(`Fido2:ServerDomain`) reuses that same origin list for its allowed
origins; `var.fido2_server_domain` (the relying party ID) is set
separately to the apex `acordanza.eu`, since it must be the frontend's
own origin or a registrable parent of it, not the API's domain.

Both apps are also bound to custom domains with free Azure-managed
certificates — see "Custom domains" below.

## Decisions made and why

- **Azure Container Apps**, not App Service or AKS. This is a low-traffic
  app for a handful of friends — Container Apps' scale-to-zero means it
  costs close to nothing when nobody's climbing-logging, at the cost of a
  cold start on the first request after idling. App Service's cheapest
  always-on tier would cost more for less. AKS is way more than this needs.
- **OIDC federated credentials**, not a service principal secret. No
  long-lived credential is stored anywhere; GitHub exchanges a short-lived
  token for an Azure access token on every run. Same reasoning applies to
  Terraform's state backend (`use_azuread_auth = true` in
  `infra/terraform/providers.tf`) — no storage account key either.
- **Azure Container Registry**, not GHCR, so both Container Apps can pull
  images using a shared user-assigned managed identity — no registry
  credentials stored in GitHub Actions or anywhere else.
- **`mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0-distroless-extra`**
  as the API's runtime base image (see `backend/Acordanza.Api/Dockerfile`).
  No shell, no package manager, non-root by default — smallest attack
  surface Microsoft ships for .NET. (Azure Linux's minimal variant is called
  "distroless", not "chiseled" — chiseled is Ubuntu-specific naming; same
  idea.) The `-extra` variant adds back ICU/tzdata/openssl so date and
  culture handling behaves normally instead of falling back to invariant
  globalization.
- **`nginxinc/nginx-unprivileged`** as the frontend's runtime base image
  (see `frontend/Dockerfile`) — serves the static Vite build. Non-root by
  default and listens on 8080 by default, matching the API's non-root/8080
  conventions without extra config.
- **Postgres Flexible Server with public access + an "allow Azure services"
  firewall rule**, not a VNet-integrated private endpoint. Container Apps'
  Consumption plan doesn't have a stable outbound IP without a VNet + NAT
  gateway, which is meaningfully more infrastructure than a small
  friends-only app needs. The connection is still password-authenticated and
  TLS-encrypted. If the trust model ever changes, tightening this to a
  private endpoint is a contained follow-up.
- **No Key Vault encryption for the Data Protection key ring**
  (`storage.tf`'s `dataprotection` container, wired up in `Program.cs`) —
  ASP.NET Core logs "No XML encryptor configured" as a result, and that's
  accepted. The keys are protected by the same access control as everything
  else in that storage account: a private container, no anonymous path in,
  reachable only with an Azure AD token from the container app's managed
  identity. `ProtectKeysWithAzureKeyVault` would wrap each key with a Key
  Vault key before it's written, so a leaked blob alone wouldn't be usable —
  but that's a second Azure resource, an RBAC grant, and ongoing cost for a
  key ring whose worst-case blast radius (a forged password-reset token) is
  already narrow for a handful of trusted friends. Revisit if a Key Vault
  ever gets provisioned for another reason.

## One-time setup

1. **Bootstrap Azure resources and GitHub OIDC trust:**
   ```
   az login
   ./scripts/bootstrap-azure.sh
   ```
   This creates the Terraform state storage account, the app resource
   group, an Entra ID app registration with federated credentials trusting
   this GitHub repo, and scoped role assignments. It prints the exact
   values to set in step 2.

2. **Set GitHub Actions repo variables** (Settings → Secrets and variables →
   Actions → Variables):
   - `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` — from the
     bootstrap script's output.
   - `CORS_ALLOWED_ORIGINS` — JSON array of *additional* allowed origins
     beyond the frontend Container App's own URL and its custom domain,
     both added automatically (see the "Architecture" section above). Fine
     to leave as `[]` unless something else needs to call the API, e.g.
     `["https://acordanza.example.com"]`.
   - `GRAFANA_OTLP_ENDPOINT` — the Grafana Cloud OTLP gateway URL for the
     stack traces/metrics/logs are exported to (Grafana Cloud UI →
     Connections → OpenTelemetry), e.g.
     `https://otlp-gateway-prod-xx-xxx.grafana.net/otlp`. Not secret — it's
     just an endpoint, the credential is the header below.

3. **Set GitHub Actions repo secrets** (same page, Secrets tab):
   - `POSTGRES_ADMIN_PASSWORD` — a strong password (e.g.
     `openssl rand -base64 24`).
   - `JWT_KEY` — the JWT signing key (e.g. `openssl rand -base64 48`). This
     is a *different* key than the one used for local dev.
   - `RESEND_API_KEY` — a Resend API key, used to send password-reset
     emails (`Resend:ApiKey` in the backend). Resend isn't provisioned by
     Terraform — create the account and key yourself, then also verify a
     domain for `var.resend_from_email` (Resend's domain verification, which
     adds DKIM/SPF-related DNS records — this doesn't touch or conflict with
     the existing Gandi mail SPF record noted under "Custom domains" below,
     as long as you merge Resend's `include:` into the existing SPF TXT
     record rather than adding a second one). Without a verified domain,
     `Resend:FromEmail` will fail to send or land in spam.
   - `GRAFANA_OTLP_HEADERS` — the OTLP auth header for the Grafana Cloud
     stack above, in `Authorization=Basic <base64 instanceId:token>` form
     (generate the token alongside the endpoint in the same Grafana Cloud
     screen). Grafana isn't provisioned by Terraform — create the stack and
     token yourself.

4. **Push to `main`, or run the workflow manually** (Actions → "CI/CD" → Run
   workflow). The first run applies the Terraform plan (creating the ACR,
   Postgres server, Container Apps environment and both apps — this takes
   several minutes, mostly waiting on the Postgres server), then builds and
   deploys both real images. `VITE_API_URL` is baked into the frontend image
   automatically from the API's custom domain — no manual step needed,
   **except** the custom-domain DNS records described in "Custom domains"
   below must exist before the very first apply that adds them, or that
   `terraform apply` fails.

5. Once it's up, grab either Container App's URL:
   ```
   az containerapp show -g rg-acordanza-prod -n ca-acordanza-prod --query properties.configuration.ingress.fqdn -o tsv
   az containerapp show -g rg-acordanza-prod -n ca-acordanza-prod-web --query properties.configuration.ingress.fqdn -o tsv
   ```

## Custom domains

`app.acordanza.eu` (frontend) and `api.acordanza.eu` (backend) are bound as
custom domains with free Azure-managed certificates
(`azurerm_container_app_custom_domain` +
`azurerm_container_app_environment_managed_certificate` in
`infra/terraform/container_app.tf` and `container_app_frontend.tf`,
controlled by `var.frontend_custom_domain` / `var.backend_custom_domain`).

Azure validates domain ownership and issues the certificate synchronously
during `terraform apply`, so **the DNS records below must already exist and
have propagated before applying** (or CI's `terraform apply` step fails and
blocks every subsequent deploy until it's fixed). At the DNS provider
hosting `acordanza.eu`, create:

| Record | Host | Value |
| --- | --- | --- |
| CNAME | `app` | the frontend Container App's FQDN (`terraform output container_app_frontend_fqdn`) |
| TXT | `asuid.app` | `terraform output custom_domain_verification_id` |
| CNAME | `api` | the backend Container App's FQDN (`terraform output container_app_fqdn`) |
| TXT | `asuid.api` | `terraform output custom_domain_verification_id` (same value — it's shared per environment) |

Both Container Apps live in the same environment, so the verification ID
is identical for both. If `acordanza.eu` has a CAA record, it must allow
DigiCert as an issuer (`0 issue digicert.com`) or certificate issuance
fails.

Once DNS resolves, `terraform apply` creates the custom domain (unbound)
and the managed certificate, and Azure auto-binds the certificate to the
domain out-of-band — that's why `certificate_binding_type` and
`container_app_environment_certificate_id` are in `ignore_changes` on both
`azurerm_container_app_custom_domain` resources. Issuance can take a few
minutes after DNS propagates.

### Landing page apex domain

The landing page is bound to the bare `acordanza.eu` apex
(`azurerm_static_web_app_custom_domain` in
`infra/terraform/static_web_app.tf`, controlled by
`var.landing_custom_domain`). Apex domains can't be pointed at anything with
a CNAME (that would conflict with any other record at the zone root, e.g.
MX), so this uses `dns-txt-token` validation and either an `ALIAS`/`ANAME`
record (if your DNS provider supports one) or a plain `A` record, instead of
the CNAME the two subdomains above use.

Unlike the Container Apps custom domains, DNS does **not** need to exist
before applying — creating the custom domain resource generates a
validation token first, which is what goes in the TXT record. Azure then
validates asynchronously in the background; Terraform doesn't wait on it,
so the domain shows as "pending validation" in the Azure portal until DNS
propagates. Order of operations:

1. `terraform apply` (creates the Static Web App and the pending custom
   domain, generating the validation token).
2. At the DNS provider hosting `acordanza.eu`, create:

   | Record | Host | Value |
   | --- | --- | --- |
   | TXT | `@` | `terraform output -raw landing_custom_domain_validation_token` |
   | ALIAS/ANAME | `@` | `terraform output -raw static_web_app_default_host_name` |

   If your DNS provider doesn't support `ALIAS`/`ANAME` at the zone apex,
   use an `A` record pointed at the Static Web App's `stableInboundIP`
   instead. That property isn't in `az staticwebapp show`'s default output
   (it's missing from that command's projection) or exposed as a Terraform
   output — fetch it via the generic resource API instead:
   ```
   az resource show --resource-group rg-acordanza-prod \
     --name stapp-acordanza-prod-landing \
     --resource-type Microsoft.Web/staticSites \
     --query "properties.stableInboundIP" -o tsv
   ```
   An `A` record pins you to a single region instead of Azure's global
   edge, which doesn't matter much for a low-traffic landing page.

   `acordanza.eu`'s nameservers are Gandi's LiveDNS (`ns-*.gandi.net`,
   confirmed via `dig NS acordanza.eu`) — LiveDNS supports `ALIAS` records
   at the apex (Gandi's legacy "Classic" DNS doesn't), so either option
   above works. Gandi won't let an `A` and an `ALIAS` coexist on `@`, so
   whichever you pick replaces the existing apex `A` record — check what's
   there first, since this domain already has mail hosted at Gandi (an SPF
   `TXT` record referencing `_mailcust.gandi.net`); leave that TXT record
   and any `MX` records alone.
3. Wait for DNS to propagate and Azure to validate in the background (no
   further `terraform apply` needed).

Static Web Apps' managed certificate is also issued by DigiCert, same as
the Container Apps above — if `acordanza.eu` ever gets a CAA record, it
needs the same `0 issue "digicert.com"` allowance or issuance fails.

### Why the resource group isn't in Terraform

The GitHub Actions service principal's role assignment is scoped to the
*single* app resource group (least privilege — it can't touch anything else
in the subscription). A principal scoped to one resource group cannot create
*new* resource groups; that needs subscription-level rights, which we
deliberately don't grant a CI pipeline. So the resource group is created
once, manually, by whoever runs the bootstrap script with their own
(broader) permissions — Terraform then treats it as a `data` source
(`infra/terraform/main.tf`) and manages everything inside it.

## Running Terraform locally

```
cd infra/terraform
terraform init \
  -backend-config="resource_group_name=rg-acordanza-tfstate" \
  -backend-config="storage_account_name=stacordanzatfstate" \
  -backend-config="container_name=tfstate" \
  -backend-config="key=acordanza.tfstate"

cp terraform.tfvars.example terraform.tfvars   # edit non-sensitive values
export TF_VAR_postgres_admin_password="..."
export TF_VAR_jwt_key="..."

terraform plan
```

You'll need the same Azure role assignments as the CI service principal
(or broader) for this to work — `az login` as yourself is enough if you're
the one who ran the bootstrap script.

## CI/CD pipeline (`.github/workflows/ci-cd.yml`)

1. **build-test-backend** — `dotnet build` + `dotnet test` (currently a
   no-op: no test project exists yet; the step picks one up automatically
   once one's added).
2. **build-test-frontend** — `bun install`, `bun run lint`, `bun run build`
   (typecheck + Vite build, as a compile sanity check — the artifact is
   discarded; the real build happens in the Docker build below with the
   real `VITE_API_URL`).
3. **security-scan** — Trivy scans the Terraform config and both
   Dockerfiles for misconfigurations plus the whole repo for leaked
   secrets, and zizmor scans the workflows themselves; a finding fails
   the pipeline. Runs on PRs too (it needs no Azure credentials). See
   "Security scanning" below for how findings are suppressed and how to
   run it locally.
4. **terraform** — needs both build-test jobs and security-scan (so insecure
   infra config can never reach `apply`); OIDC login, `terraform plan`
   (always, including on PRs), `terraform apply` (only on push to `main`).
5. **build-and-deploy-backend** / **build-and-deploy-frontend** — run in
   parallel after `terraform`. Each builds its Docker image, pushes it to
   ACR, then `az containerapp update`s its Container App to the new image
   tag. The frontend build passes the API's custom domain as a
   `VITE_API_URL` Docker build arg. Only on push to `main`.
6. **deploy-landing** — also runs after `terraform`, in parallel with the
   two jobs above. Static Web Apps' deploy action doesn't support the
   OIDC-based Azure login the other jobs use, only its own deployment
   token — so this job fetches that token fresh from Azure on every run
   (`az staticwebapp secrets list`, masked with `::add-mask::`) rather than
   storing it as a long-lived GitHub secret, then uploads `landing/`
   directly (no Docker image, no build step — it's plain static HTML).
   Only on push to `main`.

**Caveat:** the `terraform` job runs `plan` on pull requests too, which
requires real Azure credentials to be available to the PR's workflow run.
That's fine for a private repo with trusted contributors (which this is),
but would be a credential-exposure risk if this repo ever went public or
started accepting external PRs — the fix at that point would be to drop the
PR trigger for the `terraform` job, or gate it behind an approval
(`environment:` protection rules).

## Security scanning

The `security-scan` CI job runs [Trivy](https://trivy.dev) in two modes
plus [zizmor](https://docs.zizmor.sh) on every push and PR, and the
`terraform` job depends on it — a finding blocks the deploy:

- **`trivy config`** — misconfiguration scan of the Terraform files and
  both Dockerfiles. (Trivy's config scanner has no GitHub Actions checks,
  hence the separate scanner below.)
- **`trivy fs --scanners secret`** — scans the checked-out repo for
  committed credentials.
- **`zizmor`** — GitHub Actions security scan of `.github/workflows`:
  template injection, credential leakage, unpinned/impostor actions, and
  the rest of its audit suite. Fails on any low-or-higher finding;
  informational findings are advisory only. `GH_TOKEN` is passed so the
  online audits (impostor commits, known-vulnerable actions) run too.

Run the same scans locally (`brew install trivy`; zizmor runs via pipx or
uvx):

```
trivy config --skip-dirs infra/terraform/.terraform,frontend/node_modules --exit-code 1 .
trivy fs --scanners secret --skip-dirs infra/terraform/.terraform,frontend/node_modules --exit-code 1 .
GH_TOKEN=$(gh auth token) pipx run zizmor==1.27.0 --min-severity=low .github/workflows/
```

**Suppression policy:** the scan fails on *any* severity, so every
suppressed finding must carry a written justification next to the
suppression — inline `#trivy:ignore:<id>` comments in the `.tf` files for
resource-specific decisions, `.trivyignore` at the repo root for global
ones, and inline `# zizmor: ignore[rule]` comments in the workflow files.
Nothing gets suppressed just to make CI green.

Current suppressions and why:

| ID | Where | Why |
| --- | --- | --- |
| `AVD-AZU-0022` (Postgres public network access) | `postgres.tf` | Accepted risk, documented in "Decisions made and why" above: Container Apps Consumption has no stable egress IP without VNet+NAT. Connections still require TLS 1.2+ and a password; revisit if the trust model changes. |
| `AVD-AZU-0021`, `AVD-AZU-0026` (connection throttling / min TLS) | `postgres.tf` | Scanner false positives: Trivy's adapter only recognizes the legacy single-server parameter names (`connection_throttling`, `tls_version`); the config sets the real Flexible Server parameters (`connection_throttle.enable`, `ssl_min_protocol_version`). |
| `AVD-DS-0026` (no Docker `HEALTHCHECK`) | `.trivyignore` | Container Apps ignores Docker `HEALTHCHECK` and probes the ingress port itself; the API's distroless image has no shell to run a check command anyway. |
| `AVD-AZU-0012` (storage account public network access) | `storage.tf` | Same accepted risk as Postgres above: no stable Container Apps egress IP without a VNet. Every read/write still requires a managed-identity token or a short-lived user delegation SAS — there's no anonymous path in. |
| `AVD-AZU-0057/0058/0060/0061` (storage logging / geo-redundancy / customer-managed keys / infrastructure encryption) | `storage.tf` | Same cost/complexity trade-offs already accepted for ACR/Postgres below — topo photos for a handful of friends don't need enterprise-grade redundancy, key management, or audit logging. |

Checkov (`uvx checkov -d infra/terraform`) is a useful second opinion; its
extra findings are all consciously accepted cost trade-offs: ACR
Basic-tier limitations (no private endpoints, retention policies, image
signing, or zone redundancy — all Premium-only, ~10x the cost) and Postgres
geo-redundant backup (doubles backup cost, and enabling it later forces
server replacement).

`.github/dependabot.yml` keeps the security-relevant pins fresh: GitHub
Actions versions, Terraform providers, and both Docker base images get
weekly update PRs.
