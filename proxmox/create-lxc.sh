#!/usr/bin/env bash
set -Eeuo pipefail

APP_NAME="Local IP Manager"
REPOSITORY="jeffvpascale/local-ip-manager"
INSTALL_SCRIPT_URL="${LOCAL_IP_MANAGER_INSTALL_SCRIPT_URL:-https://raw.githubusercontent.com/${REPOSITORY}/main/proxmox/install.sh}"

HOST_TEMP_SCRIPT=""
CONTAINER_CREATED=false

finish() {
  local exit_code=$?

  if [[ -n "${HOST_TEMP_SCRIPT}" && -f "${HOST_TEMP_SCRIPT}" ]]; then
    rm -f "${HOST_TEMP_SCRIPT}"
  fi

  if [[ "${exit_code}" -ne 0 && "${CONTAINER_CREATED}" == true ]]; then
    printf '\n[ERROR] Installation stopped after creating LXC %s.\n' "${CONTAINER_ID}" >&2
    printf '[ERROR] The container was left in place for inspection.\n' >&2
  fi

  exit "${exit_code}"
}
trap finish EXIT

DEFAULT_HOSTNAME="local-ip-manager"
DEFAULT_CORES=1
DEFAULT_MEMORY_MB=512
DEFAULT_SWAP_MB=512
DEFAULT_DISK_GB=4
DEFAULT_BRIDGE="vmbr0"
DEFAULT_OS_VERSION=12
DEFAULT_UNPRIVILEGED=1
DEFAULT_START_ON_BOOT=1
DEFAULT_RELEASE_VERSION="latest"

info() {
  printf '\n[INFO] %s\n' "$1"
}

fail() {
  printf '\n[ERROR] %s\n' "$1" >&2
  exit 1
}

if [[ "${EUID}" -ne 0 ]]; then
  fail "Run this script as root from the Proxmox host shell."
fi

REQUIRED_COMMANDS=(curl pveversion pvesh pvesm pveam pct)

for command_name in "${REQUIRED_COMMANDS[@]}"; do
  command -v "${command_name}" >/dev/null 2>&1 ||
    fail "Required Proxmox command not found: ${command_name}"
done

info "Proxmox host checks passed"
NEXT_AVAILABLE_ID="$(pvesh get /cluster/nextid)"

if [[ ! "${NEXT_AVAILABLE_ID}" =~ ^[0-9]+$ ]]; then
  fail "Proxmox did not return a valid next container ID."
fi

CONTAINER_ID="${NEXT_AVAILABLE_ID}"
HOSTNAME="${DEFAULT_HOSTNAME}"
CORES="${DEFAULT_CORES}"
MEMORY_MB="${DEFAULT_MEMORY_MB}"
SWAP_MB="${DEFAULT_SWAP_MB}"
DISK_GB="${DEFAULT_DISK_GB}"
BRIDGE="${DEFAULT_BRIDGE}"
NETWORK_MODE="dhcp"
STATIC_IPV4_CIDR=""
IPV4_GATEWAY=""
DNS_SERVER=""
OS_VERSION="${DEFAULT_OS_VERSION}"
UNPRIVILEGED="${DEFAULT_UNPRIVILEGED}"
START_ON_BOOT="${DEFAULT_START_ON_BOOT}"
RELEASE_VERSION="${DEFAULT_RELEASE_VERSION}"

prompt_with_default() {
  local prompt_text="$1"
  local default_value="$2"
  local entered_value

  printf '%s [%s]: ' "${prompt_text}" "${default_value}" >&2
  read -r entered_value
  printf '%s' "${entered_value:-${default_value}}"
}

prompt_required() {
  local prompt_text="$1"
  local entered_value

  printf '%s: ' "${prompt_text}" >&2
  read -r entered_value

  [[ -n "${entered_value}" ]] ||
    fail "${prompt_text} is required."

  printf '%s' "${entered_value}"
}

require_positive_integer() {
  local label="$1"
  local value="$2"

  [[ "${value}" =~ ^[1-9][0-9]*$ ]] ||
    fail "${label} must be a positive whole number."
}

require_nonnegative_integer() {
  local label="$1"
  local value="$2"

  [[ "${value}" =~ ^[0-9]+$ ]] ||
    fail "${label} must be zero or a positive whole number."
}

