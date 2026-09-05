#!/usr/bin/env bash
# shellcheck shell=bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DRIVER="$ROOT/scripts/verify/remote-exec-pty-driver.py"
for c in python3 ssh ssh-keygen ssh-keyscan sudo useradd userdel chpasswd timeout sed grep base64 tr id getent cut install chown chmod mkdir rm cat sleep tee mktemp; do
  command -v "$c" >/dev/null || { echo "REMOTE_EXEC_PTY_FAIL missing=$c" >&2; exit 1; }
done
[[ -x /usr/sbin/sshd ]] || { echo 'REMOTE_EXEC_PTY_FAIL missing=/usr/sbin/sshd' >&2; exit 1; }
sudo -n true >/dev/null 2>&1 || { echo 'REMOTE_EXEC_PTY_FAIL requires passwordless runner sudo' >&2; exit 1; }
D="$(mktemp -d "${TMPDIR:-/tmp}/hgs-pty.XXXXXX")"; U=hgspty64; P='HgsRemoteExec64!'; BAD='WrongHgs64!'
KEY="$D/client"; BADKEY="$D/bad"; HOSTKEY="$D/host"; KH="$D/known_hosts"; CONF="$D/sshd_config"; LOG="$D/sshd.log"; PIDFILE="$D/sshd.pid"; PORT=''; PID=''; failures=0
fail(){ echo "[FAIL] $*" >&2; failures=$((failures+1)); }; pass(){ echo "[PASS] $*"; }
cleanup(){ [[ -n "$PID" ]] && sudo kill "$PID" >/dev/null 2>&1 || true; sudo rm -f "/etc/sudoers.d/$U" >/dev/null 2>&1 || true; id "$U" >/dev/null 2>&1 && sudo userdel -r "$U" >/dev/null 2>&1 || true; rm -rf "$D"; }
trap cleanup EXIT
id "$U" >/dev/null 2>&1 && sudo userdel -r "$U" >/dev/null 2>&1 || true
sudo useradd --create-home --shell /bin/bash "$U"; printf '%s:%s\n' "$U" "$P" | sudo chpasswd; printf '%s ALL=(ALL:ALL) ALL\n' "$U" | sudo tee "/etc/sudoers.d/$U" >/dev/null; sudo chmod 0440 "/etc/sudoers.d/$U"
ssh-keygen -q -t ed25519 -N '' -f "$KEY"; ssh-keygen -q -t ed25519 -N '' -f "$BADKEY"; ssh-keygen -q -t ed25519 -N '' -f "$HOSTKEY"
HOME_DIR="$(getent passwd "$U"|cut -d: -f6)"; sudo mkdir -p "$HOME_DIR/.ssh"; sudo install -m 0600 "$KEY.pub" "$HOME_DIR/.ssh/authorized_keys"; sudo chown -R "$U:$U" "$HOME_DIR/.ssh"; sudo chmod 0700 "$HOME_DIR/.ssh"
PORT="$(python3 - <<'PY'
import socket
s=socket.socket(); s.bind(('127.0.0.1',0)); print(s.getsockname()[1]); s.close()
PY
)"
cat >"$CONF" <<EOF_CONF
Port $PORT
ListenAddress 127.0.0.1
HostKey $HOSTKEY
PidFile $PIDFILE
AuthorizedKeysFile .ssh/authorized_keys
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
PermitTTY yes
UsePAM yes
StrictModes yes
PrintMotd no
AllowUsers $U
Subsystem sftp internal-sftp
EOF_CONF
sudo mkdir -p /run/sshd; sudo /usr/sbin/sshd -f "$CONF" -E "$LOG"
for _ in {1..50}; do [[ -f "$PIDFILE" ]] && PID="$(cat "$PIDFILE")"; ssh -i "$KEY" -p "$PORT" -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null "$U@127.0.0.1" true >/dev/null 2>&1 && break; sleep .1; done
ssh -i "$KEY" -p "$PORT" -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null "$U@127.0.0.1" true >/dev/null 2>&1 || { cat "$LOG" >&2; exit 1; }
ssh-keyscan -p "$PORT" 127.0.0.1 >"$KH" 2>/dev/null
sshfix(){ ssh -i "$KEY" -p "$PORT" -o BatchMode=yes -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$KH" "$U@127.0.0.1" "$@"; }
reset(){ sshfix 'sudo -k' >/dev/null 2>&1 || true; }
preamble(){ local f="$1" k="$2" tee="${3:-}"; cat >"$f" <<EOF_W
#!/usr/bin/env bash
set -uo pipefail
export DEPLOY_SSH_HOST=127.0.0.1 DEPLOY_SSH_USER='$U' DEPLOY_SSH_KEY='$k' DEPLOY_SSH_PORT='$PORT' DEPLOY_SSH_KNOWN_HOSTS_FILE='$KH' DEPLOY_PROXY_COMMAND='' HAPPYGYMSTATS_REMOTE_EXEC_TEST_DIRECT=1 CASE_TEE='$tee'
source '$ROOT/scripts/lib/remote-exec.sh'
EOF_W
}
ptyrun(){ python3 "$DRIVER" "$1" "$2" "$3" "$P" "$BAD" || true; }
has(){ grep -Fq -- "$2" "$1" && pass "$3" || fail "$3 missing=$2"; }
lacks(){ grep -Fq -- "$2" "$1" && fail "$3 leaked=$2" || pass "$3"; }
# success + stdin + tee + rc + non-echo
reset; W="$D/success.sh"; O="$D/success.out"; T="$D/transcript"; preamble "$W" "$KEY" "$T"; cat >>"$W" <<'EOF_W'
set +e
remote_exec_script --tee "$CASE_TEE" <<'REMOTE'
echo PTY_STDOUT_OK
echo PTY_STDERR_OK >&2
printf 'PAYLOAD_AFTER_SUDO_OK\n'
exit 23
REMOTE
r=$?; printf '__RC__=%d\n' "$r"; exit 0
EOF_W
chmod +x "$W"; ptyrun "$W" good "$O"; has "$O" '__RC__=23' 'remote rc preserved'; has "$O" PTY_STDOUT_OK 'stdout preserved'; has "$O" PTY_STDERR_OK 'stderr preserved'; has "$O" PAYLOAD_AFTER_SUDO_OK 'sudo did not consume payload'; has "$T" PTY_STDOUT_OK 'tee transcript captured'; lacks "$O" "$P" 'password not echoed'; lacks "$T" "$P" 'password not persisted'
# literal nested heredoc
reset; W="$D/literal.sh"; O="$D/literal.out"; preamble "$W" "$KEY"; cat >>"$W" <<'EOF_W'
set +e
remote_exec_script <<'REMOTE'
cat <<'INNER'
$(printf MUST_NOT_RUN)
`printf MUST_NOT_RUN_BT`
$1
INNER
REMOTE
r=$?; printf '__RC__=%d\n' "$r"; exit 0
EOF_W
chmod +x "$W"; ptyrun "$W" good "$O"; has "$O" '$(printf MUST_NOT_RUN)' 'command substitution preserved literally'; has "$O" '`printf MUST_NOT_RUN_BT`' 'backticks preserved literally'; has "$O" '$1' 'positional text preserved'; has "$O" '__RC__=0' 'literal payload succeeded'
# no controlling tty
reset; W="$D/notty.sh"; O="$D/notty.out"; preamble "$W" "$KEY"; cat >>"$W" <<'EOF_W'
set +e
remote_exec_script <<'REMOTE'
echo MUST_NOT_NEED_PASSWORD_STDIN
REMOTE
r=$?; printf '__RC__=%d\n' "$r"; exit 0
EOF_W
chmod +x "$W"; python3 - "$W" "$O" <<'PY'
import subprocess,sys
with open(sys.argv[2],'wb') as f: subprocess.run(['bash',sys.argv[1]],stdin=subprocess.DEVNULL,stdout=f,stderr=subprocess.STDOUT,start_new_session=True,timeout=15)
PY
has "$O" 'no controlling terminal; interactive sudo is not possible' 'no-tty diagnostic'; has "$O" '__RC__=77' 'no-tty sudo failure code'; lacks "$O" "$P" 'no-tty password absent'
# wrong sudo
reset; W="$D/badsudo.sh"; O="$D/badsudo.out"; preamble "$W" "$KEY"; cat >>"$W" <<'EOF_W'
set +e
remote_exec_script <<'REMOTE'
echo SHOULD_NOT_RUN_AFTER_BAD_SUDO
REMOTE
r=$?; printf '__RC__=%d\n' "$r"; exit 0
EOF_W
chmod +x "$W"; ptyrun "$W" bad "$O"; has "$O" REMOTE_SUDO_FAILED 'sudo failure marker'; has "$O" '__RC__=77' 'sudo failure rc'; lacks "$O" "$BAD" 'wrong password not echoed'; lacks "$O" SHOULD_NOT_RUN_AFTER_BAD_SUDO 'payload blocked after sudo failure'
# command-not-found vs auth failure
reset; W="$D/missing.sh"; O="$D/missing.out"; preamble "$W" "$KEY"; cat >>"$W" <<'EOF_W'
set +e
remote_exec_script <<'REMOTE'
definitely_not_a_hgs_command
REMOTE
r=$?; printf '__RC__=%d\n' "$r"; exit 0
EOF_W
chmod +x "$W"; ptyrun "$W" good "$O"; has "$O" '__RC__=127' 'remote command-not-found rc'
W="$D/auth.sh"; O="$D/auth.out"; preamble "$W" "$BADKEY"; cat >>"$W" <<'EOF_W'
set +e
remote_exec_script <<'REMOTE'
echo SHOULD_NEVER_AUTH
REMOTE
r=$?; printf '__RC__=%d\n' "$r"; exit 0
EOF_W
chmod +x "$W"; ptyrun "$W" none "$O"; has "$O" '__RC__=255' 'ssh auth failure rc'; lacks "$O" SHOULD_NEVER_AUTH 'auth failure blocks payload'
# three historical broken shapes: stdin collision, captured prompt, filtered prompt
reset; O="$D/old-stdin.out"; set +e; printf 'echo HISTORICAL_PAYLOAD\n' | timeout 4s ssh -tt -i "$KEY" -p "$PORT" -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$KH" "$U@127.0.0.1" 'sudo -k; sudo -v && bash -s' >"$O" 2>&1; r=$?; set -e; ((r!=0)) && ! grep -Fq HISTORICAL_PAYLOAD "$O" && pass 'historical stdin collision rejected within timeout' || fail "historical stdin collision escaped rc=$r"
for kind in capture filter; do
  reset; INNER="$D/$kind-inner.sh"; WR="$D/$kind-wrap.sh"; O="$D/$kind.out"
  if [[ "$kind" == capture ]]; then
    cat >"$INNER" <<'EOF_I'
#!/usr/bin/env bash
x="$(ssh -tt -i "$K" -p "$PORT" -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$KH" "$U@127.0.0.1" 'sudo -k; sudo -v')"; printf '%s\n' "$x"
EOF_I
  else
    cat >"$INNER" <<'EOF_I'
