import os
import shutil
import subprocess
import sys
import tempfile
import time

with tempfile.TemporaryFile() as fd:
    xvfb = os.environ.get('ARMSTUDIO_XVFB') or shutil.which('Xvfb')
    if not xvfb:
        raise RuntimeError('Set ARMSTUDIO_XVFB to Xvfb or install Xvfb on PATH')
    proc = subprocess.Popen(
        [xvfb, '-displayfd', str(fd.fileno()),
         '-screen', '0', '1600x1100x24', '-nolisten', 'tcp', '-ac'],
        pass_fds=(fd.fileno(),), stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            fd.seek(0)
            display = fd.read().decode().strip()
            if display:
                break
            if proc.poll() is not None:
                raise RuntimeError('Xvfb failed')
            time.sleep(.1)
        else:
            raise RuntimeError('Xvfb startup timed out')
        os.environ['DISPLAY'] = ':' + display
        sys.exit(subprocess.run(sys.argv[1:], timeout=55).returncode)
    finally:
        proc.terminate()
        proc.wait()
