# Deploying the backend to Azure

Terraform-managed infrastructure + a GitHub Actions pipeline that builds,
tests, and deploys `backend/Acordanza.Api` to Azure Container Apps on every
push to `main`.

## Architecture

```
Resource group (rg-acordanza-prod, created by scripts/bootstrap-azure.sh)
├── Azure Container Registry (Basic)            — holds built API images
├── Log Analytics workspace                     — Container Apps logs
├── Container Apps Environment
│   └── Container App (acordanza-api)           — the API, scale-to-zero
│       └── system-assigned managed identity    — AcrPull on the registry
└── Postgres Flexible Server (Burstable B1ms)
    └── database: acordanza
```

Everything above except the resource group itself is created by Terraform
(`infra/terraform/`). The resource group is created once, out-of-band, by
`scripts/bootstrap-azure.sh` — see "Why the resource group isn't in
Terraform" below.

Terraform manages the *infrastructure*; it does **not** manage which image
tag the Container App is running. CI builds and pushes a new image on every
push to `main`, then updates the running revision directly with
`az containerapp update --image ...`. This avoids a chicken-and-egg problem
(the Container App can't be created pointing at an image that doesn't exist
yet in a registry that also doesn't exist yet) and means deploys aren't
gated on a `terraform apply`.

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
- **Azure Container Registry**, not GHCR, so the Container App can pull
  images using its own managed identity — no registry credentials stored in
  GitHub Actions or anywhere else.
- **`mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0-distroless-extra`**
  as the runtime base image (see `backend/Acordanza.Api/Dockerfile`). No
  shell, no package manager, non-root by default — smallest attack surface
  Microsoft ships for .NET. (Azure Linux's minimal variant is called
  "distroless", not "chiseled" — chiseled is Ubuntu-specific naming; same
  idea.) The `-extra` variant adds back ICU/tzdata/openssl so date and
  culture handling behaves normally instead of falling back to invariant
  globalization.
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
   - `CORS_ALLOWED_ORIGINS` — JSON array of allowed frontend origins, e.g.
     `["https://acordanza.example.com"]`.

3. **Set GitHub Actions repo secrets** (same page, Secrets tab):
   - `POSTGRES_ADMIN_PASSWORD` — a strong password (e.g.
     `openssl rand -base64 24`).
   - `JWT_KEY` — the JWT signing key (e.g. `openssl rand -base64 48`). This
     is a *different* key than the one used for local dev.

4. **Push to `main`, or run the workflow manually** (Actions →
   "Backend CI/CD" → Run workflow). The first run applies the Terraform plan
   (creating the ACR, Postgres server, Container Apps environment and app —
   this takes several minutes, mostly waiting on the Postgres server) and
   deploys the first real image.

5. Once it's up, grab the Container App's URL:
   ```
   az containerapp show -g rg-acordanza-prod -n ca-acordanza-prod --query properties.configuration.ingress.fqdn -o tsv
   ```
   and use it as `VITE_API_URL` when building the frontend.

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

## CI/CD pipeline (`.github/workflows/backend-ci-cd.yml`)

1. **build-test** — `dotnet build` + `dotnet test` (currently a no-op: no
   test project exists yet; the step picks one up automatically once one's
   added).
2. **terraform** — OIDC login, `terraform plan` (always, including on PRs),
   `terraform apply` (only on push to `main`).
3. **build-and-deploy** — builds the Docker image, pushes it to ACR, then
   `az containerapp update`s the running app to the new image tag. Only on
   push to `main`.

**Caveat:** the `terraform` job runs `plan` on pull requests too, which
requires real Azure credentials to be available to the PR's workflow run.
That's fine for a private repo with trusted contributors (which this is),
but would be a credential-exposure risk if this repo ever went public or
started accepting external PRs — the fix at that point would be to drop the
PR trigger for the `terraform` job, or gate it behind an approval
(`environment:` protection rules).