is_ipv4_address() {
  local address="$1"
  local octets
  local octet
  local IFS=.

  read -r -a octets <<<"${address}"
  [[ "${#octets[@]}" -eq 4 ]] || return 1

  for octet in "${octets[@]}"; do
    [[ "${octet}" =~ ^[0-9]{1,3}$ ]] || return 1
    ((10#${octet} <= 255)) || return 1
  done
}

is_ipv4_cidr() {
  local value="$1"
  local address
  local prefix

  [[ "${value}" == */* ]] || return 1

  address="${value%/*}"
  prefix="${value##*/}"

  is_ipv4_address "${address}" || return 1
  [[ "${prefix}" =~ ^[0-9]{1,2}$ ]] || return 1
  ((10#${prefix} <= 32))
}

printf '\n%s container setup\n' "${APP_NAME}"
printf '  1) Standard settings (recommended)\n'
printf '  2) Advanced settings\n'
printf 'Choose an option [1]: '
read -r SETUP_CHOICE
SETUP_CHOICE="${SETUP_CHOICE:-1}"

case "${SETUP_CHOICE}" in
  1)
    ;;
  2)
    CONTAINER_ID="$(prompt_with_default "Container ID" "${CONTAINER_ID}")"
    HOSTNAME="$(prompt_with_default "Hostname" "${HOSTNAME}")"
    CORES="$(prompt_with_default "CPU cores" "${CORES}")"
    MEMORY_MB="$(prompt_with_default "Memory in MB" "${MEMORY_MB}")"
    SWAP_MB="$(prompt_with_default "Swap in MB" "${SWAP_MB}")"
    DISK_GB="$(prompt_with_default "Disk size in GB" "${DISK_GB}")"
    BRIDGE="$(prompt_with_default "Network bridge" "${BRIDGE}")"
    NETWORK_MODE="$(prompt_with_default "Network mode (dhcp or static)" "${NETWORK_MODE}")"
    NETWORK_MODE="${NETWORK_MODE,,}"

    if [[ "${NETWORK_MODE}" == "static" ]]; then
      STATIC_IPV4_CIDR="$(prompt_required "Static IPv4 address with CIDR (for example 192.168.1.17/24)")"
      IPV4_GATEWAY="$(prompt_required "IPv4 gateway (for example 192.168.1.1)")"
      DNS_SERVER="$(prompt_with_default "DNS server" "${IPV4_GATEWAY}")"
    fi

    OS_VERSION="$(prompt_with_default "Debian version (12 or 13)" "${OS_VERSION}")"
    RELEASE_VERSION="$(prompt_with_default "Application version" "${RELEASE_VERSION}")"
    ;;
  *)
    fail "Choose 1 for standard settings or 2 for advanced settings."
    ;;
esac

require_positive_integer "Container ID" "${CONTAINER_ID}"
require_positive_integer "CPU cores" "${CORES}"
require_positive_integer "Memory" "${MEMORY_MB}"
require_nonnegative_integer "Swap" "${SWAP_MB}"
require_positive_integer "Disk size" "${DISK_GB}"

case "${NETWORK_MODE}" in
  dhcp)
    NETWORK_CONFIG="name=eth0,bridge=${BRIDGE},ip=dhcp,type=veth"
    ;;
  static)
    is_ipv4_cidr "${STATIC_IPV4_CIDR}" ||
      fail "Static IPv4 address must use valid CIDR notation, such as 192.168.1.17/24."
    is_ipv4_address "${IPV4_GATEWAY}" ||
      fail "IPv4 gateway is not valid."
    is_ipv4_address "${DNS_SERVER}" ||
      fail "DNS server is not a valid IPv4 address."

    NETWORK_CONFIG="name=eth0,bridge=${BRIDGE},ip=${STATIC_IPV4_CIDR},gw=${IPV4_GATEWAY},type=veth"
    ;;
  *)
    fail "Network mode must be dhcp or static."
    ;;
esac

if [[ "${HOSTNAME}" != +([a-zA-Z0-9-]) ||
      "${HOSTNAME}" == -* ||
      "${HOSTNAME}" == *- ]]; then
  fail "Hostname may contain letters, numbers, and hyphens, and cannot begin or end with a hyphen."
fi

if [[ "${OS_VERSION}" != "12" && "${OS_VERSION}" != "13" ]]; then
  fail "Debian version must be 12 or 13."
fi