#!/usr/bin/env bash
ssh -tt -i "$K" -p "$PORT" -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$KH" "$U@127.0.0.1" 'sudo -k; sudo -v' | sed 's/^/  /'
EOF_I
  fi
  chmod +x "$INNER"; cat >"$WR" <<EOF_W
#!/usr/bin/env bash
export U='$U' K='$KEY' PORT='$PORT' KH='$KH'; set +e; timeout 4s bash '$INNER'; r=\$?; printf '__BROKEN_RC__=%d\n' "\$r"; exit 0
EOF_W
  chmod +x "$WR"; ptyrun "$WR" none "$O"; has "$O" '__BROKEN_RC__=124' "historical $kind prompt shape bounded by timeout"
done
# test seam cannot bypass proxy for non-loopback
O="$D/guard.out"; set +e; bash -c "export DEPLOY_SSH_HOST=example.invalid DEPLOY_SSH_USER=x DEPLOY_SSH_KEY='$KEY' DEPLOY_PROXY_COMMAND='' HAPPYGYMSTATS_REMOTE_EXEC_TEST_DIRECT=1; source '$ROOT/scripts/lib/remote-exec.sh'; printf 'true\\n' | remote_exec_script" >"$O" 2>&1; r=$?; set -e; ((r==2)) && pass 'direct seam rejects non-loopback before network' || fail "non-loopback seam rc=$r"; has "$O" 'direct SSH test seam is restricted to loopback hosts' 'loopback guard diagnostic'
if ((failures)); then echo "REMOTE_EXEC_PTY_FAIL failures=$failures" >&2; cat "$LOG" >&2 || true; exit 1; fi
echo 'REMOTE_EXEC_PTY_PASS failures=0'
