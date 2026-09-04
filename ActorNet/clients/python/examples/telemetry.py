"""Drives the ActorNet telemetry domain from Python.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Start a node first::

    dotnet run --project src/ActorNet.Cli -- run --port 9000

Then::

    python clients/python/examples/telemetry.py
"""

from __future__ import annotations

import asyncio
import os
import random
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from actornet import ActorNetClient, ActorNetError  # noqa: E402

HOST = os.environ.get("ACTORNET_HOST", "127.0.0.1")
PORT = int(os.environ.get("ACTORNET_PORT", "9000"))

DEVICES = 6
READINGS_PER_DEVICE = 40


async def main() -> int:
    rng = random.Random(20260904)

    async with ActorNetClient(host=HOST, port=PORT, client_id="python-example") as client:
        print(f"connected to {HOST}:{PORT} as {client.client_id}")

        # One actor per device. Routing by device id is what gives each device's readings a single
        # writer without any locking on either side.
        for reading in range(READINGS_PER_DEVICE):
            for device in range(DEVICES):
                # Device 2 runs hot, so the alarm path is exercised rather than just described.
                baseline = 78.0 if device == 2 else 42.0
                await client.tell(
                    f"DeviceActor/sensor-{device:03d}",
                    "iot.reading",
                    {
                        "DeviceId": f"sensor-{device:03d}",
                        "Celsius": baseline + rng.random() * 10,
                        "At": datetime.now(timezone.utc).isoformat(),
                    },
                )

        sent = DEVICES * READINGS_PER_DEVICE
        print(f"streamed {sent} readings across {DEVICES} devices\n")

        # Give the mailboxes a moment: a tell returns when the node accepts it, not when the
        # actor has handled it.
        await asyncio.sleep(0.3)

        print(f"{'device':<14}{'latest':>9}{'average':>9}{'min':>8}{'max':>8}{'count':>8}  alarm")
        for device in range(DEVICES):
            reply = await client.ask(f"DeviceActor/sensor-{device:03d}", "iot.get-status", {})
            status = reply.payload
            alarm = "YES" if status["InAlarm"] else "-"
            print(
                f"{status['DeviceId']:<14}"
                f"{status['Latest']:>9.1f}"
                f"{status['Average']:>9.1f}"
                f"{status['Minimum']:>8.1f}"
                f"{status['Maximum']:>8.1f}"
                f"{status['Readings']:>8}"
                f"  {alarm}"
            )

        alarms = await client.ask("AlarmDeskActor/main", "iot.get-alarms", {})
        active = alarms.payload["Devices"]
        print(f"\nalarm desk: {len(active)} active, {alarms.payload['RaisedTotal']} raised in total")
        for device in active:
            print(f"  {device}")

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(asyncio.run(main()))
    except ActorNetError as exc:
        print(f"actornet: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc
