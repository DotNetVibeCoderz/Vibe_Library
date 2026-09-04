// Drives the ActorNet banking domain from Node.js.
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Start a node first:
//   dotnet run --project src/ActorNet.Cli -- run --port 9000
//
// Then:
//   node clients/nodejs/examples/banking.js

'use strict';

const { ActorNetClient, ActorNetError } = require('../actornet');

const PORT = Number(process.env.ACTORNET_PORT || 9000);
const HOST = process.env.ACTORNET_HOST || '127.0.0.1';

async function main() {
  const client = new ActorNetClient({ host: HOST, port: PORT, clientId: 'nodejs-example' });

  try {
    await client.connect();
    console.log(`connected to ${HOST}:${PORT} as ${client.clientId}`);

    const account = 'BankAccountActor/nodejs-demo';

    // Aliases, not .NET type names. The node resolves them through its own allow-list, which is
    // exactly what lets a client written in another language address the same actors.
    await client.tell(account, 'bank.deposit', { Amount: 500, Reference: 'opening' });
    await client.tell(account, 'bank.deposit', { Amount: 125.5, Reference: 'salary' });
    console.log('sent two deposits');

    const accepted = await client.ask(account, 'bank.withdraw', { Amount: 90, Reference: 'atm' });
    console.log(`withdrawal ${accepted.alias}: balance now ${accepted.payload.Balance}`);

    const statement = await client.ask(account, 'bank.get-statement', { MaxEntries: 5 });
    console.log(`\nstatement for ${statement.payload.AccountId}`);
    console.log(`  balance      ${statement.payload.Balance}`);
    console.log(`  transactions ${statement.payload.Transactions}`);
    for (const line of statement.payload.Recent) console.log(`  ${line}`);

    // A refusal comes back as a normal reply, not an error: the actor decided, it did not fail.
    const declined = await client.ask(account, 'bank.withdraw', { Amount: 1e6, Reference: 'atm' });
    console.log(`\noverdraft attempt -> ${declined.alias}: ${declined.payload.Reason}`);
  } catch (err) {
    if (err instanceof ActorNetError) {
      console.error(`actornet: ${err.message}`);
      process.exitCode = 1;
    } else {
      throw err;
    }
  } finally {
    client.close();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
