using System;
using System.Collections.Generic;

namespace Eco.Mods.Companies.GameActions
{
    using Gameplay.Players;
    using Gameplay.Economy;
    using Gameplay.GameActions;
    using Gameplay.Civics;
    using Gameplay.Settlements;

    using Shared.Localization;
    using Shared.Networking;
    using System.Linq;

    [Eco, LocCategory("Companies"), LocDescription("Triggered when a company receives currency to the company bank account."), CannotBePrevented]
    public class CompanyIncome : MoneyGameAction
    {
        [Eco, LocDescription("The bank account the money came from."), CanAutoAssign] public override BankAccount SourceBankAccount { get; set; }
        [Eco, LocDescription("The bank account the money went to.")] public override BankAccount TargetBankAccount { get; set; }
        [Eco, LocDescription("The currency of the transfer."), CanAutoAssign] public override Currency Currency { get; set; }
        [Eco, LocDescription("The amount of money transfered.")] public override float CurrencyAmount { get; set; }
        [Eco, LocDescription("The legal person of the company who received the money."), CanAutoAssign] public User ReceiverLegalPerson { get; set; }

        public override IEnumerable<Settlement> SettlementScopes => ReceiverLegalPerson?.AllCitizenships ?? Enumerable.Empty<Settlement>(); //Scope based on company citizenship
    }
}