if [[ "${RELEASE_VERSION}" != "latest" &&
      ! "${RELEASE_VERSION}" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  fail "Application version must look like v1.0.0 or be latest."
fi

if ! pvesh get /cluster/nextid --vmid "${CONTAINER_ID}" >/dev/null 2>&1; then
  fail "Container or virtual-machine ID ${CONTAINER_ID} is already in use."
fi

if [[ ! -d "/sys/class/net/${BRIDGE}" ]]; then
  fail "Network bridge ${BRIDGE} does not exist on this Proxmox host."
fi

choose_storage() {
  local storage_purpose="$1"
  shift
  local storage_options=("$@")
  local selected_index

  if [[ "${#storage_options[@]}" -eq 1 || "${SETUP_CHOICE}" == "1" ]]; then
    printf '%s' "${storage_options[0]}"
    return
  fi

  printf '\nAvailable %s storage:\n' "${storage_purpose}" >&2
  for index in "${!storage_options[@]}"; do
    printf '  %s) %s\n' "$((index + 1))" "${storage_options[index]}" >&2
  done

  printf 'Choose storage [1]: ' >&2
  read -r selected_index
  selected_index="${selected_index:-1}"

  if [[ ! "${selected_index}" =~ ^[1-9][0-9]*$ ||
        "${selected_index}" -gt "${#storage_options[@]}" ]]; then
    fail "Invalid storage selection."
  fi

  printf '%s' "${storage_options[selected_index - 1]}"
}

mapfile -t ROOT_STORAGES < <(
  pvesm status -content rootdir |
    awk 'NR > 1 && $3 == "active" { print $1 }'
)

if [[ "${#ROOT_STORAGES[@]}" -eq 0 ]]; then
  fail "No active Proxmox storage supports LXC root disks."
fi

mapfile -t TEMPLATE_STORAGES < <(
  pvesm status -content vztmpl |
    awk 'NR > 1 && $3 == "active" { print $1 }'
)

if [[ "${#TEMPLATE_STORAGES[@]}" -eq 0 ]]; then
  fail "No active Proxmox storage supports container templates."
fi

ROOT_STORAGE="$(
  choose_storage "container-disk" "${ROOT_STORAGES[@]}"
)"
TEMPLATE_STORAGE="$(
  choose_storage "template" "${TEMPLATE_STORAGES[@]}"
)"

TEMPLATE_NAME="$(
  pveam available --section system |
    awk -v pattern="debian-${OS_VERSION}-standard_" '
      $2 ~ pattern && $2 ~ /amd64/ { print $2 }
    ' |
    sort -V |
    tail -n 1
)"

if [[ -z "${TEMPLATE_NAME}" ]]; then
  fail "No Debian ${OS_VERSION} amd64 template was found in the current Proxmox catalog."
fi

TEMPLATE_VOLUME="${TEMPLATE_STORAGE}:vztmpl/${TEMPLATE_NAME}"

printf '\nProposed container settings:\n'
printf '  Container ID:  %s\n' "${CONTAINER_ID}"
printf '  Hostname:      %s\n' "${HOSTNAME}"
printf '  OS:            Debian %s\n' "${OS_VERSION}"
printf '  CPU:           %s core(s)\n' "${CORES}"
printf '  Memory:        %s MB\n' "${MEMORY_MB}"
printf '  Swap:          %s MB\n' "${SWAP_MB}"
printf '  Disk:          %s GB on %s\n' "${DISK_GB}" "${ROOT_STORAGE}"
printf '  Template:      %s\n' "${TEMPLATE_VOLUME}"
printf '  Bridge:        %s\n' "${BRIDGE}"

if [[ "${NETWORK_MODE}" == "dhcp" ]]; then
  printf '  Network:       DHCP\n'
else
  printf '  Network:       Static %s\n' "${STATIC_IPV4_CIDR}"
  printf '  Gateway:       %s\n' "${IPV4_GATEWAY}"
  printf '  DNS:           %s\n' "${DNS_SERVER}"
fi

printf '  Unprivileged:  Yes\n'
printf '  Start at boot: Yes\n'
printf '  App version:   %s\n' "${RELEASE_VERSION}"

printf '\nContinue with these settings? [Y/n]: '
read -r CONFIRM_SETTINGS
CONFIRM_SETTINGS="${CONFIRM_SETTINGS:-y}"

