// Checks that a client steps over an address that is not answering.
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Start a node first:
//   dotnet run --project src/ActorNet.Cli -- run --port 9000
//
// Then:
//   node clients/nodejs/examples/failover.js
//
// The first endpoint is deliberately dead. A client that reported the whole cluster unreachable
// because the first node it tried was down would be no better than a client with one endpoint.

'use strict';

const { ActorNetClient } = require('../actornet');

const live = `${process.env.ACTORNET_HOST || '127.0.0.1'}:${process.env.ACTORNET_PORT || 9000}`;
const dead = '127.0.0.1:9099';

async function main() {
  const client = new ActorNetClient({ endpoints: [dead, live], clientId: 'nodejs-failover' });

  try {
    await client.connect();

    if (client.connectedTo !== live) {
      throw new Error(`connected to ${client.connectedTo}, expected ${live}`);
    }

    console.log(`stepped over ${dead} and connected to ${client.connectedTo}`);

    const account = 'BankAccountActor/nodejs-failover';
    await client.tell(account, 'bank.deposit', { Amount: 25, Reference: 'failover' });

    const statement = await client.ask(account, 'bank.get-statement', { MaxEntries: 1 });
    console.log(`the surviving node answered: balance ${statement.payload.Balance}`);
  } finally {
    client.close();
  }
}

main().catch((err) => {
  console.error(err.message);
  process.exit(1);
});
