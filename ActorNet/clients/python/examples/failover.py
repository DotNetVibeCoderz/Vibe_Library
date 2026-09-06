"""Checks that a client steps over an address that is not answering.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Start a node first::

    dotnet run --project src/ActorNet.Cli -- run --port 9000

Then::

    python clients/python/examples/failover.py

The first endpoint is deliberately dead. A client that reported the whole cluster unreachable
because the first node it tried was down would be no better than a client with one endpoint.
"""

from __future__ import annotations

import asyncio
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from actornet import ActorNetClient  # noqa: E402

LIVE = f"{os.environ.get('ACTORNET_HOST', '127.0.0.1')}:{os.environ.get('ACTORNET_PORT', '9000')}"
DEAD = "127.0.0.1:9099"


async def main() -> None:
    async with ActorNetClient(endpoints=[DEAD, LIVE], client_id="python-failover") as client:
        if client.connected_to != LIVE:
            raise SystemExit(f"connected to {client.connected_to}, expected {LIVE}")

        print(f"stepped over {DEAD} and connected to {client.connected_to}")

        account = "BankAccountActor/python-failover"
        await client.tell(account, "bank.deposit", {"Amount": 25, "Reference": "failover"})

        statement = await client.ask(account, "bank.get-statement", {"MaxEntries": 1})
        print(f"the surviving node answered: balance {statement.payload['Balance']}")


asyncio.run(main())