case "${CONFIRM_SETTINGS}" in
  y | Y | yes | YES | Yes)
    ;;
  *)
    printf '\nSetup cancelled. No container was created.\n'
    exit 0
    ;;
esac

info "Checking the Debian container template"
if ! pveam list "${TEMPLATE_STORAGE}" |
  awk 'NR > 1 { print $1 }' |
  grep -Fxq "${TEMPLATE_VOLUME}"; then
  info "Downloading ${TEMPLATE_NAME}"
  pveam update
  pveam download "${TEMPLATE_STORAGE}" "${TEMPLATE_NAME}"
else
  info "Using the existing ${TEMPLATE_NAME} template"
fi

if ! pvesh get /cluster/nextid --vmid "${CONTAINER_ID}" >/dev/null 2>&1; then
  fail "Container or virtual-machine ID ${CONTAINER_ID} became unavailable."
fi

info "Creating LXC ${CONTAINER_ID}"
PCT_CREATE_ARGUMENTS=(
  "${CONTAINER_ID}"
  "${TEMPLATE_VOLUME}"
  --hostname "${HOSTNAME}"
  --cores "${CORES}"
  --memory "${MEMORY_MB}"
  --swap "${SWAP_MB}"
  --rootfs "${ROOT_STORAGE}:${DISK_GB}"
  --net0 "${NETWORK_CONFIG}"
  --unprivileged "${UNPRIVILEGED}"
  --onboot "${START_ON_BOOT}"
  --ostype debian
)

if [[ "${NETWORK_MODE}" == "static" ]]; then
  PCT_CREATE_ARGUMENTS+=(--nameserver "${DNS_SERVER}")
fi

pct create "${PCT_CREATE_ARGUMENTS[@]}"
CONTAINER_CREATED=true

info "Starting LXC ${CONTAINER_ID}"
pct start "${CONTAINER_ID}"

info "Waiting for the container network"
CONTAINER_IP=""

for _ in {1..30}; do
  CONTAINER_IP="$(
    pct exec "${CONTAINER_ID}" -- \
      ip -4 -o address show dev eth0 scope global 2>/dev/null |
      awk '{ split($4, address, "/"); print address[1]; exit }' ||
      true
  )"

  if [[ -n "${CONTAINER_IP}" ]]; then
    break
  fi

  sleep 2
done

if [[ -z "${CONTAINER_IP}" ]]; then
  fail "LXC ${CONTAINER_ID} was created, but its IPv4 address did not become available."
fi

info "Downloading the container installer"
HOST_TEMP_SCRIPT="$(mktemp /tmp/local-ip-manager-install.XXXXXX.sh)"
curl -fL --retry 3 --retry-delay 2 \
  "${INSTALL_SCRIPT_URL}" \
  -o "${HOST_TEMP_SCRIPT}"

info "Copying the installer into LXC ${CONTAINER_ID}"
pct push \
  "${CONTAINER_ID}" \
  "${HOST_TEMP_SCRIPT}" \
  /tmp/local-ip-manager-install.sh \
  --perms 0755

info "Installing ${APP_NAME} inside LXC ${CONTAINER_ID}"
pct exec "${CONTAINER_ID}" -- \
  bash /tmp/local-ip-manager-install.sh "${RELEASE_VERSION}"

pct exec "${CONTAINER_ID}" -- \
  rm -f /tmp/local-ip-manager-install.sh

rm -f "${HOST_TEMP_SCRIPT}"
HOST_TEMP_SCRIPT=""

info "Checking the web application"
APPLICATION_READY=false

for _ in {1..15}; do
  if curl -fsS --max-time 2 "http://${CONTAINER_IP}:5000/" >/dev/null; then
    APPLICATION_READY=true
    break
  fi

  sleep 2
done

if [[ "${APPLICATION_READY}" != true ]]; then
  pct exec "${CONTAINER_ID}" -- \
    systemctl status local-ip-manager.service --no-pager || true
  fail "Local IP Manager started, but its web page did not respond."
fi

printf '\n%s installed successfully.\n' "${APP_NAME}"
printf 'LXC:  %s (%s)\n' "${CONTAINER_ID}" "${HOSTNAME}"
printf 'Open: http://%s:5000\n' "${CONTAINER_IP}"
printf 'Data: /var/lib/local-ip-manager/IpManagerDatabase.db\n'
