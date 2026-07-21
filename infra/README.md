# Deploying to Azure

Terraform-managed infrastructure + a GitHub Actions pipeline that builds,
tests, and deploys both `backend/Acordanza.Api` and `frontend` to Azure
Container Apps on every push to `main`.

## Architecture

```
Resource group (rg-acordanza-prod, created by scripts/bootstrap-azure.sh)
├── Azure Container Registry (Basic)            — holds built API + web images
├── Log Analytics workspace                     — Container Apps logs
├── Container Apps Environment
│   ├── Container App (ca-acordanza-prod)       — the API, scale-to-zero
│   └── Container App (ca-acordanza-prod-web)   — the frontend (nginx), scale-to-zero
│       └── shared user-assigned managed identity — AcrPull on the registry
└── Postgres Flexible Server (Burstable B1ms)
    └── database: acordanza
```

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
the JS bundle at Docker build time (as a build arg pointing at the API
Container App's FQDN), since Vite env vars can't be swapped at runtime once
bundled. The backend's CORS config, in turn, includes the frontend
Container App's FQDN automatically (`local.frontend_origin` in
`infra/terraform/container_app.tf`, derived from
`azurerm_container_app.frontend`) — no manual origin bookkeeping needed
unless you add a custom domain later (`cors_allowed_origins`).

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
     beyond the frontend Container App's own URL, which is added
     automatically (see the "Architecture" section above). Fine to leave as
     `[]` unless you're adding a custom domain, e.g.
     `["https://acordanza.example.com"]`.

3. **Set GitHub Actions repo secrets** (same page, Secrets tab):
   - `POSTGRES_ADMIN_PASSWORD` — a strong password (e.g.
     `openssl rand -base64 24`).
   - `JWT_KEY` — the JWT signing key (e.g. `openssl rand -base64 48`). This
     is a *different* key than the one used for local dev.

4. **Push to `main`, or run the workflow manually** (Actions → "CI/CD" → Run
   workflow). The first run applies the Terraform plan (creating the ACR,
   Postgres server, Container Apps environment and both apps — this takes
   several minutes, mostly waiting on the Postgres server), then builds and
   deploys both real images. `VITE_API_URL` is baked into the frontend image
   automatically from the API app's FQDN — no manual step needed.

5. Once it's up, grab either Container App's URL:
   ```
   az containerapp show -g rg-acordanza-prod -n ca-acordanza-prod --query properties.configuration.ingress.fqdn -o tsv
   az containerapp show -g rg-acordanza-prod -n ca-acordanza-prod-web --query properties.configuration.ingress.fqdn -o tsv
   ```

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
3. **terraform** — needs both build-test jobs; OIDC login, `terraform plan`
   (always, including on PRs), `terraform apply` (only on push to `main`).
4. **build-and-deploy-backend** / **build-and-deploy-frontend** — run in
   parallel after `terraform`. Each builds its Docker image, pushes it to
   ACR, then `az containerapp update`s its Container App to the new image
   tag. The frontend build passes the API app's FQDN as a `VITE_API_URL`
   Docker build arg. Only on push to `main`.

**Caveat:** the `terraform` job runs `plan` on pull requests too, which
requires real Azure credentials to be available to the PR's workflow run.
That's fine for a private repo with trusted contributors (which this is),
but would be a credential-exposure risk if this repo ever went public or
started accepting external PRs — the fix at that point would be to drop the
PR trigger for the `terraform` job, or gate it behind an approval
(`environment:` protection rules).
