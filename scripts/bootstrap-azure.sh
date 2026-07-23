#!/usr/bin/env bash
# One-time setup, run manually (from a machine with the Azure CLI logged in
# as a user who can create resource groups / app registrations / role
# assignments in the target subscription). Not run by CI. Idempotent-ish:
# safe to re-run, `az` commands here mostly no-op or error harmlessly if the
# thing already exists, but read the output either way.
#
# What this creates, and why it can't be done by Terraform itself:
#   - The Terraform state storage account/container — Terraform can't create
#     the backend it's about to store its own state in.
#   - The app's resource group — the GitHub Actions service principal's role
#     assignment below is scoped to *this one resource group* (least
#     privilege), and a principal scoped to a single resource group cannot
#     create new resource groups (that needs subscription-level rights we
#     deliberately don't grant it).
#   - The Entra ID app registration + federated credentials + role
#     assignments that let GitHub Actions authenticate via OIDC in the first
#     place — Terraform can't grant itself its own initial permissions.
set -euo pipefail

GITHUB_REPO="acheve-org/acordanza"
LOCATION="spaincentral"

TF_STATE_RESOURCE_GROUP="rg-acordanza-tfstate"
TF_STATE_STORAGE_ACCOUNT="stacordanzatfstate" # must be globally unique; edit if taken
TF_STATE_CONTAINER="tfstate"

APP_RESOURCE_GROUP="rg-acordanza-prod"
APP_REGISTRATION_NAME="acordanza-github-actions"

echo "==> Checking Azure CLI login"
az account show >/dev/null || {
  echo "Not logged in. Run 'az login' first." >&2
  exit 1
}
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
echo "Subscription: $SUBSCRIPTION_ID"
echo "Tenant:       $TENANT_ID"

echo "==> Creating Terraform state resource group"
az group create --name "$TF_STATE_RESOURCE_GROUP" --location "$LOCATION" >/dev/null

echo "==> Creating Terraform state storage account"
az storage account create \
  --name "$TF_STATE_STORAGE_ACCOUNT" \
  --resource-group "$TF_STATE_RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku Standard_LRS \
  --min-tls-version TLS1_2 \
  --https-only true \
  --allow-blob-public-access false >/dev/null

echo "==> Enabling blob versioning (protects state against accidental overwrite)"
az storage account blob-service-properties update \
  --account-name "$TF_STATE_STORAGE_ACCOUNT" \
  --enable-versioning true >/dev/null

echo "==> Creating Terraform state container"
az storage container create \
  --name "$TF_STATE_CONTAINER" \
  --account-name "$TF_STATE_STORAGE_ACCOUNT" \
  --auth-mode login >/dev/null

echo "==> Creating app resource group"
az group create --name "$APP_RESOURCE_GROUP" --location "$LOCATION" >/dev/null

echo "==> Creating Entra ID app registration"
APP_ID=$(az ad app list --display-name "$APP_REGISTRATION_NAME" --query "[0].appId" -o tsv)
if [ -z "$APP_ID" ]; then
  APP_ID=$(az ad app create --display-name "$APP_REGISTRATION_NAME" --query appId -o tsv)
fi
echo "App (client) ID: $APP_ID"

echo "==> Creating service principal"
SP_OBJECT_ID=$(az ad sp list --filter "appId eq '$APP_ID'" --query "[0].id" -o tsv)
if [ -z "$SP_OBJECT_ID" ]; then
  SP_OBJECT_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)
fi
echo "Service principal object ID: $SP_OBJECT_ID"

echo "==> Looking up GitHub's OIDC subject-claim prefix for $GITHUB_REPO"
# GitHub repos created after 2026-07-15 default to an "immutable" subject
# format that embeds the numeric owner/repo IDs
# (repo:OWNER@OWNER_ID/REPO@REPO_ID) instead of just their names, so it
# can't be hardcoded — ask GitHub for the exact prefix it's actually
# signing tokens with, rather than guessing.
if ! command -v gh >/dev/null 2>&1; then
  echo "gh CLI not found — falling back to the plain name-based subject prefix." >&2
  echo "If your repo uses immutable subject claims this will be wrong; see" >&2
  echo "https://docs.github.com/en/actions/reference/security/oidc" >&2
  SUB_PREFIX="repo:${GITHUB_REPO}"
else
  SUB_PREFIX=$(gh api "repos/${GITHUB_REPO}/actions/oidc/customization/sub" --jq '.sub_claim_prefix')
fi
echo "Subject prefix: $SUB_PREFIX"

create_or_update_federated_credential() {
  local name="$1" subject="$2"
  local params
  params=$(cat <<EOF
{
  "name": "${name}",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "${subject}",
  "audiences": ["api://AzureADTokenExchange"]
}
EOF
  )
  if az ad app federated-credential show --id "$APP_ID" --federated-credential-id "$name" >/dev/null 2>&1; then
    az ad app federated-credential update --id "$APP_ID" --federated-credential-id "$name" --parameters "$params" >/dev/null
  else
    az ad app federated-credential create --id "$APP_ID" --parameters "$params" >/dev/null
  fi
}

echo "==> Creating/updating federated credential for pushes to main"
create_or_update_federated_credential "github-push-main" "${SUB_PREFIX}:ref:refs/heads/main"

echo "==> Creating/updating federated credential for pull requests"
create_or_update_federated_credential "github-pull-request" "${SUB_PREFIX}:pull_request"

APP_RG_ID="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${APP_RESOURCE_GROUP}"
STATE_SA_ID=$(az storage account show --name "$TF_STATE_STORAGE_ACCOUNT" --resource-group "$TF_STATE_RESOURCE_GROUP" --query id -o tsv)

echo "==> Granting Contributor on $APP_RESOURCE_GROUP (manage app resources)"
az role assignment create --assignee "$SP_OBJECT_ID" --role "Contributor" --scope "$APP_RG_ID" >/dev/null

echo "==> Granting User Access Administrator on $APP_RESOURCE_GROUP (needed for Terraform to create the AcrPull role assignment)"
az role assignment create --assignee "$SP_OBJECT_ID" --role "User Access Administrator" --scope "$APP_RG_ID" >/dev/null

echo "==> Granting Storage Blob Data Contributor on the state storage account (read/write Terraform state without a storage key)"
az role assignment create --assignee "$SP_OBJECT_ID" --role "Storage Blob Data Contributor" --scope "$STATE_SA_ID" >/dev/null

cat <<EOF

==> Done. Set these in the GitHub repo (Settings > Secrets and variables > Actions):

Variables (Settings > Secrets and variables > Actions > Variables):
  AZURE_CLIENT_ID       = $APP_ID
  AZURE_TENANT_ID       = $TENANT_ID
  AZURE_SUBSCRIPTION_ID = $SUBSCRIPTION_ID
  CORS_ALLOWED_ORIGINS  = [] (optional — only needed for additional origins
                              beyond the frontend Container App's own URL,
                              e.g. a custom domain)

Secrets (Settings > Secrets and variables > Actions > Secrets):
  POSTGRES_ADMIN_PASSWORD = <generate a strong password, e.g. openssl rand -base64 24>
  JWT_KEY                 = <generate with: openssl rand -base64 48>

See infra/README.md for the full explanation of what each of these is for.
EOF
