#!/usr/bin/env python3
import errno, os, pty, select, signal, sys, time
if len(sys.argv) != 6:
    raise SystemExit('usage: driver.py WRAPPER MODE OUT GOOD BAD')
wrapper, mode, output, good, bad = sys.argv[1:]
pid, fd = pty.fork()
if pid == 0:
    os.execlp('bash', 'bash', wrapper)
buf = bytearray(); scan = 0; answered = 0; status = None; deadline = time.monotonic()+25
while status is None:
    if time.monotonic() > deadline:
        os.kill(pid, signal.SIGKILL); _, status = os.waitpid(pid, 0); break
    ready, _, _ = select.select([fd], [], [], .1)
    if ready:
        try: chunk = os.read(fd, 4096)
        except OSError as exc:
            if exc.errno != errno.EIO: raise
            chunk = b''
        if chunk:
            buf.extend(chunk)
            while True:
                p = buf.find(b'password for ', scan)
                if p < 0: break
                colon = buf.find(b':', p)
                if colon < 0: break
                scan = colon + 1
                answer = good if mode == 'good' else bad if mode == 'bad' else None
                if answer is not None:
                    os.write(fd, answer.encode()+b'\n'); answered += 1
    try: waited, code = os.waitpid(pid, os.WNOHANG)
    except ChildProcessError: waited, code = pid, 0
    if waited == pid: status = code
with open(output, 'wb') as f: f.write(buf)
rc = os.waitstatus_to_exitcode(status)
print(f'PTY_DRIVER child_rc={rc} prompts_answered={answered} output={output}')
sys.exit(0 if rc == 0 else rc)
