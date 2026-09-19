#!/usr/bin/env bash
set -Eeuo pipefail

APP_NAME="Local IP Manager"
SERVICE_NAME="local-ip-manager"
SERVICE_USER="localip"
INSTALL_DIR="/opt/local-ip-manager"
DATA_DIR="/var/lib/local-ip-manager"
REPOSITORY="jeffvpascale/local-ip-manager"
REQUESTED_VERSION="${1:-${LOCAL_IP_MANAGER_VERSION:-latest}}"

info() {
  printf '\n[INFO] %s\n' "$1"
}

fail() {
  printf '\n[ERROR] %s\n' "$1" >&2
  exit 1
}

if [[ "${EUID}" -ne 0 ]]; then
  fail "Run this installer as root."
fi

case "$(uname -m)" in
  x86_64 | amd64) ;;
  *) fail "This release currently supports x86-64/amd64 containers only." ;;
esac

command -v systemctl >/dev/null 2>&1 ||
  fail "This installer requires a systemd-based Debian container."

[[ -f /etc/os-release ]] ||
  fail "The container does not provide /etc/os-release."

source /etc/os-release

if [[ "${ID}" != "debian" ]]; then
  fail "This installer currently supports Debian containers only."
fi

case "${VERSION_ID}" in
  12)
    DOTNET_OS_PACKAGES=(libicu72 libssl3)
    ;;
  13)
    DOTNET_OS_PACKAGES=(libicu76 libssl3t64)
    ;;
  *)
    fail "This installer currently supports Debian 12 and Debian 13."
    ;;
esac

info "Installing operating-system dependencies"
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends \
  ca-certificates \
  curl \
  iproute2 \
  iputils-ping \
  libc6 \
  libgcc-s1 \
  libgssapi-krb5-2 \
  libstdc++6 \
  tzdata \
  unzip \
  zlib1g \
  "${DOTNET_OS_PACKAGES[@]}"

if [[ "${REQUESTED_VERSION}" == "latest" ]]; then
  info "Finding the latest GitHub release"
  REQUESTED_VERSION="$(
    curl -fsSL "https://api.github.com/repos/${REPOSITORY}/releases/latest" |
      sed -n 's/.*"tag_name":[[:space:]]*"\([^"]*\)".*/\1/p' |
      head -n 1
  )"
  [[ -n "${REQUESTED_VERSION}" ]] ||
    fail "Could not determine the latest GitHub release."
fi

