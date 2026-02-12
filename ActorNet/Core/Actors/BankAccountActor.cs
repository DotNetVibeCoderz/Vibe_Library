using System;
using System.Threading.Tasks;

namespace ActorNet.Core.Actors
{
    // Messages for Bank Account
    public record Deposit(decimal Amount);
    public record Withdraw(decimal Amount);
    public record GetBalance();
    public record BalanceResponse(decimal Amount);

    public class BankAccountActor : VirtualActor
    {
        private decimal _balance;

        public override async Task ActivateAsync()
        {
            await base.ActivateAsync();
            // Simulate loading state from database
            // In a real app, we'd use a persistence provider here
            _balance = 0; 
            Console.WriteLine($"[State] Loaded Balance for {Id}: {_balance:C}");
        }

        public override async Task ReceiveAsync(IActorContext context, object message)
        {
            switch (message)
            {
                case Deposit d:
                    _balance += d.Amount;
                    Console.WriteLine($"[{Id}] Deposited {d.Amount:C}. New Balance: {_balance:C}");
                    break;

                case Withdraw w:
                    if (_balance >= w.Amount)
                    {
                        _balance -= w.Amount;
                        Console.WriteLine($"[{Id}] Withdrew {w.Amount:C}. New Balance: {_balance:C}");
                    }
                    else
                    {
                        Console.WriteLine($"[{Id}] Insufficient funds for withdrawal of {w.Amount:C}. Current: {_balance:C}");
                    }
                    break;

                case GetBalance:
                    Console.WriteLine($"[{Id}] Balance query received.");
                    context.Reply(new BalanceResponse(_balance));
                    break;

                default:
                    Console.WriteLine($"[{Id}] Unknown message: {message}");
                    break;
            }
        }
    }
}