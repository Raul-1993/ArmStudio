#!/usr/bin/env python3
"""Reproducible Linux PTY + Arduino host-stub checks; no robot connected."""
import argparse
import os
from pathlib import Path
import pty
import select
import shutil
import subprocess
import tempfile
import time
import tty


def require_tool(name):
    executable = shutil.which(name)
    if executable is None:
        raise SystemExit("Required tool not found: " + name)
    return executable


def serial_scenario(mono, executable, mode):
    master, slave = pty.openpty()
    tty.setraw(slave)
    process = subprocess.Popen(
        [mono, str(executable), os.ttyname(slave), mode],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    buffered = b""
    deadline = time.monotonic() + 12
    try:
        while process.poll() is None:
            if time.monotonic() > deadline:
                raise RuntimeError("Serial scenario exceeded 12 seconds: " + mode)
            ready, _, _ = select.select([master], [], [], 0.05)
            if not ready:
                continue
            buffered += os.read(master, 4096)
            while b"\n" in buffered:
                raw, buffered = buffered.split(b"\n", 1)
                line = raw.decode("ascii").strip()
                if mode == "silent":
                    continue
                if line == "HELLO 1":
                    reply = "ARMSTUDIO 2 FUTURE" if mode == "mismatch" else "ARMSTUDIO 1 DIAGNOSTIC"
                elif line.endswith(" PING"):
                    sequence = "N999" if mode == "late" else line.split()[0]
                    reply = "OK " + sequence + " PONG"
                elif line.endswith(" STATUS"):
                    reply = "OK " + line.split()[0] + " DIAGNOSTIC"
                else:
                    raise RuntimeError("Unexpected transmitted frame: " + repr(line))
                os.write(master, (reply + "\n").encode("ascii"))
        output = process.communicate(timeout=1)[0].strip()
        print(mode + ": " + output, flush=True)
        if process.returncode != 0:
            raise RuntimeError("Serial scenario failed: " + mode)
    finally:
        if process.poll() is None:
            process.kill()
            process.communicate()
        os.close(master)
        os.close(slave)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mono", help="Path to the Mono runtime (defaults to PATH)")
    parser.add_argument("--mcs-exe", help="Path to Mono's mcs.exe compiler")
    parser.add_argument("--cxx", help="Path to a C++ compiler (defaults to g++)")
    args = parser.parse_args()
    mono = args.mono or require_tool("mono")
    cxx = args.cxx or require_tool("g++")
    compiler = Path(args.mcs_exe) if args.mcs_exe else Path(mono).resolve().parent.parent / "lib/mono/4.5/mcs.exe"
    if not compiler.is_file():
        raise SystemExit("mcs.exe not found; pass --mcs-exe /path/to/mcs.exe")
    here = Path(__file__).resolve().parent
    core = here.parent / "ArmStudio.Core"
    with tempfile.TemporaryDirectory(prefix="armstudio-transport-checks-") as temporary:
        build = Path(temporary)
        executable = build / "SerialIntegrationChecks.exe"
        subprocess.run([
            mono, str(compiler), "-langversion:7.2", "-sdk:4.8", "-r:System.Runtime.Serialization",
            str(core / "Models.cs"), str(core / "JointState.cs"), str(core / "MotionGeometry.cs"), str(core / "Transports.cs"),
            str(here / "SerialIntegrationChecks.cs"), "-out:" + str(executable)
        ], check=True, timeout=60)
        for mode in ["basic", "cancel", "silent", "mismatch", "late"]:
            serial_scenario(mono, executable, mode)
        firmware_checks = build / "FirmwareChecks"
        subprocess.run([
            cxx, "-std=c++11", "-Wall", "-Wextra", "-pedantic",
            "-I" + str(here / "fixtures"), str(here / "FirmwareChecks.cpp"), "-o", str(firmware_checks)
        ], check=True, timeout=60)
        subprocess.run([str(firmware_checks)], check=True, timeout=5)
    print("Transport checks passed. No hardware was accessed.", flush=True)


if __name__ == "__main__":
    main()