if [[ ! "${REQUESTED_VERSION}" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  fail "Version must look like v1.0.0 or be latest."
fi

VERSION_NUMBER="${REQUESTED_VERSION#v}"
ASSET_NAME="local-ip-manager-${VERSION_NUMBER}-linux-x64.zip"
DOWNLOAD_URL="https://github.com/${REPOSITORY}/releases/download/${REQUESTED_VERSION}/${ASSET_NAME}"
DOWNLOAD_DIR="$(mktemp -d)"
ARCHIVE_PATH="${DOWNLOAD_DIR}/${ASSET_NAME}"
STAGING_DIR="${INSTALL_DIR}.new"
BACKUP_DIR="${INSTALL_DIR}.previous"
INSTALL_REPLACED=false

finish() {
  local exit_code=$?

  if [[ "${exit_code}" -ne 0 &&
        "${INSTALL_REPLACED}" == true &&
        -d "${BACKUP_DIR}" ]]; then
    printf '\n[INFO] Restoring the previous application version\n' >&2
    systemctl stop "${SERVICE_NAME}.service" >/dev/null 2>&1 || true
    rm -rf "${INSTALL_DIR}"
    mv "${BACKUP_DIR}" "${INSTALL_DIR}"
    systemctl start "${SERVICE_NAME}.service" >/dev/null 2>&1 || true
  fi

  rm -rf "${DOWNLOAD_DIR}" "${STAGING_DIR}"
  exit "${exit_code}"
}
trap finish EXIT

info "Downloading ${APP_NAME} ${REQUESTED_VERSION}"
curl -fL --retry 3 --retry-delay 2   "${DOWNLOAD_URL}"   -o "${ARCHIVE_PATH}"

info "Preparing the service account and persistent data directory"
getent group "${SERVICE_USER}" >/dev/null 2>&1 ||
  groupadd --system "${SERVICE_USER}"

if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
  useradd     --system     --gid "${SERVICE_USER}"     --home-dir "${INSTALL_DIR}"     --no-create-home     --shell /usr/sbin/nologin     "${SERVICE_USER}"
fi

install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 0750 "${DATA_DIR}"

if systemctl list-unit-files "${SERVICE_NAME}.service" >/dev/null 2>&1; then
  systemctl stop "${SERVICE_NAME}.service" || true
fi

# Preserve a database from an older installation that stored it beside the app.
if [[ -f "${INSTALL_DIR}/IpManagerDatabase.db" &&
      ! -f "${DATA_DIR}/IpManagerDatabase.db" ]]; then
  info "Moving the existing database to persistent storage"
  cp -a "${INSTALL_DIR}/IpManagerDatabase.db"* "${DATA_DIR}/"
  chown "${SERVICE_USER}:${SERVICE_USER}" "${DATA_DIR}/IpManagerDatabase.db"*
fi

info "Installing application files"
rm -rf "${STAGING_DIR}" "${BACKUP_DIR}"
mkdir -p "${STAGING_DIR}"
unzip -q "${ARCHIVE_PATH}" -d "${STAGING_DIR}"

[[ -f "${STAGING_DIR}/LocalIPManager" ]] ||
  fail "The release archive does not contain the LocalIPManager executable."

chmod 0755 "${STAGING_DIR}/LocalIPManager"
chown -R root:root "${STAGING_DIR}"

if [[ -d "${INSTALL_DIR}" ]]; then
  mv "${INSTALL_DIR}" "${BACKUP_DIR}"
fi
mv "${STAGING_DIR}" "${INSTALL_DIR}"
INSTALL_REPLACED=true

info "Creating the systemd service"
cat >"/etc/systemd/system/${SERVICE_NAME}.service" <<EOF
[Unit]
Description=Local IP Manager
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
User=localip
Group=localip
WorkingDirectory=/opt/local-ip-manager
ExecStart=/opt/local-ip-manager/LocalIPManager
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=http://0.0.0.0:5000"
Environment="ConnectionStrings__DefaultConnection=Data Source=/var/lib/local-ip-manager/IpManagerDatabase.db"
Environment="HOME=/var/lib/local-ip-manager"
Restart=on-failure
RestartSec=5
AmbientCapabilities=CAP_NET_RAW
CapabilityBoundingSet=CAP_NET_RAW
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/var/lib/local-ip-manager

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now "${SERVICE_NAME}.service"

info "Checking the application service"
for _ in {1..15}; do
  if systemctl is-active --quiet "${SERVICE_NAME}.service"; then
    break
  fi
  sleep 1
done

if ! systemctl is-active --quiet "${SERVICE_NAME}.service"; then
  journalctl -u "${SERVICE_NAME}.service" -n 40 --no-pager
  fail "The application service did not start."
fi

info "Checking the network-scan capability"
AMBIENT_CAPABILITIES="$(
  systemctl show "${SERVICE_NAME}.service"     --property=AmbientCapabilities     --value
)"

if [[ "${AMBIENT_CAPABILITIES}" != *"cap_net_raw"* ]]; then
  fail "The service does not have CAP_NET_RAW."
fi

if ! systemd-run   --quiet   --wait   --pipe   --collect   --uid="${SERVICE_USER}"   --property=AmbientCapabilities=CAP_NET_RAW   --property=CapabilityBoundingSet=CAP_NET_RAW   /usr/bin/ping -c 1 -W 2 127.0.0.1 >/dev/null; then
  fail "The container did not permit CAP_NET_RAW for the service user."
fi

rm -rf "${BACKUP_DIR}"
INSTALL_REPLACED=false
apt-get clean
rm -rf /var/lib/apt/lists/*

CONTAINER_IP="$(hostname -I | awk '{print $1}')"

printf '\n%s %s installed successfully.\n' "${APP_NAME}" "${REQUESTED_VERSION}"
printf 'Open: http://%s:5000\n' "${CONTAINER_IP:-CONTAINER-IP}"
printf 'Database: %s/IpManagerDatabase.db\n' "${DATA_DIR}"
