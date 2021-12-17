using System;

namespace Eco.Mods.Companies.GameActions
{
    using Gameplay.Players;
    using Gameplay.Economy;
    using Gameplay.GameActions;
    using Gameplay.Civics;

    using Shared.Localization;
    using Shared.Networking;

    [Eco, LocCategory("Companies"), LocDescription("Triggered when a company sends currency from the company bank account."), CannotBePrevented]
    public class CompanyExpense : MoneyGameAction
    {
        [Eco, LocDescription("The citizen who transfered the money."), CanAutoAssign] public User Citizen { get; set; }
        [Eco, LocDescription("The bank account the money came from."), CanAutoAssign] public override BankAccount SourceBankAccount { get; set; }
        [Eco, LocDescription("The bank account the money went to.")] public override BankAccount TargetBankAccount { get; set; }
        [Eco, LocDescription("The currency of the transfer."), CanAutoAssign] public override Currency Currency { get; set; }
        [Eco, LocDescription("The amount of money transfered.")] public override float CurrencyAmount { get; set; }
        [Eco, LocDescription("The legal person of the company who sent the money."), CanAutoAssign] public User SenderLegalPerson { get; set; }
    }
}
